using System.Diagnostics;
using System.Text.Json;

namespace UpgradeCaptain.Harness;

/// <summary>Checkpoints registrados en StateRoot/checkpoints.json.</summary>
public sealed record CheckpointState(string? Baseline, IReadOnlyList<GreenCheckpoint> Greens, string? LastRestoredTo)
{
    public static CheckpointState Empty { get; } = new(null, [], null);

    /// <summary>A dónde se vuelve: el último verde o, si aún no hay ninguno, el punto de partida.</summary>
    public string? RestoreTarget => this.Greens.Count > 0 ? this.Greens[^1].Sha : this.Baseline;
}

public sealed record GreenCheckpoint(string Sha, string Message, DateTimeOffset At);

/// <summary>
/// Deshacer cambios malos (el harness integrado no tiene rollback de shell ni de ficheros).
/// Trabaja en la rama upgrade/teide-geo-2.0 del repo de la demo:
///   · CommitGreen: git add -A + git commit cuando la verificación está en verde
///   · RestoreLastGreen: git reset --hard + git clean -fd al último verde
/// Nunca hace push. Se niega a trabajar si el directorio no es la raíz de su propio repo git.
/// </summary>
public class GitCheckpoints
{
    public const string DefaultBranch = "upgrade/teide-geo-2.0";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _repoDir;
    private readonly string _stateFile;
    private readonly string _branch;
    private readonly IProcessRunner _runner;
    private readonly AuditLog? _audit;

    public GitCheckpoints(string repoDir, string stateRoot, string branch = DefaultBranch, IProcessRunner? runner = null, AuditLog? audit = null)
    {
        this._repoDir = Path.GetFullPath(repoDir);
        this._stateFile = Path.Combine(stateRoot, "checkpoints.json");
        this._branch = branch;
        this._runner = runner ?? ProcessRunner.Default;
        this._audit = audit;
    }

    public string Branch => this._branch;

    public CheckpointState State =>
        File.Exists(this._stateFile)
            ? JsonSerializer.Deserialize<CheckpointState>(File.ReadAllText(this._stateFile), JsonOptions) ?? CheckpointState.Empty
            : CheckpointState.Empty;

    /// <summary>Apunta el punto de partida (HEAD actual) la primera vez. Idempotente.</summary>
    public virtual async Task EnsureBaselineAsync(CancellationToken cancellationToken = default)
    {
        if (this.State.Baseline is not null)
        {
            return;
        }

        await this.EnsureOwnRepositoryAsync(cancellationToken).ConfigureAwait(false);
        string head = await this.GitAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        this.Save(this.State with { Baseline = head });
    }

    /// <summary>Commit de lo que hay (verde verificado) en la rama de la actualización. Devuelve el SHA.</summary>
    public virtual async Task<string> CommitGreenAsync(string message, CancellationToken cancellationToken = default)
    {
        using Activity? span = HarnessDiagnostics.ActivitySource.StartActivity("upgrade_captain.checkpoint");
        await this.EnsureBaselineAsync(cancellationToken).ConfigureAwait(false);
        await this.EnsureBranchAsync(cancellationToken).ConfigureAwait(false);

        await this.GitAsync(["add", "-A"], cancellationToken).ConfigureAwait(false);
        ProcessResult staged = await this._runner.RunAsync("git", ["-C", this._repoDir, "diff", "--cached", "--quiet"], this._repoDir, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (staged.ExitCode != 0)
        {
            await this.GitAsync(
                ["-c", "user.name=Upgrade Captain", "-c", "user.email=upgrade-captain@canarias-tours.example", "commit", "-q", "-m", message],
                cancellationToken).ConfigureAwait(false);
        }

        string sha = await this.GitAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        CheckpointState state = this.State;
        if (state.Greens.Count == 0 || state.Greens[^1].Sha != sha)
        {
            this.Save(state with { Greens = [.. state.Greens, new GreenCheckpoint(sha, message, DateTimeOffset.UtcNow)] });
        }

        span?.SetTag("checkpoint.sha", sha);
        return sha;
    }

    /// <summary>Vuelve al último verde (o al punto de partida): reset --hard + clean -fd. Devuelve el SHA.</summary>
    public virtual async Task<string> RestoreLastGreenAsync(string reason = "restore", CancellationToken cancellationToken = default)
    {
        using Activity? span = HarnessDiagnostics.ActivitySource.StartActivity("upgrade_captain.restore");
        await this.EnsureBaselineAsync(cancellationToken).ConfigureAwait(false);
        string target = this.State.RestoreTarget ?? throw new InvalidOperationException("No hay checkpoint al que volver.");

        await this.EnsureOwnRepositoryAsync(cancellationToken).ConfigureAwait(false);
        await this.GitAsync(["reset", "--hard", "-q", target], cancellationToken).ConfigureAwait(false);
        await this.GitAsync(["clean", "-fd", "-q"], cancellationToken).ConfigureAwait(false);

        this.Save(this.State with { LastRestoredTo = target });
        this._audit?.Append("restored", JobContext.Current?.JobId ?? "-", "harness", new { sha = target, reason });
        span?.SetTag("checkpoint.sha", target);
        return target;
    }

    private async Task EnsureBranchAsync(CancellationToken cancellationToken)
    {
        string current = await this.GitAsync(["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
        if (current == this._branch)
        {
            return;
        }

        ProcessResult exists = await this._runner.RunAsync("git", ["-C", this._repoDir, "rev-parse", "--verify", "--quiet", "refs/heads/" + this._branch], this._repoDir, GitTimeout, cancellationToken).ConfigureAwait(false);
        // switch conserva los cambios sin commitear del árbol de trabajo
        await this.GitAsync(exists.ExitCode == 0 ? ["switch", "-q", this._branch] : ["switch", "-q", "-c", this._branch], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Salvaguarda: reset --hard y clean -fd SOLO en la raíz de un repo propio. Si el directorio estuviera
    /// dentro de otro repo (p. ej. upgrade-captain), --show-prefix no sería vacío y nos negamos.
    /// </summary>
    private async Task EnsureOwnRepositoryAsync(CancellationToken cancellationToken)
    {
        ProcessResult prefix = await this._runner.RunAsync("git", ["-C", this._repoDir, "rev-parse", "--show-prefix"], this._repoDir, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (prefix.ExitCode != 0 || prefix.StdOut.Trim().Length > 0 || !Directory.Exists(Path.Combine(this._repoDir, ".git")))
        {
            throw new InvalidOperationException($"{this._repoDir} no es la raíz de un repositorio git propio: no se tocan checkpoints.");
        }
    }

    private async Task<string> GitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessResult result = await this._runner.RunAsync("git", ["-C", this._repoDir, .. arguments], this._repoDir, GitTimeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} falló ({result.ExitCode}): {result.StdErr.Trim()}");
        }

        return result.StdOut.Trim();
    }

    private void Save(CheckpointState state) => FileLock.WriteAllTextAtomic(this._stateFile, JsonSerializer.Serialize(state, JsonOptions));
}
