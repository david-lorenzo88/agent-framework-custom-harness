using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Core;

/// <summary>
/// Las cinco tools de Upgrade Captain. Todas son de solo lectura: no necesitan aprobación.
/// Trabajan sobre <see cref="DemoPaths.RepoDir"/> y nunca salen de él.
/// Bilingües: la implementación es una (<c>RepoReader</c>); lo que ve el modelo (descripciones de tools y
/// parámetros, mensajes) va en el idioma de la demo.
/// </summary>
public static class UpgradeTools
{
    /// <summary>Tamaño máximo que devuelve read_source (64 KB).</summary>
    public const int MaxSourceBytes = 64 * 1024;

    /// <summary>Las cinco tools sobre las rutas activas (<see cref="DemoPaths.Current"/>) y en el idioma activo.</summary>
    public static IList<AITool> All => Create(DemoPaths.Current, DemoLanguages.Current);

    /// <summary>Las cinco tools sobre unas rutas concretas, en el idioma activo.</summary>
    public static IList<AITool> Create(DemoPaths paths) => Create(paths, DemoLanguages.Current);

    /// <summary>Las cinco tools sobre unas rutas concretas y en un idioma concreto (lo usa la factoría del step-3).</summary>
    public static IList<AITool> Create(DemoPaths paths, DemoLanguage language)
    {
        if (!Directory.Exists(paths.RepoDir))
        {
            throw new DirectoryNotFoundException(language.Pick(
                $"No existe el repo de la demo ({paths.RepoDir}). Ejecuta scripts/reset-demo.sh primero.",
                $"The demo repo doesn't exist ({paths.RepoDir}). Run scripts/reset-demo.sh first."));
        }

        var repo = new RepoReader(paths, language);
        return language == DemoLanguage.En ? English(new EnglishSurface(repo)) : Spanish(new SpanishSurface(repo));
    }

    private static IList<AITool> Spanish(SpanishSurface s) =>
    [
        AIFunctionFactory.Create(s.ListProjects, "list_projects",
            "Lista los proyectos .NET del repositorio. Devuelve una lista de objetos " +
            "{ name, path, packageReferences: [ { id, version } ] } (version resuelta desde Directory.Packages.props)."),
        AIFunctionFactory.Create(s.ListSourceFiles, "list_source_files",
            "Lista los ficheros .cs de un proyecto, o de todo el repositorio si no se indica proyecto. " +
            "Devuelve una lista de rutas relativas al repositorio (p. ej. src/Canarias.Tours.Payments/FareCalculator.cs)."),
        AIFunctionFactory.Create(s.ReadSource, "read_source",
            "Devuelve el contenido COMPLETO de un fichero del repositorio como un único texto. Máximo 64 KB. " +
            "Cada línea va precedida de su número con el formato '   N: ' (número, dos puntos y un espacio): " +
            "para mirar el código quita ese prefijo."),
        AIFunctionFactory.Create(s.GetAdvisories, "get_advisories",
            "Devuelve los avisos de seguridad de un paquete NuGet como texto JSON: " +
            "[ { id, package, affected, fixed, severity, title, description, remediation } ]."),
        AIFunctionFactory.Create(s.GetMigrationNotes, "get_migration_notes",
            "Devuelve como texto Markdown las notas de versión (cambios incompatibles incluidos) de un paquete entre dos versiones."),
    ];

    private static IList<AITool> English(EnglishSurface s) =>
    [
        AIFunctionFactory.Create(s.ListProjects, "list_projects",
            "Lists the .NET projects in the repository. Returns a list of objects " +
            "{ name, path, packageReferences: [ { id, version } ] } (version resolved from Directory.Packages.props)."),
        AIFunctionFactory.Create(s.ListSourceFiles, "list_source_files",
            "Lists the .cs files of a project, or of the whole repository if no project is given. " +
            "Returns a list of paths relative to the repository (e.g. src/Canarias.Tours.Payments/FareCalculator.cs)."),
        AIFunctionFactory.Create(s.ReadSource, "read_source",
            "Returns the FULL content of a repository file as a single text. 64 KB maximum. " +
            "Every line starts with its number in the format '   N: ' (number, colon and a space): " +
            "strip that prefix to look at the code."),
        AIFunctionFactory.Create(s.GetAdvisories, "get_advisories",
            "Returns the security advisories of a NuGet package as JSON text: " +
            "[ { id, package, affected, fixed, severity, title, description, remediation } ]."),
        AIFunctionFactory.Create(s.GetMigrationNotes, "get_migration_notes",
            "Returns, as Markdown text, the release notes (breaking changes included) of a package between two versions."),
    ];

