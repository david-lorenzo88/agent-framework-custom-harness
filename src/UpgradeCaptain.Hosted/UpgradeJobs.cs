using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using UpgradeCaptain.Core;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Hosted;

/// <summary>
/// Ejecuta los trabajos de actualización dentro del contenedor (una sesión de Foundry = una VM con su $HOME).
///   · La primera vez copia la plantilla /opt/demo/canarias-tours a $HOME/work/canarias-tours.
///   · Todo lo que tiene que sobrevivir a escalar a cero vive en $HOME/state: sesiones, aprobaciones,
///     checkpoints, auditoría y el último estado de cada trabajo.
///   · Los trabajos son desatendidos: lo protegido se aparca para su propietario.
/// </summary>
public sealed class UpgradeJobs : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly UpgradeCaptainOptions _options;
    private readonly string _demoRoot;
    private readonly ILogger<UpgradeJobs> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1); // un trabajo o reanudación a la vez por contenedor
    private readonly ConcurrentDictionary<string, Task> _running = new();
    private readonly Lock _buildLock = new();
    private UpgradeCaptainBuild? _build;

    public UpgradeJobs(UpgradeCaptainOptions options, string demoRoot, ILogger<UpgradeJobs> logger)
    {
        this._options = options;
        this._demoRoot = demoRoot;
        this._logger = logger;
    }

    /// <summary>upgrade.requested: crea la sesión del trabajo y lo ejecuta hasta que termina o se aparca.</summary>
    public async Task<JobStatus> RequestAsync(UpgradeRequest request, bool wait)
    {
        UpgradeCaptainBuild build = this.Build();
        if (build.Services!.Sessions.Exists(request.JobId) || this._running.ContainsKey(request.JobId))
        {
            return this.Status(request.JobId, Lang.Pick("El trabajo ya existe: devuelvo su estado.", "The job already exists: returning its status."));
        }

        Task run = this.RunGuardedAsync(request.JobId, async () =>
        {
            AIAgent agent = build.Agent;
            AgentSession session = await agent.CreateSessionAsync();
            JobContext.Set(session, new JobContext(request.JobId, request.RequestedBy, Attended: false));
            if (agent.GetService<AgentModeProvider>() is { } modes)
            {
                // Desatendido: nadie a quien preguntar en modo plan. Sin aviso de cambio de modo (D-31).
                await modes.SetModeAsync(session, "execute", disableNotification: true);
            }

            AgentResponse response = await agent.RunAsync(request.ToPrompt(), session);
            this._logger.LogInformation("{JobId}: {Text}", request.JobId, Tail(response.Text));
        });

        if (wait)
        {
            await run;
        }

        return this.Status(request.JobId, wait ? null : Lang.Pick("Trabajo en marcha: consulta job.status.", "Job started: check job.status."));
    }

    /// <summary>approval.decided: aplica las reglas del CodeOwnerApprovalAgent y, si el lote está completo, reanuda.</summary>
    public async Task<(JobStatus Status, bool Accepted)> DecideAsync(string jobId, string approvalId, string approver, bool approve, string? reason, bool wait)
    {
        CodeOwnerApprovalAgent approvals = this.Build().Approvals!;
        ApprovalDecisionResult decision = await approvals.DecideAsync(approvalId, approver, approve, reason, CancellationToken.None, resume: false);
        if (!decision.Accepted)
        {
            return (this.Status(jobId, decision.Message), false);
        }

        if (approvals.Services.Approvals.OpenBatch(jobId) is { Count: > 0 } batch && batch.All(a => a.Status != ApprovalStatus.Pending))
        {
            // Reanuda desde la sesión guardada en $HOME/state: funciona igual en un contenedor nuevo.
            Task run = this.RunGuardedAsync(jobId, async () =>
            {
                AgentResponse response = await approvals.ResumeAsync(jobId);
                this._logger.LogInformation("{JobId} reanudado: {Text}", jobId, Tail(response.Text));
            });

            if (wait)
            {
                await run;
            }
        }

        return (this.Status(jobId, decision.Message), true);
    }

    /// <summary>job.status (y la respuesta de todos los eventos).</summary>
    public JobStatus Status(string jobId, string? message = null)
    {
        HarnessServices services = this.Build().Services!;
        var pending = services.Approvals.OpenBatch(jobId)
            .Where(a => a.Status == ApprovalStatus.Pending)
            .Select(a => new ApprovalView(a.Id, a.ToolName, a.Paths, a.Owners, a.RequestedBy))
            .ToList();
        PersistedStatus? persisted = this.ReadPersisted(jobId);

        bool known = this._running.ContainsKey(jobId) || services.Sessions.Exists(jobId) || persisted is not null;
        string status =
            !known ? JobStates.Unknown
            : this._running.ContainsKey(jobId) ? JobStates.Running
            : pending.Count > 0 ? JobStates.WaitingApproval
            : services.Cache.BudgetExceeded ? JobStates.BudgetExceeded
            : services.Cache.Last is { IsGreen: true } ? JobStates.Green
            : services.Cache.IsStuck ? JobStates.Blocked
            : persisted?.Status ?? JobStates.Running;

        string report = Path.Combine(this._options.Paths.RepoDir, "UPGRADE_REPORT.md");
        return new JobStatus(
            jobId,
            status,
            pending,
            services.Cache.Last?.Summary ?? persisted?.LastVerification,
            File.Exists(report) ? report : null,
            message);
    }

    public async ValueTask DisposeAsync()
    {
        if (this._build is not null)
        {
            await this._build.DisposeAsync();
        }
    }

    private async Task RunGuardedAsync(string jobId, Func<Task> body)
    {
        var tcs = new TaskCompletionSource();
        if (!this._running.TryAdd(jobId, tcs.Task))
        {
            throw new InvalidOperationException(Lang.Pick($"{jobId} ya se está ejecutando.", $"{jobId} is already running."));
        }

        await this._gate.WaitAsync();
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "{JobId} falló", jobId);
        }
        finally
        {
            this._gate.Release();
            this._running.TryRemove(jobId, out _);
            this.WritePersisted(jobId);
            tcs.TrySetResult();
        }
    }

    private UpgradeCaptainBuild Build()
    {
        lock (this._buildLock)
        {
            if (this._build is null)
            {
                this.EnsureWorkspace();
                this._build = UpgradeCaptainFactory.Create(this._options);
            }

            return this._build;
        }
    }

    /// <summary>Primera vez en esta sesión: copia la plantilla de la imagen a $HOME/work (que Foundry conserva).</summary>
    private void EnsureWorkspace()
    {
        string repo = this._options.Paths.RepoDir;
        if (Directory.Exists(Path.Combine(repo, ".git")))
        {
            return;
        }

        string template = Path.Combine(this._demoRoot, "canarias-tours");
        this._logger.LogInformation("Copiando {Template} a {Repo}", template, repo);
        CopyDirectory(template, repo);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private string PersistedPath(string jobId) => Path.Combine(this._options.StateRoot, "jobs", jobId + ".json");

    private void WritePersisted(string jobId)
    {
        JobStatus status = this.Status(jobId);
        Directory.CreateDirectory(Path.GetDirectoryName(this.PersistedPath(jobId))!);
        File.WriteAllText(this.PersistedPath(jobId), JsonSerializer.Serialize(new PersistedStatus(status.Status, status.LastVerification), JsonOptions));
    }

    private PersistedStatus? ReadPersisted(string jobId) =>
        File.Exists(this.PersistedPath(jobId))
            ? JsonSerializer.Deserialize<PersistedStatus>(File.ReadAllText(this.PersistedPath(jobId)))
            : null;

    private static string Tail(string text) => text.Length <= 600 ? text : "…" + text[^600..];

    private sealed record PersistedStatus(string Status, string? LastVerification);
}
