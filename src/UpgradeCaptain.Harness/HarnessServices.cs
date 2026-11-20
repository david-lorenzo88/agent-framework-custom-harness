namespace UpgradeCaptain.Harness;

/// <summary>
/// Los servicios compartidos por las piezas a medida, sobre un repo y un directorio de estado.
/// StateRoot contiene approvals.json, audit.jsonl, checkpoints.json y sessions/.
/// </summary>
public sealed record HarnessServices(
    string RepoDir,
    string StateRoot,
    RepoVerifier Verifier,
    RepoStateCache Cache,
    GitCheckpoints Checkpoints,
    SessionVault Sessions,
    ApprovalStore Approvals,
    AuditLog Audit,
    CodeOwnersPolicy Policy)
{
    /// <summary>Idioma de lo que el harness dice al modelo y a la persona: false = español (por defecto), true = inglés.</summary>
    public bool English { get; init; }

    public static HarnessServices Create(string repoDir, string stateRoot, string advisoriesFile, IProcessRunner? runner = null, bool english = false)
    {
        Directory.CreateDirectory(stateRoot);
        var audit = new AuditLog(stateRoot);
        return new HarnessServices(
            RepoDir: repoDir,
            StateRoot: stateRoot,
            Verifier: new RepoVerifier(repoDir, advisoriesFile, runner, audit) { English = english },
            Cache: new RepoStateCache(),
            Checkpoints: new GitCheckpoints(repoDir, stateRoot, GitCheckpoints.DefaultBranch, runner, audit),
            Sessions: new SessionVault(stateRoot),
            Approvals: new ApprovalStore(stateRoot),
            Audit: audit,
            Policy: CodeOwnersPolicy.Load(repoDir))
        {
            English = english,
        };
    }
}