    /// <summary>Lo que ve el modelo en español: solo las descripciones de los parámetros.</summary>
    private sealed class SpanishSurface(RepoReader repo)
    {
        public IReadOnlyList<ProjectInfo> ListProjects() => repo.ListProjects();

        public IReadOnlyList<string> ListSourceFiles(
            [Description("Opcional. Nombre del proyecto (p. ej. Canarias.Tours.Payments) o ruta relativa de su carpeta. Vacío = todo el repositorio.")] string project = "")
            => repo.ListSourceFiles(project);

        public string ReadSource(
            [Description("Ruta del fichero relativa a la raíz del repositorio, p. ej. src/Canarias.Tours.Payments/FareCalculator.cs.")] string path)
            => repo.ReadSource(path);

        public string GetAdvisories(
            [Description("Id del paquete NuGet, p. ej. Teide.Geo.")] string package)
            => repo.GetAdvisories(package);

        public string GetMigrationNotes(
            [Description("Id del paquete NuGet, p. ej. Teide.Geo.")] string package,
            [Description("Versión actual, p. ej. 1.8.0.")] string fromVersion,
            [Description("Versión objetivo, p. ej. 2.0.0.")] string toVersion)
            => repo.GetMigrationNotes(package, fromVersion, toVersion);
    }

    /// <summary>What the model sees in English: only the parameter descriptions.</summary>
    private sealed class EnglishSurface(RepoReader repo)
    {
        public IReadOnlyList<ProjectInfo> ListProjects() => repo.ListProjects();

        public IReadOnlyList<string> ListSourceFiles(
            [Description("Optional. Project name (e.g. Canarias.Tours.Payments) or the relative path of its folder. Empty = the whole repository.")] string project = "")
            => repo.ListSourceFiles(project);

        public string ReadSource(
            [Description("File path relative to the repository root, e.g. src/Canarias.Tours.Payments/FareCalculator.cs.")] string path)
            => repo.ReadSource(path);

        public string GetAdvisories(
            [Description("NuGet package id, e.g. Teide.Geo.")] string package)
            => repo.GetAdvisories(package);

        public string GetMigrationNotes(
            [Description("NuGet package id, e.g. Teide.Geo.")] string package,
            [Description("Current version, e.g. 1.8.0.")] string fromVersion,
            [Description("Target version, e.g. 2.0.0.")] string toVersion)
            => repo.GetMigrationNotes(package, fromVersion, toVersion);
    }

    /// <summary>Implementación de las tools. Clase aparte para que cada método sea pequeño y legible.</summary>
    private sealed class RepoReader(DemoPaths paths, DemoLanguage language)
    {
        private readonly string _repo = Path.GetFullPath(paths.RepoDir);

