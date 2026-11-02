using System.Diagnostics;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>Con git de verdad en un directorio temporal (sin modelo, sin red).</summary>
public class GitCheckpointsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public async Task CommitGreen_ThenRestore_GoesBackToTheGreenCommit()
    {
        string repo = this.InitRepo("repo");
        string state = this._temp.Sub("state");
        var checkpoints = new GitCheckpoints(repo, state);
        await checkpoints.EnsureBaselineAsync();

        File.WriteAllText(Path.Combine(repo, "Fare.cs"), "km");
        string green = await checkpoints.CommitGreenAsync("verde");

        Assert.Equal("upgrade/teide-geo-2.0", Git(repo, "branch --show-current"));
        Assert.Equal(green, Git(repo, "rev-parse HEAD"));
        Assert.Equal(green, checkpoints.State.Greens.Single().Sha);

        File.WriteAllText(Path.Combine(repo, "Fare.cs"), "metros");   // cambio malo
        File.WriteAllText(Path.Combine(repo, "Basura.cs"), "x");       // fichero nuevo sin seguimiento
        string restored = await checkpoints.RestoreLastGreenAsync("test");

        Assert.Equal(green, restored);
        Assert.Equal("km", File.ReadAllText(Path.Combine(repo, "Fare.cs")));
        Assert.False(File.Exists(Path.Combine(repo, "Basura.cs")));
    }

    [Fact]
    public async Task Restore_WithoutGreens_GoesBackToBaseline()
    {
        string repo = this.InitRepo("repo");
        var checkpoints = new GitCheckpoints(repo, this._temp.Sub("state"));
        await checkpoints.EnsureBaselineAsync();
        string baseline = Git(repo, "rev-parse HEAD");

        File.WriteAllText(Path.Combine(repo, "README.md"), "roto");
        Assert.Equal(baseline, await checkpoints.RestoreLastGreenAsync());
        Assert.Equal("inicial", File.ReadAllText(Path.Combine(repo, "README.md")));
    }

    [Fact]
    public async Task RefusesToWork_InsideAnotherRepository()
    {
        string outer = this.InitRepo("outer");
        string nested = Path.Combine(outer, "demo-workspace", "canarias-tours");
        Directory.CreateDirectory(nested);

        var checkpoints = new GitCheckpoints(nested, this._temp.Sub("state"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => checkpoints.EnsureBaselineAsync());
    }

    private string InitRepo(string name)
    {
        string repo = this._temp.Sub(name);
        Git(repo, "init -q -b main");
        File.WriteAllText(Path.Combine(repo, "README.md"), "inicial");
        Git(repo, "add -A");
        Git(repo, "-c user.name=t -c user.email=t@t -c commit.gpgsign=false commit -q -m inicial");
        return repo;
    }

    private static string Git(string repo, string args)
    {
        var info = new ProcessStartInfo("git", $"-C \"{repo}\" {args}") { RedirectStandardOutput = true, RedirectStandardError = true };
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
