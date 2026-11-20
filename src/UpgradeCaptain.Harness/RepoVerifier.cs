using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UpgradeCaptain.Harness;

/// <summary>
/// Verifica el repo como lo haría CI, con procesos directos en el directorio del repo (no con la tool
/// run_shell del modelo): <c>dotnet build</c>, <c>dotnet test</c> con TRX y la versión resuelta de los
/// paquetes frente a los avisos de seguridad. 120 s como máximo por comando.
/// </summary>
public partial class RepoVerifier
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    private const int MaxItems = 10;
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private readonly string _repoDir;
    private readonly string _advisoriesFile;
    private readonly IProcessRunner _runner;
    private readonly AuditLog? _audit;

    public RepoVerifier(string repoDir, string advisoriesFile, IProcessRunner? runner = null, AuditLog? audit = null)
    {
        this._repoDir = Path.GetFullPath(repoDir);
        this._advisoriesFile = advisoriesFile;
        this._runner = runner ?? ProcessRunner.Default;
        this._audit = audit;
    }

    /// <summary>Idioma de los resultados (resumen y feedback para el modelo).</summary>
    public bool English { get; init; }

    /// <summary>Compila, ejecuta los tests y comprueba dependencias. Virtual para poder falsearlo en tests.</summary>
    public virtual async Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        using Activity? span = HarnessDiagnostics.ActivitySource.StartActivity("upgrade_captain.verify");
        var clock = Stopwatch.StartNew();

        // 1. Build
        ProcessResult build = await this._runner.RunAsync("dotnet", ["build", "--nologo", "-clp:ErrorsOnly"], this._repoDir, CommandTimeout, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> errors = this.ParseBuildErrors(build.Output);
        bool buildOk = build.ExitCode == 0;
        if (!buildOk && errors.Count == 0)
        {
            errors = [build.TimedOut
                ? this.L("dotnet build superó el tiempo máximo (120 s)", "dotnet build exceeded the time limit (120 s)")
                : this.L($"dotnet build terminó con código {build.ExitCode}", $"dotnet build exited with code {build.ExitCode}")];
        }

        // 2. Tests (solo si compila), con TRX para leer los fallos sin depender del idioma de la consola
        int total = 0, failedCount = 0;
        IReadOnlyList<FailedTest> failed = [];
        bool testsOk = false;
        if (buildOk)
        {
            string resultsDir = Path.Combine(this._repoDir, "TestResults");
            string trxPath = Path.Combine(resultsDir, "verify.trx");
            if (File.Exists(trxPath))
            {
                File.Delete(trxPath); // nunca leer el TRX de una verificación anterior
            }
            ProcessResult test = await this._runner.RunAsync(
                "dotnet",
                ["test", "--no-build", "--nologo", "--logger", "trx;LogFileName=verify.trx", "--results-directory", resultsDir],
                this._repoDir, CommandTimeout, cancellationToken).ConfigureAwait(false);

            (total, failedCount, failed) = File.Exists(trxPath) ? this.ParseTrx(trxPath) : (0, 0, []);
            testsOk = test.ExitCode == 0 && failedCount == 0 && total > 0;
            if (!testsOk && failed.Count == 0)
            {
                failed = [new FailedTest("dotnet test", test.TimedOut
                    ? this.L("superó el tiempo máximo (120 s)", "exceeded the time limit (120 s)")
                    : this.L($"terminó con código {test.ExitCode} sin TRX", $"exited with code {test.ExitCode} without a TRX"))];
                failedCount = Math.Max(failedCount, 1);
            }
        }

        // 3. Dependencias: versión resuelta frente a los avisos de seguridad
        Dictionary<string, string> resolved = await this.ResolvePackageVersionsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> vulnerable = this.FindVulnerable(resolved);

        var result = new VerificationResult
        {
            BuildSucceeded = buildOk,
            Errors = errors,
            TestsPassed = testsOk,
            TestsTotal = total,
            TestsFailed = failedCount,
            FailedTests = failed,
            VulnerablePackages = vulnerable,
            ResolvedVersions = resolved,
            Duration = clock.Elapsed,
            English = this.English,
        };

        span?.SetTag("build.ok", buildOk);
        span?.SetTag("tests.failed", failedCount);
        span?.SetTag("tests.total", total);
        span?.SetTag("vulnerable.count", vulnerable.Count);
        span?.SetTag("verify.green", result.IsGreen);
        HarnessDiagnostics.Verifications.Add(1, new KeyValuePair<string, object?>("result", result.IsGreen ? "green" : "red"));

        this._audit?.Append("verified", JobContext.Current?.JobId ?? "-", "harness", new { result.IsGreen, result.Summary, result.Fingerprint });
        return result;
    }

    // ── Parseo de la salida de dotnet build ────────────────────────────────────
    // /ruta/Fichero.cs(25,51): error CS0246: El nombre del tipo... [/ruta/Proyecto.csproj]
    [GeneratedRegex(@"^\s*(?<file>[^\s(][^(]*?)\((?<line>\d+),(?<col>\d+)\):\s+error\s+(?<code>[A-Z]+\d+):\s+(?<msg>.*?)(?:\s+\[[^\]]+\])?\s*$", RegexOptions.Multiline)]
    private static partial Regex BuildErrorRegex();

    internal IReadOnlyList<string> ParseBuildErrors(string output) =>
        BuildErrorRegex().Matches(output)
            .Select(m => $"{this.Relative(m.Groups["file"].Value)}:{m.Groups["line"].Value} {m.Groups["code"].Value} {m.Groups["msg"].Value.Trim()}")
            .Distinct()
            .Take(MaxItems)
            .ToList();

    // ── Parseo del TRX de dotnet test ──────────────────────────────────────────
    // "at X in /ruta/Test.cs:line 25" (o "en ... en /ruta/Test.cs:línea 25" con la consola en español)
    [GeneratedRegex(@"(?:in|en)\s+(?<file>\S+\.cs):(?:line|línea)\s+(?<line>\d+)")]
    private static partial Regex StackLocationRegex();

    internal (int Total, int Failed, IReadOnlyList<FailedTest> FailedTests) ParseTrx(string trxPath)
    {
        XDocument doc = XDocument.Load(trxPath);
        XElement? counters = doc.Descendants(Trx + "Counters").FirstOrDefault();
        int total = (int?)counters?.Attribute("total") ?? 0;
        int failedCount = (int?)counters?.Attribute("failed") ?? 0;

        var failed = doc.Descendants(Trx + "UnitTestResult")
            .Where(r => (string?)r.Attribute("outcome") == "Failed")
            .Select(r =>
            {
                string message = r.Descendants(Trx + "Message").FirstOrDefault()?.Value ?? string.Empty;
                string stack = r.Descendants(Trx + "StackTrace").FirstOrDefault()?.Value ?? string.Empty;
                Match location = StackLocationRegex().Match(stack);
                return new FailedTest(
                    Name: (string?)r.Attribute("testName") ?? "?",
                    Message: CompactMessage(message),
                    Location: location.Success ? $"{this.Relative(location.Groups["file"].Value)}:{location.Groups["line"].Value}" : null);
            })
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();

        return (total, Math.Max(failedCount, failed.Count), failed);
    }

    /// <summary>Las 3 primeras líneas no vacías del mensaje, en una sola línea (Expected/Actual incluidos).</summary>
    private static string CompactMessage(string message)
    {
        string compact = string.Join(" | ", message.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(3));
        return compact.Length > 220 ? compact[..219] + "…" : compact;
    }

    // ── Dependencias ───────────────────────────────────────────────────────────
    private async Task<Dictionary<string, string>> ResolvePackageVersionsAsync(CancellationToken cancellationToken)
    {
        var watched = this.ReadAdvisories().Select(a => a.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (watched.Count == 0)
        {
            return resolved;
        }

        ProcessResult list = await this._runner.RunAsync("dotnet", ["list", "package", "--format", "json"], this._repoDir, CommandTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            // { "projects": [ { "frameworks": [ { "topLevelPackages": [ { "id", "resolvedVersion" } ] } ] } ] }
            JsonNode? root = JsonNode.Parse(list.StdOut);
            foreach (JsonNode? package in root?["projects"]?.AsArray().SelectMany(p => p?["frameworks"]?.AsArray() ?? [])
                         .SelectMany(f => f?["topLevelPackages"]?.AsArray() ?? []) ?? [])
            {
                string? id = (string?)package?["id"];
                string? version = (string?)package?["resolvedVersion"];
                if (id is not null && version is not null && watched.Contains(id))
                {
                    resolved[id] = version;
                }
            }
        }
        catch (JsonException)
        {
            // Si no hay restore todavía, caemos a la versión pedida en Directory.Packages.props.
        }

        if (resolved.Count == 0)
        {
            foreach (var (id, version) in this.ReadCentralVersions().Where(kv => watched.Contains(kv.Key)))
            {
                resolved[id] = version;
            }
        }

        return resolved;
    }

    private IReadOnlyList<string> FindVulnerable(IReadOnlyDictionary<string, string> resolved) =>
        this.ReadAdvisories()
            .Where(a => resolved.TryGetValue(a.Package, out string? version) && a.Affects(version))
            .Select(a => $"{a.Package} {resolved[a.Package]} ({a.Id})")
            .ToList();

    private IReadOnlyList<Advisory> ReadAdvisories()
    {
        if (!File.Exists(this._advisoriesFile))
        {
            return [];
        }

        return JsonNode.Parse(File.ReadAllText(this._advisoriesFile))!.AsArray()
            .Select(a => new Advisory((string)a!["id"]!, (string)a["package"]!, (string)a["affected"]!))
            .ToList();
    }

    private Dictionary<string, string> ReadCentralVersions()
    {
        string props = Path.Combine(this._repoDir, "Directory.Packages.props");
        return !File.Exists(props)
            ? []
            : XDocument.Load(props).Descendants("PackageVersion")
                .Where(e => e.Attribute("Include") is not null && e.Attribute("Version") is not null)
                .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);
    }

    private string Relative(string path) =>
        Path.IsPathRooted(path) ? Path.GetRelativePath(this._repoDir, path).Replace('\\', '/') : path.Replace('\\', '/');

    /// <summary>Aviso de seguridad con rango "&lt; X" o "&lt;= X".</summary>
    private sealed record Advisory(string Id, string Package, string Affected)
    {
        public bool Affects(string version)
        {
            if (!Version.TryParse(version.Split('-')[0], out Version? v))
            {
                return false;
            }

            string range = this.Affected.Replace(" ", string.Empty);
            return range.StartsWith("<=", StringComparison.Ordinal) ? Version.TryParse(range[2..], out var max) && v <= max
                 : range.StartsWith('<') ? Version.TryParse(range[1..], out var limit) && v < limit
                 : false;
        }
    }

    private string L(string es, string en) => this.English ? en : es;
}
