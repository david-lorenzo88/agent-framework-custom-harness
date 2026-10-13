using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Core;

/// <summary>
/// Las cinco tools de Upgrade Captain. Todas son de solo lectura: no necesitan aprobación.
/// Trabajan sobre <see cref="DemoPaths.RepoDir"/> y nunca salen de él.
/// </summary>
public static class UpgradeTools
{
    /// <summary>Tamaño máximo que devuelve read_source (64 KB).</summary>
    public const int MaxSourceBytes = 64 * 1024;

    /// <summary>Las cinco tools sobre las rutas activas (<see cref="DemoPaths.Current"/>).</summary>
    public static IList<AITool> All => Create(DemoPaths.Current);

    /// <summary>Las cinco tools sobre unas rutas concretas (lo usa la factoría del step-6).</summary>
    public static IList<AITool> Create(DemoPaths paths)
    {
        if (!Directory.Exists(paths.RepoDir))
        {
            throw new DirectoryNotFoundException(
                $"No existe el repo de la demo ({paths.RepoDir}). Ejecuta scripts/reset-demo.sh primero.");
        }

        var repo = new RepoReader(paths);
        return
        [
            AIFunctionFactory.Create(repo.ListProjects, "list_projects",
                "Lista los proyectos .NET del repositorio. Devuelve una lista de objetos " +
                "{ name, path, packageReferences: [ { id, version } ] } (version resuelta desde Directory.Packages.props)."),
            AIFunctionFactory.Create(repo.ListSourceFiles, "list_source_files",
                "Lista los ficheros .cs de un proyecto, o de todo el repositorio si no se indica proyecto. " +
                "Devuelve una lista de rutas relativas al repositorio (p. ej. src/Canarias.Tours.Payments/FareCalculator.cs)."),
            AIFunctionFactory.Create(repo.ReadSource, "read_source",
                "Devuelve el contenido COMPLETO de un fichero del repositorio como un único texto. Máximo 64 KB. " +
                "Cada línea va precedida de su número con el formato '   N: ' (número, dos puntos y un espacio): " +
                "para mirar el código quita ese prefijo."),
            AIFunctionFactory.Create(repo.GetAdvisories, "get_advisories",
                "Devuelve los avisos de seguridad de un paquete NuGet como texto JSON: " +
                "[ { id, package, affected, fixed, severity, title, description, remediation } ]."),
            AIFunctionFactory.Create(repo.GetMigrationNotes, "get_migration_notes",
                "Devuelve como texto Markdown las notas de versión (cambios incompatibles incluidos) de un paquete entre dos versiones."),
        ];
    }

    /// <summary>Implementación de las tools. Clase aparte para que cada método sea pequeño y legible.</summary>
    private sealed class RepoReader(DemoPaths paths)
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

        public IReadOnlyList<string> ListSourceFiles(
            [Description("Opcional. Nombre del proyecto (p. ej. Canarias.Tours.Payments) o ruta relativa de su carpeta. Vacío = todo el repositorio.")] string project = "")
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
                return [$"No existe el proyecto '{project}'. Usa list_projects para ver los nombres."];
            }

            return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !IsBuildOutput(p))
                .Select(this.Relative)
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        public string ReadSource(
            [Description("Ruta del fichero relativa a la raíz del repositorio, p. ej. src/Canarias.Tours.Payments/FareCalculator.cs.")] string path)
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
            return truncated ? numbered + "\n[... recortado a 64 KB ...]" : numbered;
        }

        public string GetAdvisories(
            [Description("Id del paquete NuGet, p. ej. Teide.Geo.")] string package)
        {
            if (!File.Exists(paths.AdvisoriesFile))
            {
                return "[]";
            }

            var matches = JsonNode.Parse(File.ReadAllText(paths.AdvisoriesFile))!.AsArray()
                .Where(a => string.Equals((string?)a?["package"], package, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!.DeepClone())
                .ToArray();
            return new JsonArray(matches).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public string GetMigrationNotes(
            [Description("Id del paquete NuGet, p. ej. Teide.Geo.")] string package,
            [Description("Versión actual, p. ej. 1.8.0.")] string fromVersion,
            [Description("Versión objetivo, p. ej. 2.0.0.")] string toVersion)
        {
            if (!package.Equals("Teide.Geo", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(paths.MigrationNotesDir))
            {
                return $"No hay notas de versión para {package}.";
            }

            if (!Version.TryParse(fromVersion, out Version? from) || !Version.TryParse(toVersion, out Version? to))
            {
                return "Versiones no válidas. Usa el formato mayor.menor.parche (p. ej. 1.8.0).";
            }

            // Todas las notas en el rango (from, to], de la más antigua a la más nueva.
            var notes = Directory.EnumerateFiles(paths.MigrationNotesDir, "*.md")
                .Select(file => (File: file, Version: Version.TryParse(Path.GetFileNameWithoutExtension(file), out var v) ? v : null))
                .Where(n => n.Version is not null && n.Version > from && n.Version <= to)
                .OrderBy(n => n.Version)
                .Select(n => File.ReadAllText(n.File))
                .ToList();

            return notes.Count == 0
                ? $"No hay notas de {package} entre {fromVersion} y {toVersion}."
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
                error = $"Acceso denegado: '{relativePath}' está fuera del repositorio.";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = $"No existe el fichero '{relativePath}'.";
                return false;
            }

            if (new FileInfo(fullPath).LinkTarget is not null)
            {
                error = $"Acceso denegado: '{relativePath}' es un enlace simbólico.";
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