        public IReadOnlyList<ProjectInfo> ListProjects()
        {
            Dictionary<string, string> versions = this.ReadCentralVersions();
            return Directory.EnumerateFiles(this._repo, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Order(StringComparer.Ordinal)
                .Select(path => new ProjectInfo(
                    Name: Path.GetFileNameWithoutExtension(path),
                    Path: this.Relative(path),
                    PackageReferences: XDocument.Load(path).Descendants("PackageReference")
                        .Select(e => (string?)e.Attribute("Include"))
                        .OfType<string>()
                        .Select(id => new PackageRef(id, versions.GetValueOrDefault(id, "?")))
                        .ToList()))
                .ToList();
        }

        public IReadOnlyList<string> ListSourceFiles(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
            {
                return Directory.EnumerateFiles(this._repo, "*.cs", SearchOption.AllDirectories)
                    .Where(p => !IsBuildOutput(p))
                    .Select(this.Relative)
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }

            string? projectDir = Directory.EnumerateFiles(this._repo, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !IsBuildOutput(p))
                .Where(p => Path.GetFileNameWithoutExtension(p).Equals(project, StringComparison.OrdinalIgnoreCase)
                         || this.Relative(Path.GetDirectoryName(p)!).Equals(project.Trim('/'), StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetDirectoryName)
                .FirstOrDefault();

            if (projectDir is null)
            {
                return [language.Pick(
                    $"No existe el proyecto '{project}'. Usa list_projects para ver los nombres.",
                    $"There is no project '{project}'. Use list_projects to see the names.")];
            }

            return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !IsBuildOutput(p))
                .Select(this.Relative)
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        public string ReadSource(string path)
        {
            if (!this.TryResolveInsideRepo(path, out string fullPath, out string error))
            {
                return error;
            }

            string text = File.ReadAllText(fullPath);
            bool truncated = text.Length > MaxSourceBytes;
            if (truncated)
            {
                text = text[..MaxSourceBytes];
            }

            // Números de línea: el modelo puede citar "fichero:línea" sin contar a mano.
            var numbered = string.Join('\n', text.Split('\n').Select((line, i) => $"{i + 1,4}: {line.TrimEnd('\r')}"));
            return truncated ? numbered + language.Pick("\n[... recortado a 64 KB ...]", "\n[... truncated at 64 KB ...]") : numbered;
        }

        public string GetAdvisories(string package)
        {
            string file = paths.AdvisoriesFileFor(language);
            if (!File.Exists(file))
            {
                return "[]";
            }

            var matches = JsonNode.Parse(File.ReadAllText(file))!.AsArray()
                .Where(a => string.Equals((string?)a?["package"], package, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!.DeepClone())
                .ToArray();
            return new JsonArray(matches).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        public string GetMigrationNotes(string package, string fromVersion, string toVersion)
        {
            if (!package.Equals("Teide.Geo", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(paths.MigrationNotesDir))
            {
                return language.Pick($"No hay notas de versión para {package}.", $"There are no release notes for {package}.");
            }

            if (!Version.TryParse(fromVersion, out Version? from) || !Version.TryParse(toVersion, out Version? to))
            {
                return language.Pick(
                    "Versiones no válidas. Usa el formato mayor.menor.parche (p. ej. 1.8.0).",
                    "Invalid versions. Use the major.minor.patch format (e.g. 1.8.0).");
            }

            // Todas las notas en el rango (from, to], de la más antigua a la más nueva.
            // En español, <versión>.md; en inglés, <versión>.en.md (pack-feed.sh copia las dos).
            string suffix = language == DemoLanguage.En ? ".en.md" : ".md";
            var notes = Directory.EnumerateFiles(paths.MigrationNotesDir, "*.md")
                .Where(file => file.EndsWith(suffix, StringComparison.Ordinal) && (language == DemoLanguage.En || !file.EndsWith(".en.md", StringComparison.Ordinal)))
                .Select(file => (File: file, Version: Version.TryParse(Path.GetFileName(file)[..^suffix.Length], out var v) ? v : null))
                .Where(n => n.Version is not null && n.Version > from && n.Version <= to)
                .OrderBy(n => n.Version)
                .Select(n => File.ReadAllText(n.File))
                .ToList();

            return notes.Count == 0
                ? language.Pick($"No hay notas de {package} entre {fromVersion} y {toVersion}.", $"There are no notes for {package} between {fromVersion} and {toVersion}.")
                : string.Join("\n\n---\n\n", notes);
        }

        private Dictionary<string, string> ReadCentralVersions()
        {
            string props = Path.Combine(this._repo, "Directory.Packages.props");
            if (!File.Exists(props))
            {
                return [];
            }

            return XDocument.Load(props).Descendants("PackageVersion")
                .Where(e => e.Attribute("Include") is not null && e.Attribute("Version") is not null)
                .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Confinamiento: la ruta resuelta debe quedar dentro del repo y no ser un enlace simbólico.</summary>
        private bool TryResolveInsideRepo(string relativePath, out string fullPath, out string error)
        {
            fullPath = Path.GetFullPath(Path.Combine(this._repo, relativePath));
            error = string.Empty;
            if (!fullPath.StartsWith(this._repo + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                error = language.Pick($"Acceso denegado: '{relativePath}' está fuera del repositorio.", $"Access denied: '{relativePath}' is outside the repository.");
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = language.Pick($"No existe el fichero '{relativePath}'.", $"The file '{relativePath}' doesn't exist.");
                return false;
            }

            if (new FileInfo(fullPath).LinkTarget is not null)
            {
                error = language.Pick($"Acceso denegado: '{relativePath}' es un enlace simbólico.", $"Access denied: '{relativePath}' is a symbolic link.");
                return false;
            }

            return true;
        }

        private string Relative(string path) => Path.GetRelativePath(this._repo, path).Replace('\\', '/');

        private static bool IsBuildOutput(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>Proyecto del repo, tal como lo devuelve list_projects.</summary>
    public sealed record ProjectInfo(string Name, string Path, IReadOnlyList<PackageRef> PackageReferences);

    /// <summary>Referencia a paquete con la versión central resuelta.</summary>
    public sealed record PackageRef(string Id, string Version);
}
