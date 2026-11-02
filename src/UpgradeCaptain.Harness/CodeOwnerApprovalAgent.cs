using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness;

/// <summary>Opciones de <see cref="CodeOwnerApprovalAgent"/>.</summary>
public sealed class CodeOwnerApprovalAgentOptions
{
    /// <summary>Quién pide el trabajo si la sesión no trae JobContext (consola: --as).</summary>
    public string DefaultRequester { get; set; } = "@david-lorenzo";

    /// <summary>Si la sesión no trae JobContext: ¿hay una persona delante?</summary>
    public bool DefaultAttended { get; set; } = true;

    /// <summary>En consola, al aparcar, esperar la decisión y continuar en la misma ejecución.</summary>
    public bool WaitForDecisionWhenAttended { get; set; } = true;

    public TimeSpan DecisionPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Aviso al aparcar (línea de consola en local; log y estado en el Hosted Agent).</summary>
    public Func<PendingApproval, ValueTask>? OnParked { get; set; }

    /// <summary>¿Sigue viva la consola que espera? (sustituible en tests).</summary>
    public Func<int, bool> IsProcessAlive { get; set; } = DefaultIsProcessAlive;

    private static bool DefaultIsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Resultado de <see cref="CodeOwnerApprovalAgent.DecideAsync"/>.</summary>
public sealed record ApprovalDecisionResult(bool Accepted, string Message, AgentResponse? Response = null);

/// <summary>
/// Aprueba el PROPIETARIO del código tocado, no quien pide el trabajo. Es el decorador más externo
/// (envuelve al HarnessAgent):
///   1. Tras cada ejecución, revisa los ToolApprovalRequestContent pendientes:
///      · sin rutas protegidas y con una persona delante → pasan tal cual (aprobación en consola)
///      · con alguna ruta protegida, o en desatendido → se APARCAN: ApprovalStore + SessionVault + AuditLog
///   2. Mientras el lote está aparcado, la sesión no avanza (otra ejecución borraría el request pendiente).
///   3. DecideAsync aplica las reglas (propietario de cada ruta, y distinto de quien pide) y reanuda.
///   4. Al final de cada ejecución añade la línea de estado del harness (verde, bloqueado o rojo).
/// </summary>
public sealed class CodeOwnerApprovalAgent : DelegatingAIAgent
{
    private readonly HarnessServices _services;
    private readonly CodeOwnerApprovalAgentOptions _options;

    public CodeOwnerApprovalAgent(AIAgent innerAgent, HarnessServices services, CodeOwnerApprovalAgentOptions? options = null)
        : base(innerAgent)
    {
        this._services = services;
        this._options = options ?? new CodeOwnerApprovalAgentOptions();
    }

    public HarnessServices Services => this._services;

    // ─────────────────────────────────────────────────────────────────────────
    // Decisión (la llaman `approvals approve|reject`, approve.sh y el Hosted Agent)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decide una aprobación aparcada. Reglas: quien aprueba debe ser propietario (CODEOWNERS) de CADA ruta
    /// tocada y distinto de quien pidió el trabajo. Si nadie está esperando la decisión, reanuda aquí mismo.
    /// </summary>
    /// <param name="resume">false = solo registra la decisión (el Hosted Agent reanuda en segundo plano con ResumeAsync).</param>
    public async Task<ApprovalDecisionResult> DecideAsync(string approvalId, string approverId, bool approved, string? reason, CancellationToken cancellationToken = default, bool resume = true)
    {
        using Activity? span = HarnessDiagnostics.ActivitySource.StartActivity("upgrade_captain.decide");
        PendingApproval? approval = this._services.Approvals.Get(approvalId);
        if (approval is null)
        {
            return new ApprovalDecisionResult(false, $"No existe la aprobación {approvalId}.");
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return new ApprovalDecisionResult(false, $"{approval.Id} ya está decidida ({approval.Status}) por {approval.DecidedBy}.");
        }

        string? refusal = this.Refusal(approval, approverId);
        if (refusal is not null)
        {
            this._services.Audit.Append("refused", approval.JobId, approverId, new { reason = refusal }, approval.Id);
            span?.SetTag("decision", "refused");
            return new ApprovalDecisionResult(false, refusal);
        }

        approval = this._services.Approvals.Update(approval.Id, a => a with
        {
            Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected,
            DecidedBy = approverId,
            DecidedAt = DateTimeOffset.UtcNow,
            Reason = reason,
        });
        this._services.Audit.Append("decided", approval.JobId, approverId, new { approved, reason }, approval.Id);
        span?.SetTag("decision", approved ? "approved" : "rejected");

        string verb = approved ? "Aprobada" : "Rechazada";
        if (approval.WaiterPid is int pid && this._options.IsProcessAlive(pid))
        {
            return new ApprovalDecisionResult(true, $"{verb} {approval.Id} por {approverId}. La consola que espera (PID {pid}) continúa la ejecución.");
        }

        if (this._services.Approvals.OpenBatch(approval.JobId).Any(a => a.Status == ApprovalStatus.Pending))
        {
            return new ApprovalDecisionResult(true, $"{verb} {approval.Id} por {approverId}. Faltan otras decisiones del mismo lote.");
        }

        if (!resume)
        {
            return new ApprovalDecisionResult(true, $"{verb} {approval.Id} por {approverId}. Reanudando en segundo plano.");
        }

        AgentResponse response = await this.ResumeAsync(approval.JobId, cancellationToken).ConfigureAwait(false);
        return new ApprovalDecisionResult(true, $"{verb} {approval.Id} por {approverId}. Ejecución reanudada.", response);
    }

