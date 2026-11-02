using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness.Tests;

public class CodeOwnersPolicyTests
{
    // El CODEOWNERS real de Canarias.Tours
    private const string CanariasTours =
        """
        # Propietarios
        *                              @david-lorenzo
        /src/Canarias.Tours.Payments/  @maria-payments
        """;

    private readonly CodeOwnersPolicy _policy = CodeOwnersPolicy.Parse(CanariasTours);

    [Theory]
    [InlineData("src/Canarias.Tours.Payments/FareCalculator.cs", "@maria-payments", true)]
    [InlineData("src/Canarias.Tours.Payments/Sub/Deep.cs", "@maria-payments", true)]
    [InlineData("src/Canarias.Tours.Domain/RoutePlanner.cs", "@david-lorenzo", false)]
    [InlineData("Directory.Packages.props", "@david-lorenzo", false)]
    [InlineData("tests/Canarias.Tours.Tests/FareTests.cs", "@david-lorenzo", false)]
    public void Payments_IsProtected_AndOwnedByMaria(string path, string owner, bool isProtected)
    {
        Assert.Equal([owner], this._policy.OwnersFor([path]));
        Assert.Equal(isProtected, this._policy.IsProtected(path));
    }

    [Theory]
    [InlineData("./src/Canarias.Tours.Payments/FareCalculator.cs")]
    [InlineData("/src/Canarias.Tours.Payments/FareCalculator.cs")]
    [InlineData("src\\Canarias.Tours.Payments\\FareCalculator.cs")]
    public void Paths_AreNormalized(string path) => Assert.True(this._policy.IsProtected(path));

    [Fact]
    public void AnchoredDirectory_DoesNotMatchNestedFolderWithSameName()
    {
        Assert.False(this._policy.IsProtected("legacy/src/Canarias.Tours.Payments/Old.cs"));
    }

    [Fact]
    public void LastMatchingRuleWins()
    {
        var policy = CodeOwnersPolicy.Parse(
            """
            /src/Canarias.Tours.Payments/  @maria-payments
            *                              @david-lorenzo
            """);

        // La regla "*" va después y gana: Payments deja de estar protegida.
        Assert.Equal(["@david-lorenzo"], policy.OwnersFor(["src/Canarias.Tours.Payments/FareCalculator.cs"]));
        Assert.False(policy.IsProtected("src/Canarias.Tours.Payments/FareCalculator.cs"));
    }

    [Theory]
    [InlineData("*.props", "Directory.Packages.props", true)]
    [InlineData("*.props", "src/Deep/Other.props", true)]
    [InlineData("docs/", "docs/a.md", true)]
    [InlineData("docs/", "src/docs/a.md", true)]          // sin barra inicial: cualquier nivel
    [InlineData("/docs/", "src/docs/a.md", false)]        // anclado a la raíz
    [InlineData("src/**/Fare*.cs", "src/Canarias.Tours.Payments/FareCalculator.cs", true)]
    [InlineData("src/**/Fare*.cs", "src/FareCalculator.cs", true)]
    [InlineData("src/**/Fare*.cs", "tests/FareTests.cs", false)]
    [InlineData("**/Payments/**", "src/Payments/x/y.cs", true)]
    public void Patterns_FollowGitHubSemantics(string pattern, string path, bool matches)
    {
        var policy = CodeOwnersPolicy.Parse($"{pattern} @owner");
        Assert.Equal(matches, policy.Match(path) is not null);
    }

    [Fact]
    public void OwnersFor_UnionsOwnersOfSeveralPaths()
    {
        Assert.Equal(
            ["@maria-payments", "@david-lorenzo"],
            this._policy.OwnersFor(["src/Canarias.Tours.Payments/FareCalculator.cs", "README.md", "src/Canarias.Tours.Payments/Other.cs"]));
    }

    [Theory]
    [InlineData("file_access_replace")]
    [InlineData("file_access_write")]
    [InlineData("file_access_replace_lines")]
    [InlineData("file_access_delete")]
    public void ExtractPaths_FileWriteTools_UseFileName(string tool)
    {
        var call = new FunctionCallContent("c1", tool, new Dictionary<string, object?> { ["fileName"] = "./src/Canarias.Tours.Payments/FareCalculator.cs" });
        Assert.Equal(["src/Canarias.Tours.Payments/FareCalculator.cs"], CodeOwnersPolicy.ExtractPaths(call));
    }

    [Fact]
    public void ExtractPaths_BumpPackageScript_TouchesCentralProps()
    {
        var call = new FunctionCallContent("c1", "run_skill_script", new Dictionary<string, object?>
        {
            ["skillName"] = "nuget-upgrade",
            ["scriptName"] = "scripts/bump_package.cs",
        });
        Assert.Equal(["Directory.Packages.props"], CodeOwnersPolicy.ExtractPaths(call));
    }

    [Fact]
    public void ExtractPaths_Reads_HaveNoPaths()
    {
        var call = new FunctionCallContent("c1", "file_access_read", new Dictionary<string, object?> { ["fileName"] = "src/Canarias.Tours.Payments/FareCalculator.cs" });
        Assert.Empty(CodeOwnersPolicy.ExtractPaths(call));
    }

    // D-34: en el ensayo, el agente editó Payments con un perl -pi por run_shell y se saltó a María.
    [Theory]
    [InlineData("sed -i 's/a/b/' src/Canarias.Tours.Payments/FareCalculator.cs")]
    [InlineData("perl -0pi -e 's/Distance\\(a, b\\)/Distance(a, b, DistanceUnit.Kilometers)/' src/Canarias.Tours.Domain/RoutePlanner.cs ./src/Canarias.Tours.Payments/FareCalculator.cs")]
    [InlineData("cd src/Canarias.Tours.Payments && sed -i 's/x/y/' TourPackagePricing.cs")]
    [InlineData("cat > \"src/Canarias.Tours.Payments/FareCalculator.cs\" <<'EOF'")]
    public void PathsFor_ShellNamingAProtectedPath_IsProtectedAndOnlyThatPathCounts(string command)
    {
        var call = new FunctionCallContent("c1", "run_shell", new Dictionary<string, object?> { ["command"] = command });

        IReadOnlyList<string> paths = this._policy.PathsFor(call);

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(this._policy.IsProtected(p), p));
        Assert.All(paths, p => Assert.True(this._policy.IsOwner("@maria-payments", p), p)); // María puede aprobarlo ella sola
    }

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("dotnet test --nologo")]
    [InlineData("grep -RIn 'GeoCalculator' src tests")]
    [InlineData("sed -i 's/a/b/' src/Canarias.Tours.Domain/RoutePlanner.cs")]
    public void PathsFor_ShellWithoutProtectedPaths_HasNone(string command)
    {
        var call = new FunctionCallContent("c1", "run_shell", new Dictionary<string, object?> { ["command"] = command });
        Assert.Empty(this._policy.PathsFor(call));
    }
}
