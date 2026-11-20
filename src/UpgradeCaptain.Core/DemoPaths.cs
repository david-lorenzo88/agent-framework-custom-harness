namespace UpgradeCaptain.Core;

/// <summary>
/// Dónde vive cada cosa de la demo. Las tools, el shell y el acceso a ficheros se confinan a <see cref="RepoDir"/>:
/// nunca a este repo (upgrade-captain) ni a $HOME (PLAN.md §5.6).
/// </summary>
/// <param name="ProjectRoot">Raíz de upgrade-captain (local) o $HOME (hosted). Aquí cuelgan state/, sessions/ y agent-file-memory/.</param>
/// <param name="RepoDir">El repo que arregla el agente: demo-workspace/canarias-tours.</param>
/// <param name="AdvisoriesFile">Avisos de seguridad (demo/security/advisories.json).</param>
/// <param name="MigrationNotesDir">Notas de versión de Teide.Geo (demo/feed/notes).</param>
/// <param name="SkillsDir">Skills copiadas junto al ejecutable (bin/.../skills).</param>
public sealed record DemoPaths(
    string ProjectRoot,
    string RepoDir,
    string AdvisoriesFile,
    string MigrationNotesDir,
    string SkillsDir)
{
    private static DemoPaths? s_current;

    /// <summary>Rutas activas. Por defecto, las locales descubiertas a partir del directorio actual.</summary>
    public static DemoPaths Current
    {
        get => s_current ??= ForLocal();
        set => s_current = value;
    }

    /// <summary>Rutas en la máquina del ponente: todo cuelga de la raíz del repo upgrade-captain.</summary>
    public static DemoPaths ForLocal(string? projectRoot = null)
    {
        string root = projectRoot ?? FindProjectRoot();
        return new DemoPaths(
            ProjectRoot: root,
            RepoDir: Path.Combine(root, "demo-workspace", "canarias-tours"),
            AdvisoriesFile: Path.Combine(root, "demo", "security", "advisories.json"),
            MigrationNotesDir: Path.Combine(root, "demo", "feed", "notes"),
            SkillsDir: Path.Combine(AppContext.BaseDirectory, "skills"));
    }

    /// <summary>
    /// Rutas dentro del contenedor del Hosted Agent: el estado vive en $HOME, que Foundry conserva entre sesiones;
    /// los datos de la demo (feed, plantilla, avisos) vienen en la imagen, bajo <paramref name="demoRoot"/>.
    /// </summary>
    public static DemoPaths ForHosted(string home, string demoRoot = "/opt/demo") => new(
        ProjectRoot: home,
        RepoDir: Path.Combine(home, "work", "canarias-tours"),
        AdvisoriesFile: Path.Combine(demoRoot, "security", "advisories.json"),
        MigrationNotesDir: Path.Combine(demoRoot, "feed", "notes"),
        SkillsDir: Path.Combine(AppContext.BaseDirectory, "skills"));

    /// <summary>Avisos en el idioma pedido: advisories.en.json junto a advisories.json (si falta, el español).</summary>
    public string AdvisoriesFileFor(DemoLanguage language) => Localized(this.AdvisoriesFile, language);

    /// <summary>Skills en el idioma pedido: skills/ (español) o skills-en/ al lado.</summary>
    public string SkillsDirFor(DemoLanguage language) =>
        language == DemoLanguage.En ? this.SkillsDir.TrimEnd(Path.DirectorySeparatorChar) + "-en" : this.SkillsDir;

    /// <summary>fichero.json → fichero.en.json en inglés, si existe.</summary>
    private static string Localized(string file, DemoLanguage language)
    {
        if (language == DemoLanguage.Es)
        {
            return file;
        }

        string english = Path.Combine(Path.GetDirectoryName(file)!, $"{Path.GetFileNameWithoutExtension(file)}.{language.Code()}{Path.GetExtension(file)}");
        return File.Exists(english) ? english : file;
    }

    /// <summary>Sube desde el directorio actual (y desde el ejecutable) hasta encontrar UpgradeCaptain.slnx.</summary>
    private static string FindProjectRoot()
    {
        if (Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_ROOT") is { Length: > 0 } fromEnv)
        {
            return Path.GetFullPath(fromEnv);
        }

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "UpgradeCaptain.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException(
            "No encuentro la raíz de upgrade-captain (UpgradeCaptain.slnx). Ejecuta desde el repo o define UPGRADE_CAPTAIN_ROOT.");
    }
}
