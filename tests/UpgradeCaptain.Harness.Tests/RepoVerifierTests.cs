namespace UpgradeCaptain.Harness.Tests;

public class RepoVerifierTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public void ParseBuildErrors_RelativeFileLineCodeMessage_Deduplicated()
    {
        string repo = this._temp.Sub("repo");
        var verifier = new RepoVerifier(repo, "none.json");
        string output =
            $"""
            {repo}/src/Canarias.Tours.Domain/RouteLeg.cs(5,58): error CS0246: El nombre del tipo o del espacio de nombres 'GeoPoint' no se encontró [{repo}/src/Canarias.Tours.Domain/Canarias.Tours.Domain.csproj]
            {repo}/src/Canarias.Tours.Domain/RouteLeg.cs(5,58): error CS0246: El nombre del tipo o del espacio de nombres 'GeoPoint' no se encontró [{repo}/src/Canarias.Tours.Domain/Canarias.Tours.Domain.csproj]
            {repo}/src/Canarias.Tours.Api/ElevationService.cs(9,51): error CS1061: 'ElevationClient' no contiene una definición para 'GetElevation' [{repo}/src/Canarias.Tours.Api/Canarias.Tours.Api.csproj]
                0 Advertencia(s)
                3 Errores
            """;

        IReadOnlyList<string> errors = verifier.ParseBuildErrors(output);

        Assert.Equal(
        [
            "src/Canarias.Tours.Domain/RouteLeg.cs:5 CS0246 El nombre del tipo o del espacio de nombres 'GeoPoint' no se encontró",
            "src/Canarias.Tours.Api/ElevationService.cs:9 CS1061 'ElevationClient' no contiene una definición para 'GetElevation'",
        ], errors);
    }

    [Fact]
    public void ParseTrx_FailedTestsWithMessageAndLocation()
    {
        string repo = this._temp.Sub("repo");
        string trx = Path.Combine(this._temp.Path, "verify.trx");
        File.WriteAllText(trx, SampleTrx(repo));

        var (total, failed, tests) = new RepoVerifier(repo, "none.json").ParseTrx(trx);

        Assert.Equal(24, total);
        Assert.Equal(1, failed);
        FailedTest test = Assert.Single(tests);
        Assert.Equal("Canarias.Tours.Tests.FareTests.Quote_LosCristianosToTeide_Is33_66Eur", test.Name);
        Assert.Equal("Assert.Equal() Failure: Values differ | Expected: 33.66 | Actual:   30663.76", test.Message);
        Assert.Equal("tests/Canarias.Tours.Tests/FareTests.cs:21", test.Location);
    }

    [Fact]
    public async Task VerifyAsync_GreenBuild_RedTests_VulnerablePackage()
    {
        string repo = this._temp.Sub("repo");
        string advisories = Path.Combine(this._temp.Path, "advisories.json");
        File.WriteAllText(advisories, """[{"id":"TEIDE-2026-0042","package":"Teide.Geo","affected":"< 2.0.0","fixed":"2.0.0"}]""");
        var runner = new FakeRunner(repo, SampleTrx(repo), resolvedVersion: "1.8.0");

        VerificationResult result = await new RepoVerifier(repo, advisories, runner).VerifyAsync();

        Assert.True(result.BuildSucceeded);
        Assert.False(result.TestsPassed);
        Assert.Equal(1, result.TestsFailed);
        Assert.Equal(["Teide.Geo 1.8.0 (TEIDE-2026-0042)"], result.VulnerablePackages);
        Assert.Equal("1.8.0", result.ResolvedVersions["Teide.Geo"]);
        Assert.False(result.IsGreen);
        Assert.Equal(["build", "test", "list"], runner.Commands);
    }

    [Fact]
    public async Task VerifyAsync_FixedVersion_IsNotVulnerable()
    {
        string repo = this._temp.Sub("repo");
        string advisories = Path.Combine(this._temp.Path, "advisories.json");
        File.WriteAllText(advisories, """[{"id":"TEIDE-2026-0042","package":"Teide.Geo","affected":"< 2.0.0","fixed":"2.0.0"}]""");
        var runner = new FakeRunner(repo, SampleTrx(repo, failed: 0), resolvedVersion: "2.0.0");

        VerificationResult result = await new RepoVerifier(repo, advisories, runner).VerifyAsync();

        Assert.True(result.IsGreen);
        Assert.Empty(result.VulnerablePackages);
    }

    [Fact]
    public void Feedback_IsSpanish_Actionable_AndCapped()
    {
        string feedback = Results.FareTestsRed().ToFeedback();
        Assert.StartsWith("🔎 Verificación del harness", feedback);
        Assert.Contains("no cambies los tests", feedback);

        var huge = Results.FareTestsRed() with
        {
            FailedTests = Enumerable.Range(0, 10).Select(i => new FailedTest($"Test{i}", new string('x', 400))).ToList(),
        };
        Assert.True(huge.ToFeedback().Length <= 2000);
    }

    private static string SampleTrx(string repo, int failed = 1)
    {
        string failedResult = failed == 0
            ? string.Empty
            : "<UnitTestResult testName=\"Canarias.Tours.Tests.FareTests.Quote_LosCristianosToTeide_Is33_66Eur\" outcome=\"Failed\">"
              + "<Output><ErrorInfo>"
              + "<Message>Assert.Equal() Failure: Values differ\nExpected: 33.66\nActual:   30663.76</Message>"
              + $"<StackTrace>   en Canarias.Tours.Tests.FareTests.Quote_LosCristianosToTeide_Is33_66Eur() en {repo}/tests/Canarias.Tours.Tests/FareTests.cs:línea 21</StackTrace>"
              + "</ErrorInfo></Output></UnitTestResult>";

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>"
            + failedResult
            + "<UnitTestResult testName=\"Canarias.Tours.Tests.DistanceTests.Plan_CreatesOneLegPerStop\" outcome=\"Passed\" />"
            + "</Results>"
            + $"<ResultSummary outcome=\"{(failed == 0 ? "Completed" : "Failed")}\">"
            + $"<Counters total=\"24\" executed=\"24\" passed=\"{24 - failed}\" failed=\"{failed}\" />"
            + "</ResultSummary></TestRun>";
    }

    /// <summary>dotnet falso: build OK, test escribe el TRX, list package devuelve la versión indicada.</summary>
    private sealed class FakeRunner(string repo, string trx, string resolvedVersion) : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            this.Commands.Add(arguments[0]);
            switch (arguments[0])
            {
                case "test":
                    Directory.CreateDirectory(Path.Combine(repo, "TestResults"));
                    File.WriteAllText(Path.Combine(repo, "TestResults", "verify.trx"), trx);
                    return Task.FromResult(new ProcessResult(trx.Contains("failed=\"0\"") ? 0 : 1, "", "", false));
                case "list":
                    string json = $$"""{"projects":[{"path":"x","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Teide.Geo","requestedVersion":"{{resolvedVersion}}","resolvedVersion":"{{resolvedVersion}}"}]}]}]}""";
                    return Task.FromResult(new ProcessResult(0, json, "", false));
                default:
                    return Task.FromResult(new ProcessResult(0, "Compilación correcta.", "", false));
            }
        }
    }
}