    /// <summary>Reanuda un trabajo aparcado ya decidido: carga la sesión del SessionVault y sigue.</summary>
    public async Task<AgentResponse> ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingApproval> batch = this._services.Approvals.OpenBatch(jobId);
        if (batch.Count == 0 || batch.Any(a => a.Status == ApprovalStatus.Pending))
        {
            throw new InvalidOperationException($"El trabajo {jobId} no tiene un lote decidido que reanudar.");
        }

        JsonElement json = this._services.Sessions.Load(jobId, batch[0].SessionSha256);
        AgentSession session = await this.DeserializeSessionAsync(json, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await this.RunAsync(Array.Empty<ChatMessage>(), session, cancellationToken: cancellationToken).ConfigureAwait(false); // RunCore aplica las decisiones
    }

    /// <summary>Por qué <paramref name="approverId"/> NO puede decidir (todas las razones), o null si puede.</summary>
    private string? Refusal(PendingApproval approval, string approverId)
    {
        var reasons = new List<string>();

        // 1. Debe ser propietario (CODEOWNERS) de CADA ruta tocada.
        var notOwned = approval.Paths.Where(p => !this._services.Policy.IsOwner(approverId, p)).ToList();
        if (notOwned.Count > 0)
        {
            reasons.Add($"no es propietario de {string.Join(", ", notOwned)} según CODEOWNERS (propietario: {string.Join(" o ", this._services.Policy.OwnersFor(notOwned))})");
        }

        // 2. Separación de funciones: quien pide el trabajo no aprueba sus propios cambios.
        if (string.Equals(approverId, approval.RequestedBy, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("pidió este trabajo y no puede aprobar sus propios cambios (separación de funciones)");
        }

        return reasons.Count == 0
            ? null
            : $"{approverId} no puede decidir {approval.Id}: {string.Join("; y además ", reasons)}.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ejecución
    // ─────────────────────────────────────────────────────────────────────────

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default) =>
        this.RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        session ??= await this.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        JobContext job = this.EnsureJob(session);
        await this._services.Checkpoints.EnsureBaselineAsync(cancellationToken).ConfigureAwait(false);
        int verificationsBefore = this._services.Cache.Version;
        List<ChatMessage> input = [.. messages];

        // 1. ¿Hay un lote aparcado? Mientras esté abierto la sesión no avanza: primero se aplica la decisión.
        IReadOnlyList<PendingApproval> batch = this._services.Approvals.OpenBatch(job.JobId);
        if (batch.Count > 0)
        {
            if (batch.Any(a => a.Status == ApprovalStatus.Pending))
            {
                if (!this.CanWait(job))
                {
                    yield return Status($"⏸ La sesión sigue esperando: {Describe(batch)}.");
                    yield break;
                }

                yield return Status($"⏳ Esperando la decisión de {Owners(batch)}…");
                batch = await this.WaitForDecisionsAsync(job.JobId, cancellationToken).ConfigureAwait(false);
            }

            if (input.Any(m => !string.IsNullOrWhiteSpace(m.Text)))
            {
                yield return Status("ℹ Primero aplico la decisión pendiente; repite tu mensaje cuando termine.");
            }

            (ChatMessage decisions, IReadOnlyList<string> lines) = await this.TakeDecisionsAsync(job, batch, cancellationToken).ConfigureAwait(false);
            foreach (string line in lines)
            {
                yield return Status(line);
            }

            input = [decisions];
        }

        // 2. "Aprobar siempre" no puede saltarse al propietario de las rutas protegidas.
        if (this.DowngradeStandingApprovals(input) is { } downgraded)
        {
            input = downgraded;
            yield return Status("ℹ «Aprobar siempre» no se aplica a tools que modifican ficheros: aprobado solo esta vez.");
        }

        while (true)
        {
            // 3. Ejecuta el HarnessAgent; retiene los requests de aprobación hasta saber si hay que aparcarlos.
            var held = new List<ToolApprovalRequestContent>();
            BudgetExceededException? budget = null;
            await using (IAsyncEnumerator<AgentResponseUpdate> stream = this.InnerAgent.RunStreamingAsync(input, session, options, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    try
                    {
                        if (!await stream.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    catch (BudgetExceededException ex)
                    {
                        budget = ex;
                        break;
                    }

                    AgentResponseUpdate update = stream.Current;
                    var requests = update.Contents.OfType<ToolApprovalRequestContent>().ToList();
                    if (requests.Count > 0)
                    {
                        held.AddRange(requests);
                        update = WithoutContents(update, requests);
                        if (update.Contents.Count == 0)
                        {
                            continue;
                        }
                    }

                    yield return update;
                }
            }

            if (budget is not null)
            {
                yield return Status(await this.HandleBudgetAsync(budget, cancellationToken).ConfigureAwait(false));
                await this.PersistAsync(job, session, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            if (held.Count == 0)
            {
                break; // sin aprobaciones pendientes: la ejecución ha terminado
            }

            // 4. ¿Pasa a la consola o se aparca?
            if (job.Attended && !held.Any(this.TouchesProtectedPath))
            {
                await this.PersistAsync(job, session, cancellationToken).ConfigureAwait(false);
                yield return new AgentResponseUpdate(ChatRole.Assistant, [.. held]); // aprobación integrada en consola
                yield break;
            }

            IReadOnlyList<PendingApproval> parked = await this.ParkAsync(job, session, held, cancellationToken).ConfigureAwait(false);
            yield return Status($"⏸ Pendiente de aprobación de {Owners(parked)} ({string.Join(", ", parked.Select(a => a.Id))}): {Describe(parked)}");
            if (!this.CanWait(job))
            {
                yield break;
            }

            // 5. En consola: esperar la decisión (María aprueba desde otra terminal) y seguir en esta misma ejecución.
            yield return Status($"⏳ Esperando la decisión de {Owners(parked)}…");
            batch = await this.WaitForDecisionsAsync(job.JobId, cancellationToken).ConfigureAwait(false);
            (ChatMessage resumed, IReadOnlyList<string> resumeLines) = await this.TakeDecisionsAsync(job, batch, cancellationToken).ConfigureAwait(false);
            foreach (string line in resumeLines)
            {
                yield return Status(line);
            }

            input = [resumed];
        }

        // 6. Veredicto del harness, si hubo verificación durante esta ejecución.
        if (this._services.Cache.Version != verificationsBefore && this.Verdict() is { } verdict)
        {
            yield return Status(verdict);
        }

        await this.PersistAsync(job, session, cancellationToken).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aparcar y reanudar
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<PendingApproval>> ParkAsync(JobContext job, AgentSession session, List<ToolApprovalRequestContent> requests, CancellationToken cancellationToken)
    {
        using Activity? span = HarnessDiagnostics.ActivitySource.StartActivity("upgrade_captain.park");

        // La sesión se guarda DESPUÉS de que la ejecución interna haya terminado: así incluye el request
        // pendiente que el binding de aprobaciones del harness exigirá al reanudar (Q3).
        JsonElement json = await this.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false);
        string sessionSha = this._services.Sessions.Save(job.JobId, json);
        string batchId = Guid.NewGuid().ToString("N")[..8];
        int? waiter = this.CanWait(job) ? Environment.ProcessId : null;

        var parked = new List<PendingApproval>();
        foreach (ToolApprovalRequestContent request in requests)
        {
            FunctionCallContent? call = request.ToolCall as FunctionCallContent;
            string toolName = call?.Name ?? request.ToolCall.GetType().Name;
            IReadOnlyList<string> paths = call is null ? [] : this._services.Policy.PathsFor(call);
            IReadOnlyList<string> owners = this._services.Policy.OwnersFor(paths);

            this._services.Audit.Append("requested", job.JobId, job.RequestedBy, new { tool = toolName, paths });
            PendingApproval approval = this._services.Approvals.Add(id => new PendingApproval
            {
                Id = id,
                JobId = job.JobId,
                BatchId = batchId,
                RequestId = request.RequestId,
                ToolName = toolName,
                ArgumentsJson = call?.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions),
                Paths = paths,
                Owners = owners,
                RequestedBy = job.RequestedBy,
                CreatedAt = DateTimeOffset.UtcNow,
                RequestJson = JsonSerializer.Serialize<AIContent>(request, AIJsonUtilities.DefaultOptions),
                SessionSha256 = sessionSha,
                Attended = job.Attended,
                WaiterPid = waiter,
            });
            this._services.Audit.Append("parked", job.JobId, "harness", new { approval.ToolName, approval.Paths, approval.Owners, sessionSha256 = sessionSha }, approval.Id);
            parked.Add(approval);

            if (this._options.OnParked is { } notify)
            {
                await notify(approval).ConfigureAwait(false);
            }
        }

        span?.SetTag("approvals", string.Join(",", parked.Select(a => a.Id)));
        span?.SetTag("owners", Owners(parked));
        return parked;
    }

    private async Task<IReadOnlyList<PendingApproval>> WaitForDecisionsAsync(string jobId, CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<PendingApproval> batch = this._services.Approvals.OpenBatch(jobId);
            if (batch.Count == 0)
            {
                throw new InvalidOperationException($"Las aprobaciones de {jobId} se aplicaron en otro proceso: importa la sesión guardada.");
            }

            if (!batch.Any(a => a.Status == ApprovalStatus.Pending))
            {
                return batch;
            }

            await Task.Delay(this._options.DecisionPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Convierte las decisiones del lote en ToolApprovalResponseContent respondiendo al request ORIGINAL
    /// (CreateResponse sobre el request guardado: es lo que acepta el binding del harness). Si alguna es un
    /// rechazo, vuelve antes al último verde.
    /// </summary>
    private async Task<(ChatMessage Message, IReadOnlyList<string> Lines)> TakeDecisionsAsync(JobContext job, IReadOnlyList<PendingApproval> batch, CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>();
        var lines = new List<string>();
        foreach (PendingApproval approval in batch)
        {
            var request = (ToolApprovalRequestContent)JsonSerializer.Deserialize<AIContent>(approval.RequestJson, AIJsonUtilities.DefaultOptions)!;
            bool approved = approval.Status == ApprovalStatus.Approved;
            contents.Add(request.CreateResponse(approved, $"{(approved ? "Aprobado" : "Rechazado")} por {approval.DecidedBy}: {approval.Reason}"));
            lines.Add(approved
                ? $"▶ {approval.Id} aprobada por {approval.DecidedBy}{(approval.Reason is null ? string.Empty : $": «{approval.Reason}»")}. Continúo."
                : $"✋ {approval.Id} rechazada por {approval.DecidedBy}{(approval.Reason is null ? string.Empty : $": «{approval.Reason}»")}.");
        }

        if (batch.Any(a => a.Status == ApprovalStatus.Rejected))
        {
            string sha = await this._services.Checkpoints.RestoreLastGreenAsync("aprobación rechazada", cancellationToken).ConfigureAwait(false);
            lines.Add($"↩ Restaurado el último verde ({Short(sha)}).");
        }

        foreach (PendingApproval approval in batch)
        {
            this._services.Approvals.Update(approval.Id, a => a with { AppliedAt = DateTimeOffset.UtcNow });
        }

        this._services.Audit.Append("resumed", job.JobId, "harness", new { approvals = batch.Select(a => a.Id).ToArray(), decisions = batch.Select(a => a.Status.ToString()).ToArray() });
        return (new ChatMessage(ChatRole.User, contents), lines);
    }

    private async Task<string> HandleBudgetAsync(BudgetExceededException budget, CancellationToken cancellationToken)
    {
        this._services.Cache.MarkBudgetExceeded();
        string sha = await this._services.Checkpoints.RestoreLastGreenAsync("presupuesto agotado", cancellationToken).ConfigureAwait(false);
        return $"💸 {budget.Message} Restaurado el último verde ({Short(sha)}). Estado: budget_exceeded.";
    }

    private async Task PersistAsync(JobContext job, AgentSession session, CancellationToken cancellationToken) =>
        this._services.Sessions.Save(job.JobId, await this.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(false));

    // ─────────────────────────────────────────────────────────────────────────
    // Utilidades
    // ─────────────────────────────────────────────────────────────────────────

    private JobContext EnsureJob(AgentSession session)
    {
        if (JobContext.Get(session) is { } job)
        {
            return job;
        }

        job = new JobContext($"console-{DateTime.Now:yyyyMMdd-HHmmss}", this._options.DefaultRequester, this._options.DefaultAttended);
        JobContext.Set(session, job);
        return job;
    }

    private bool CanWait(JobContext job) => job.Attended && this._options.WaitForDecisionWhenAttended;

    private bool TouchesProtectedPath(ToolApprovalRequestContent request) =>
        request.ToolCall is FunctionCallContent call && this._services.Policy.PathsFor(call).Any(this._services.Policy.IsProtected);

    /// <summary>Sustituye las respuestas "aprobar siempre" de tools con rutas por una aprobación de una vez.</summary>
    private List<ChatMessage>? DowngradeStandingApprovals(List<ChatMessage> input)
    {
        bool TouchesFiles(AlwaysApproveToolApprovalResponseContent c) =>
            c.InnerResponse.ToolCall is FunctionCallContent call && this._services.Policy.PathsFor(call).Count > 0;

        if (!input.Any(m => m.Contents.OfType<AlwaysApproveToolApprovalResponseContent>().Any(TouchesFiles)))
        {
            return null;
        }

        return input.Select(m => new ChatMessage(m.Role, m.Contents.Select(c => c is AlwaysApproveToolApprovalResponseContent always && TouchesFiles(always) ? always.InnerResponse : c).ToList())
        {
            AuthorName = m.AuthorName,
            MessageId = m.MessageId,
            AdditionalProperties = m.AdditionalProperties,
        }).ToList();
    }

    private string? Verdict()
    {
        VerificationResult? last = this._services.Cache.Last;
        if (last is null)
        {
            return null;
        }

        CheckpointState checkpoints = this._services.Checkpoints.State;
        if (last.IsGreen)
        {
            string sha = checkpoints.Greens.Count > 0 ? Short(checkpoints.Greens[^1].Sha) : "?";
            return $"✅ Verde verificado por el harness: {last.Summary} ({last.Duration.TotalSeconds:F0} s). Checkpoint {sha} en {this._services.Checkpoints.Branch}.";
        }

        return this._services.Cache.IsStuck
            ? $"⛔ Bloqueado: el mismo fallo {this._services.Cache.ConsecutiveSameFailures} veces seguidas ({last.Summary}). Restaurado el último verde ({Short(checkpoints.LastRestoredTo)})."
            : $"❌ El harness no da la tarea por terminada: {last.Summary}.";
    }

    private static AgentResponseUpdate Status(string text) => new(ChatRole.Assistant, $"\n\n{text}\n");

    private static AgentResponseUpdate WithoutContents(AgentResponseUpdate update, List<ToolApprovalRequestContent> remove) =>
        new(update.Role, update.Contents.Where(c => c is not ToolApprovalRequestContent r || !remove.Contains(r)).ToList())
        {
            AgentId = update.AgentId,
            ResponseId = update.ResponseId,
            MessageId = update.MessageId,
            AuthorName = update.AuthorName,
            CreatedAt = update.CreatedAt,
        };

    private static string Owners(IEnumerable<PendingApproval> approvals)
    {
        var owners = approvals.SelectMany(a => a.Owners).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return owners.Count == 0 ? "cualquier persona distinta de quien lo pidió" : string.Join(" o ", owners);
    }

    private static string Describe(IEnumerable<PendingApproval> approvals) =>
        string.Join("; ", approvals.Select(a => $"{a.ToolName} {(a.Paths.Count == 0 ? string.Empty : string.Join(", ", a.Paths))}".Trim()));

    private static string Short(string? sha) => sha is { Length: >= 7 } ? sha[..7] : sha ?? "?";
}
