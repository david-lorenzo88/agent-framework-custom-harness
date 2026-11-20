using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness;

/// <summary>Una regla de CODEOWNERS ya convertida a expresión regular.</summary>
public sealed record CodeOwnersRule(string Pattern, IReadOnlyList<string> Owners, Regex Regex);

/// <summary>
/// .github/CODEOWNERS con la semántica de GitHub: gana la ÚLTIMA regla que coincide; soporta *, ** y
/// patrones de directorio (terminados en /). Una ruta está "protegida" cuando la regla que gana no es
/// la regla por defecto (*).
/// </summary>
public sealed class CodeOwnersPolicy
{
    /// <summary>Tools que escriben ficheros: su argumento fileName es la ruta tocada.</summary>
    public static readonly IReadOnlySet<string> FileWriteTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "file_access_write", "file_access_replace", "file_access_replace_lines", "file_access_delete",
    };

    private readonly IReadOnlyList<CodeOwnersRule> _rules;

    private CodeOwnersPolicy(IReadOnlyList<CodeOwnersRule> rules) => this._rules = rules;

    public IReadOnlyList<CodeOwnersRule> Rules => this._rules;

    /// <summary>Lee .github/CODEOWNERS (o CODEOWNERS / docs/CODEOWNERS) del repo. Sin fichero, nadie es propietario.</summary>
    public static CodeOwnersPolicy Load(string repoDir)
    {
        string? file = new[] { ".github/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS" }
            .Select(p => Path.Combine(repoDir, p))
            .FirstOrDefault(File.Exists);
        return Parse(file is null ? string.Empty : File.ReadAllText(file));
    }

    public static CodeOwnersPolicy Parse(string text)
    {
        var rules = new List<CodeOwnersRule>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#', 2)[0].Trim();
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                rules.Add(new CodeOwnersRule(parts[0], parts[1..], ToRegex(parts[0])));
            }
        }

        return new CodeOwnersPolicy(rules);
    }

    /// <summary>
    /// La regla que gana para una ruta (la última que coincide), o null. Una ruta sin "/" final también se
    /// prueba como directorio: "src/Canarias.Tours.Payments" (por ejemplo, en un cd) cae bajo su regla.
    /// </summary>
    public CodeOwnersRule? Match(string path)
    {
        string normalized = Normalize(path);
        string asDirectory = normalized.TrimEnd('/') + "/";
        return this._rules.LastOrDefault(r => r.Regex.IsMatch(normalized) || r.Regex.IsMatch(asDirectory));
    }

    /// <summary>Propietarios de un conjunto de rutas (unión, sin duplicados).</summary>
    public IReadOnlyList<string> OwnersFor(IEnumerable<string> paths) =>
        paths.SelectMany(p => this.Match(p)?.Owners ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Protegida = la regla que gana no es la regla por defecto "*".</summary>
    public bool IsProtected(string path) => this.Match(path) is { Pattern: not "*" };

    /// <summary>¿Es <paramref name="handle"/> propietario de <paramref name="path"/>?</summary>
    public bool IsOwner(string handle, string path) =>
        this.Match(path)?.Owners.Contains(handle, StringComparer.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// Rutas que cuentan para CODEOWNERS en una llamada. En run_shell solo las protegidas: el resto de palabras
    /// con "/" puede ser cualquier cosa (el s/a/b/ de un sed) y no debe impedir aprobar al propietario.
    /// </summary>
    public IReadOnlyList<string> PathsFor(FunctionCallContent call) =>
        call.Name == "run_shell" ? ExtractPaths(call).Where(this.IsProtected).ToList() : ExtractPaths(call);

    /// <summary>
    /// Rutas que toca una llamada a tool (PLAN.md §9.4, ampliado en D-34):
    ///   · file_access_write/replace/replace_lines/delete → argumento fileName
    ///   · run_skill_script de bump_package.cs          → Directory.Packages.props
    ///   · run_shell → no hay forma fiable de saber qué hace un comando, así que somos conservadores:
    ///                 cuenta como tocada CUALQUIER ruta que el comando nombre (sed -i, perl -pi, cd …).
    ///                 Es una heurística: lo que no nombra la ruta (find … -exec, variables, globs como
    ///                 src/*/X.cs) se escapa, y para eso sigue estando la aprobación en consola.
    /// </summary>
    public static IReadOnlyList<string> ExtractPaths(FunctionCallContent call)
    {
        if (call.Name == "run_shell" && call.Arguments?.TryGetValue("command", out object? command) == true && command is not null)
        {
            return ShellPaths(command.ToString()!);
        }

        if (FileWriteTools.Contains(call.Name) && call.Arguments?.TryGetValue("fileName", out object? fileName) == true && fileName is not null)
        {
            return [Normalize(fileName.ToString()!)];
        }

        if (call.Name == "run_skill_script"
            && call.Arguments?.TryGetValue("scriptName", out object? script) == true
            && script?.ToString()?.EndsWith("bump_package.cs", StringComparison.Ordinal) == true)
        {
            return ["Directory.Packages.props"];
        }

        return [];
    }

    // Separadores de palabras en un comando de shell (espacios, comillas, ; | & < > ( ) ` =).
    private static readonly char[] ShellSeparators = [' ', '\t', '\r', '\n', '\'', '"', ';', '|', '&', '<', '>', '(', ')', '`', '='];

    // Una ruta relativa: segmentos de letras, dígitos, . _ - separados por "/" (al menos uno).
    private static readonly Regex PathToken = new(@"^\.{0,2}/?[\w.\-]+(/[\w.\-]+)+/?$", RegexOptions.CultureInvariant);

    /// <summary>Palabras de un comando de shell que parecen rutas (con al menos una "/"), normalizadas.</summary>
    internal static IReadOnlyList<string> ShellPaths(string command) =>
        command.Split(ShellSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => PathToken.IsMatch(token))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Ruta relativa al repo, con / y sin "./" ni "/" inicial.</summary>
    public static string Normalize(string path)
    {
        string p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p.TrimStart('/');
    }

    /// <summary>
    /// Patrón de CODEOWNERS (semántica gitignore) → regex sobre la ruta relativa:
    ///   /src/Pagos/  → anclado a la raíz, directorio y todo lo que cuelga
    ///   docs/        → un directorio docs en cualquier nivel
    ///   *.cs         → sin barra: cualquier nivel
    ///   src/**/x.cs  → ** = cualquier número de directorios
    /// </summary>
    internal static Regex ToRegex(string pattern)
    {
        bool directory = pattern.EndsWith('/');
        string p = pattern.Trim('/');
        bool anchored = pattern.StartsWith('/') || p.Contains('/');

        var sb = new StringBuilder(anchored ? "^" : "^(?:.*/)?");
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '*' && i + 1 < p.Length && p[i + 1] == '*')
            {
                bool slashAfter = i + 2 < p.Length && p[i + 2] == '/';
                sb.Append(slashAfter ? "(?:.*/)?" : ".*");
                i += slashAfter ? 2 : 1;
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        // Un directorio cubre todo lo que cuelga de él; un patrón sin barra final puede ser fichero o directorio.
        sb.Append(directory ? "/.*$" : "(?:/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
    }
}
