using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Core;

/// <summary>Backend de CodeAct. Hyperlight no funciona en macOS (necesita KVM o WHP): PLAN.md §5.5.</summary>
public enum CodeActBackend
{
    None,
    Local,
    Hyperlight,
}

/// <summary>
/// Configuración de Upgrade Captain. La misma factoría sirve a la consola (ForLocal) y al Hosted Agent (ForHosted).
/// Con <see cref="EnableCustomHarness"/> = false el agente es idéntico al del step-2.
/// </summary>
public sealed class UpgradeCaptainOptions
{
    public required DemoPaths Paths { get; init; }

    // ── Modelo ───────────────────────────────────────────────────────────────
    public string? ProjectEndpoint { get; set; }

    public string Model { get; set; } = "gpt-5.4";

    /// <summary>Si se indica, sustituye al cliente de Foundry (lo usan los tests con clientes falsos).</summary>
    public IChatClient? ChatClient { get; set; }

    public int MaxContextWindowTokens { get; set; } = 1_050_000;

    public int MaxOutputTokens { get; set; } = 128_000;

    // ── Capacidades (step-2) ──────────────────────────────────────────────────
    public bool EnableShell { get; set; }

    public bool EnableFileAccess { get; set; }

    public CodeActBackend CodeAct { get; set; } = CodeActBackend.None;

    /// <summary>Python para CodeAct Local (.venv/bin/python en local, python3 en el contenedor).</summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>Lista de permitidos del shell (null = sin lista; el Hosted Agent la usa).</summary>
    public IReadOnlyList<string>? ShellAllowList { get; set; }

    /// <summary>Skills empaquetadas y de confianza: también run_skill_script se aprueba solo (Hosted Agent).</summary>
    public bool TrustSkillScripts { get; set; }

    /// <summary>
    /// Desatendido en la VM por sesión (Hosted Agent): se aprueban solas las escrituras en rutas NO protegidas
    /// y el shell (ya limitado por la lista de permitidos). Solo lo protegido llega a CodeOwnerApprovalAgent.
    /// </summary>
    public bool AutoApproveUnprotected { get; set; }

    // ── Harness a medida (step-3) ────────────────────────────────────────────
    public bool EnableCustomHarness { get; set; }

    /// <summary>Tope de tokens por sesión que aplica BudgetGuard (UPGRADE_CAPTAIN_TOKEN_BUDGET, por defecto 1.500.000).</summary>
    public long TokenBudget { get; set; } =
        long.TryParse(Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_TOKEN_BUDGET"), out long budget) && budget > 0 ? budget : 1_500_000;

    /// <summary>Raíz de la memoria en ficheros del harness (FileMemoryProvider).</summary>
    public required string MemoryRoot { get; set; }

    /// <summary>Raíz del estado de las piezas a medida: approvals.json, audit.jsonl, checkpoints.json, sessions/.</summary>
    public required string StateRoot { get; set; }

    /// <summary>Quién pide el trabajo por defecto (consola: --as; desatendido: requestedBy del job).</summary>
    public string Requester { get; set; } = "@david-lorenzo";

    /// <summary>¿Hay una persona delante? En desatendido todas las aprobaciones se aparcan.</summary>
    public bool Attended { get; set; } = true;

    /// <summary>
    /// Máquina del ponente: shell, ficheros y harness a medida activados, todo confinado a
    /// demo-workspace/canarias-tours. La superficie (direct/codeact) la elige --surface.
    /// </summary>
    public static UpgradeCaptainOptions ForLocal(string repoDir)
    {
        DemoPaths paths = DemoPaths.ForLocal() with { RepoDir = Path.GetFullPath(repoDir) };
        return new UpgradeCaptainOptions
        {
            Paths = paths,
            ProjectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"),
            Model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4",
            EnableShell = true,
            EnableFileAccess = true,
            CodeAct = CodeActBackend.None,
            PythonExecutable = Path.Combine(paths.ProjectRoot, ".venv", "bin", "python"),
            EnableCustomHarness = true,
            MemoryRoot = Path.Combine(paths.ProjectRoot, "agent-file-memory"),
            StateRoot = Path.Combine(paths.ProjectRoot, "state"),
        };
    }

    /// <summary>
    /// Contenedor del Hosted Agent (step-4). Shell y ficheros SIGUEN activos, confinados a $HOME/work/canarias-tours
    /// y con lista de permitidos: la frontera real es la VM por sesión de Foundry (ver docs/architecture.md).
    /// El estado vive en $HOME, que Foundry conserva cuando la sesión escala a cero.
    /// </summary>
    public static UpgradeCaptainOptions ForHosted(string home)
    {
        DemoPaths paths = DemoPaths.ForHosted(home, Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_DEMO_ROOT") ?? "/opt/demo");
        return new UpgradeCaptainOptions
        {
            Paths = paths,
            ProjectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT"),
            Model = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
                ?? Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
                ?? "gpt-5.4",
            EnableShell = true,
            EnableFileAccess = true,
            ShellAllowList =
            [
                @"^\s*dotnet(\s|$)",
                @"^\s*git\s+(status|diff|log|add|commit)(\s|$)",
            ],
            CodeAct = CodeActBackend.Local,
            PythonExecutable = "python3",
            TrustSkillScripts = true,
            AutoApproveUnprotected = true,
            EnableCustomHarness = true,
            MemoryRoot = Path.Combine(home, "memory"),
            StateRoot = Path.Combine(home, "state"),
            Requester = "@dependabot",
            Attended = false,
        };
    }
}
