using System.Diagnostics;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// Los fragmentos que enseña la cabina web (src/UpgradeCaptain.Web/Snippets) son código REAL con los comentarios
/// traducidos: cada línea de código tiene que existir en el fichero original (en main o en la tag del paso).
/// Así la diapositiva no se queda atrás cuando cambia el código.
/// </summary>
public class SnippetFidelityTests
{
    private static readonly string Root = DemoPaths.ForLocal().ProjectRoot;

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        string dir = Path.Combine(Root, "src", "UpgradeCaptain.Web", "Snippets");
        foreach (string file in Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.cs").Order(StringComparer.Ordinal) : Enumerable.Empty<string>())
        {
            data.Add(Path.GetFileName(file));
        }

        if (data.Count == 0)
        {
            data.Add("(none)"); // en las tags sin cabina web no hay fragmentos
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryCodeLine_ExistsInTheSource(string name)
    {
        if (name == "(none)")
        {
            return;
        }

        string[] snippet = File.ReadAllLines(Path.Combine(Root, "src", "UpgradeCaptain.Web", "Snippets", name));
        Assert.StartsWith("//@source ", snippet[0]);
        string source = snippet[0]["//@source ".Length..].Trim();
        string? text = ReadSource(source);
        if (text is null)
        {
            return; // tag que no existe en este clon (p. ej. sin la cadena de pasos): no hay con qué comparar
        }

        var sourceLines = new HashSet<string>(text.Split('\n').Select(StripComment).Where(l => l.Length > 0), StringComparer.Ordinal);
        var missing = snippet.Skip(1)
            .Select(CodeOf)
            .Where(code => code.Length > 0 && !code.StartsWith("//", StringComparison.Ordinal))
            .Where(code => !sourceLines.Contains(code))
            .ToList();

        Assert.True(missing.Count == 0, $"{name}: lines not found in {source}:\n{string.Join('\n', missing)}");
    }

    /// <summary>El código de una línea del fragmento: sin //en:, //es: ni //!, recortado.</summary>
    private static string CodeOf(string line)
    {
        int cut = line.Length;
        foreach (string marker in new[] { "//en:", "//es:", "//!" })
        {
            int index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return line[..cut].Trim();
    }

    /// <summary>Una línea del original sin su comentario final (respetando las cadenas, que pueden llevar "//").</summary>
    private static string StripComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && line[i] == '/' && line[i + 1] == '/' && line[..i].Trim().Length > 0)
            {
                return line[..i].Trim();
            }
        }

        return line.Trim();
    }

    /// <summary>Un fichero de main (disco) o de una tag (git show tag:ruta).</summary>
    private static string? ReadSource(string source)
    {
        int colon = source.IndexOf(':');
        if (colon < 0)
        {
            return File.ReadAllText(Path.Combine(Root, source));
        }

        var git = new ProcessStartInfo("git", ["-C", Root, "show", source]) { RedirectStandardOutput = true, RedirectStandardError = true };
        using Process process = Process.Start(git)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }
}
