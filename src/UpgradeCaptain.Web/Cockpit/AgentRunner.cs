#pragma warning disable MAAI001 // AgentModeProvider, TodoProvider y el bucle son experimentales en Agent Framework.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Los agentes que puede montar la cabina: uno por paso de la charla, todos con UpgradeCaptainFactory.</summary>
public enum AgentStage
{
    /// <summary>step-1 · el HarnessAgent con las 5 tools de solo lectura, modo plan y bucle de todos.</summary>
    Step1,

    /// <summary>step-2 · + shell confinado, ficheros, aprobaciones y skills.</summary>
    Step2,

    /// <summary>step-3 sin VerifiedCompletionEvaluator: el muro.</summary>
    Step3Pre,

    /// <summary>step-3 · el harness a medida completo.</summary>
    Step3,
}

public enum RunState
{
    Off,
    Building,
    Idle,
    Running,
    AwaitingUser,
}

/// <summary>
/// Ejecuta el agente del paso activo y convierte el streaming en entradas del panel. Es el bucle de la consola
/// vendorizada (HarnessAgentRunner + observers) para la web:
///   · modo plan: pide el esquema PlanningResponse y pinta preguntas o el plan para aprobar
///   · aprobaciones integradas (ToolApprovalRequestContent): tarjeta con el diff del cambio
///   · líneas de estado del harness a medida (CodeOwnerApprovalAgent.StatusProperty) y feedback del bucle
///   · las respuestas a todas las preguntas de un turno se envían juntas, como hace la consola
/// </summary>
public sealed class AgentRunner(Action notify) : IAsyncDisposable
{
    public const string ApprovePlan = "\u0001approve";

    private readonly object _gate = new();
    private readonly List<TranscriptEntry> _entries = [];
    private readonly List<ApprovalEntry> _pendingApprovals = [];
    private readonly Dictionary<string, (ToolEntry Entry, Stopwatch Clock)> _tools = [];
    private PlanEntry? _pendingPlan;
    private CancellationTokenSource? _cts;
    private Task _run = Task.CompletedTask;

    public AgentStage? Stage { get; private set; }

    public DemoLanguage Language { get; private set; } = DemoLanguage.En;

    public bool TinyContext { get; private set; }

    public UpgradeCaptainBuild? Build { get; private set; }

    public AgentSession? Session { get; private set; }

    public string? JobId { get; private set; }

    public string Mode { get; private set; } = "plan";

    public RunState State { get; private set; } = RunState.Off;

    public bool WaitingForOwner { get; private set; }

    public IReadOnlyList<TodoItem> Todos { get; private set; } = [];

    public UsageDetails? LastUsage { get; private set; }

    public int ModelCalls { get; private set; }

    public Stopwatch? RunClock { get; private set; }

    public IReadOnlyList<TranscriptEntry> Entries
    {
        get
        {
            lock (this._gate)
            {
                return [.. this._entries];
            }
        }
    }

    public HarnessServices? Services => this.Build?.Services;

    public CodeOwnerApprovalAgent? Approvals => this.Build?.Approvals;

    public IReadOnlyList<string> LoopEvaluators =>
        this.Build?.HarnessOptions.LoopEvaluators?.Select(e => e.GetType().Name).ToList() ?? [];

    private AgentModeProvider? Modes => this.Build?.Agent.GetService<AgentModeProvider>();

    private string L(string es, string en) => this.Language.Pick(es, en);

    /// <summary>Monta el agente de un paso con una sesión nueva (y un JobContext si lleva el harness a medida).</summary>
    public async Task StartAsync(AgentStage stage, DemoLanguage language, bool tinyContext = false)
    {
        await this.StopAsync().ConfigureAwait(false);
        this.Stage = stage;
        this.Language = language;
        this.TinyContext = tinyContext;
        this.State = RunState.Building;
        this.WaitingForOwner = false;
        this.Todos = [];
        this.LastUsage = null;
        this.ModelCalls = 0;
        lock (this._gate)
        {
            this._entries.Clear();
            this._pendingApprovals.Clear();
            this._tools.Clear();
            this._pendingPlan = null;
        }

        notify();

        try
        {
            UpgradeCaptainOptions options = OptionsFor(stage, language, tinyContext);
            this.Build = UpgradeCaptainFactory.Create(options);
            this.Session = await this.Build.Agent.CreateSessionAsync().ConfigureAwait(false);
            if (this.Build.Services is not null)
            {
                this.JobId = $"cockpit-{DateTime.Now:yyyyMMdd-HHmmss}";
                JobContext.Set(this.Session, new JobContext(this.JobId, options.Requester, Attended: true));
            }

            this.Mode = await this.Modes!.GetModeAsync(this.Session).ConfigureAwait(false);
            this.State = RunState.Idle;
            this.Add(new NoticeEntry(this.L(
                $"Agente del {StageName(stage)} listo · modo {this.Mode} · evaluadores: {string.Join(" → ", this.LoopEvaluators)}",
                $"{StageName(stage)} agent ready · {this.Mode} mode · evaluators: {string.Join(" → ", this.LoopEvaluators)}"), NoticeLevel.Success));
        }
        catch (Exception ex)
        {
            this.State = RunState.Off;
            this.Add(new NoticeEntry(this.L($"No se pudo montar el agente: {ex.Message}", $"Couldn't build the agent: {ex.Message}"), NoticeLevel.Error));
        }

        notify();
    }

    /// <summary>Las opciones de cada paso. La misma factoría que la consola, con los interruptores del paso.</summary>
    public static UpgradeCaptainOptions OptionsFor(AgentStage stage, DemoLanguage language, bool tinyContext = false)
    {
        UpgradeCaptainOptions options = UpgradeCaptainOptions.ForLocal(DemoPaths.Current.RepoDir);
        options.Language = language;
        options.Requester = "@david-lorenzo";
        if (tinyContext)
        {
            (options.MaxContextWindowTokens, options.MaxOutputTokens) = (16_000, 8_000); // como --context tiny
        }

        switch (stage)
        {
            case AgentStage.Step1:
                options.EnableShell = false;
                options.EnableFileAccess = false;
                options.EnableSkills = false;
                options.EnableCustomHarness = false;
                break;
            case AgentStage.Step2:
                options.EnableCustomHarness = false;
                break;
            case AgentStage.Step3Pre:
                options.EnableVerifiedCompletion = false;
                break;
        }

        return options;
    }

    public static string StageName(AgentStage stage) => stage switch
    {
        AgentStage.Step1 => "step-1",
        AgentStage.Step2 => "step-2",
        AgentStage.Step3Pre => "step-3-pre",
        _ => "step-3",
    };

    public async Task SetModeAsync(string mode)
    {
        if (this.Modes is not { } modes || this.Session is null || this.State is RunState.Running or RunState.Building)
        {
            return;
        }

        // disableNotification: el aviso de cambio de modo dispara el filtro de contenido de Foundry (D-31).
        await modes.SetModeAsync(this.Session, mode, disableNotification: true).ConfigureAwait(false);
        this.Mode = mode;
        this.Add(new NoticeEntry(this.L($"Modo → {mode}", $"Mode → {mode}")));
        notify();
    }

    public void Send(string text)
    {
        if (this.State != RunState.Idle || this.Session is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        this.Add(new UserEntry(text.Trim()));
        this.Launch([new ChatMessage(ChatRole.User, text.Trim())]);
    }

    public void Answer(ApprovalEntry entry, ApprovalChoice choice)
    {
        if (entry.Answer is not null)
        {
            return;
        }

        entry.Answer = choice;
        if (choice == ApprovalChoice.Deny && entry.Call is { } call && this._tools.TryGetValue(call.CallId, out var tool))
        {
            tool.Entry.State = ToolState.Denied;
        }

        this.ContinueIfAnswered();
    }

    public async Task AnswerAsync(PlanEntry entry, int question, string answer)
    {
        if (entry.Answers[question] is not null || string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        if (entry.IsApproval && answer == ApprovePlan && this.Modes is { } modes && this.Session is not null)
        {
            await modes.SetModeAsync(this.Session, "execute", disableNotification: true).ConfigureAwait(false);
            this.Mode = "execute";
            this.Add(new NoticeEntry(this.L("✅ Plan aprobado: modo execute", "✅ Plan approved: execute mode"), NoticeLevel.Success));
        }

        entry.Answers[question] = answer;
        this.ContinueIfAnswered();
    }

    public void Cancel() => this._cts?.Cancel();

    public async Task StopAsync()
    {
        this._cts?.Cancel();
        try
        {
            await this._run.ConfigureAwait(false);
        }
        catch
        {
            // la ejecución anterior ya informó de su error
        }

        if (this.Build is not null)
        {
            await this.Build.DisposeAsync().ConfigureAwait(false);
        }

        this.Build = null;
        this.Session = null;
        this.JobId = null;
        this.State = RunState.Off;
    }

    public async Task RefreshTodosAsync()
    {
        if (this.Build?.Agent.GetService<TodoProvider>() is { } todos && this.Session is not null)
        {
            this.Todos = [.. await todos.GetAllTodosAsync(this.Session).ConfigureAwait(false)];
        }
    }

    public async ValueTask DisposeAsync() => await this.StopAsync().ConfigureAwait(false);

    // ─────────────────────────────────────────────────────────────────────────
    // El bucle
    // ─────────────────────────────────────────────────────────────────────────

    private void ContinueIfAnswered()
    {
        List<ChatMessage> messages = [];
        lock (this._gate)
        {
            if (this._pendingApprovals.Any(a => a.Answer is null) || this._pendingPlan is { Complete: false })
            {
                notify();
                return;
            }

            foreach (ApprovalEntry approval in this._pendingApprovals)
            {
                AIContent response = approval.Answer switch
                {
                    ApprovalChoice.AlwaysTool => approval.Request.CreateAlwaysApproveToolResponse("User chose to always approve this tool"),
                    ApprovalChoice.AlwaysArguments => approval.Request.CreateAlwaysApproveToolWithArgumentsResponse("User chose to always approve this tool with these arguments"),
                    ApprovalChoice.Deny => approval.Request.CreateResponse(approved: false, reason: "User denied"),
                    _ => approval.Request.CreateResponse(approved: true, reason: "User approved"),
                };
                messages.Add(new ChatMessage(ChatRole.User, [response]));
            }

            if (this._pendingPlan is { } plan)
            {
                for (int i = 0; i < plan.Plan.Questions.Count; i++)
                {
                    string answer = plan.Answers[i]!;
                    messages.Add(plan.IsApproval
                        ? new ChatMessage(ChatRole.User, answer == ApprovePlan ? "Approved" : answer)
                        : new ChatMessage(ChatRole.User, $"Q: {plan.Plan.Questions[i].Message}\nA: {answer}"));
                }
            }

            this._pendingApprovals.Clear();
            this._pendingPlan = null;
        }

        this.Launch(messages);
    }

    private void Launch(IList<ChatMessage> messages)
    {
        this._cts = new CancellationTokenSource();
        this.State = RunState.Running;
        this.RunClock = Stopwatch.StartNew();
        notify();
        CancellationToken token = this._cts.Token;
        this._run = Task.Run(() => this.RunLoopAsync(messages, token));
    }

    private async Task RunLoopAsync(IList<ChatMessage> input, CancellationToken cancellationToken)
    {
        IList<ChatMessage>? next = input;
        while (next is not null && this.Build is { } build && this.Session is { } session)
        {
            this.Mode = await this.Modes!.GetModeAsync(session, cancellationToken).ConfigureAwait(false);
            bool planning = this.Mode == "plan";
            var runOptions = new AgentRunOptions();
            if (planning)
            {
                runOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlanningResponse>();
            }

            AssistantEntry? current = null;
            string? currentKey = null;
            var tools = this._tools;
            var approvals = new List<ApprovalEntry>();
            try
            {
                await foreach (AgentResponseUpdate update in build.Agent.RunStreamingAsync(next, session, runOptions, cancellationToken).ConfigureAwait(false))
                {
                    // 1. Líneas de estado del harness a medida.
                    if (update.AdditionalProperties?.ContainsKey(CodeOwnerApprovalAgent.StatusProperty) == true)
                    {
                        var harness = new HarnessEntry(update.Text, HarnessEntry.KindOf(update.Text));
                        this.WaitingForOwner = harness.Kind is HarnessKind.Waiting or HarnessKind.Parked
                            || (this.WaitingForOwner && harness.Kind is not (HarnessKind.Resumed or HarnessKind.Rejected or HarnessKind.Restored));
                        this.Add(harness);
                        current = null;
                        notify();
                        continue;
                    }

                    // 2. Feedback del bucle (evaluadores): llega como mensaje de usuario.
                    if (update.Role == ChatRole.User)
                    {
                        if (!string.IsNullOrWhiteSpace(update.Text))
                        {
                            this.Add(new HarnessEntry(update.Text, update.Text.TrimStart().StartsWith("🔎", StringComparison.Ordinal) ? HarnessKind.Verification : HarnessKind.Loop));
                            current = null;
                        }

                        notify();
                        continue;
                    }

                    this.WaitingForOwner = false;
                    foreach (AIContent content in update.Contents)
                    {
                        switch (content)
                        {
                            case FunctionCallContent call when !tools.ContainsKey(call.CallId):
                                var tool = new ToolEntry { CallId = call.CallId, Name = call.Name, Arguments = call.Arguments, State = ToolState.Running };
                                tools[call.CallId] = (tool, Stopwatch.StartNew());
                                this.Add(tool);
                                current = null;
                                break;
                            case FunctionResultContent result when tools.TryGetValue(result.CallId, out var running) && running.Entry.Result is null:
                                running.Entry.Result = ToolText.ResultText(result.Exception?.Message ?? result.Result);
                                running.Entry.State = running.Entry.State == ToolState.Denied ? ToolState.Denied
                                    : result.Exception is null && !LooksDenied(running.Entry.Result) ? ToolState.Done : ToolState.Failed;
                                running.Entry.Duration = running.Clock.Elapsed;
                                if (ToolText.Family(running.Entry.Name) == "todo")
                                {
                                    await this.RefreshTodosAsync().ConfigureAwait(false);
                                }

                                break;
                            case ToolApprovalRequestContent request:
                                if (request.ToolCall is FunctionCallContent pending)
                                {
                                    if (!tools.TryGetValue(pending.CallId, out var waiting))
                                    {
                                        waiting = (new ToolEntry { CallId = pending.CallId, Name = pending.Name, Arguments = pending.Arguments }, new Stopwatch());
                                        tools[pending.CallId] = waiting;
                                        this.Add(waiting.Entry);
                                    }

                                    waiting.Entry.State = ToolState.AwaitingApproval;
                                    waiting.Clock.Restart();
                                }

                                var approval = new ApprovalEntry { Request = request, Preview = this.PreviewFor(request.ToolCall as FunctionCallContent) };
                                approvals.Add(approval);
                                this.Add(approval);
                                current = null;
                                break;
                            case UsageContent usage:
                                this.LastUsage = usage.Details;
                                this.ModelCalls++;
                                break;
                            case ErrorContent error:
                                this.Add(new NoticeEntry(error.Message, NoticeLevel.Error));
                                break;
                        }
                    }

                    // 3. Texto del modelo, una entrada por mensaje.
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        string key = $"{update.ResponseId}/{update.MessageId}";
                        if (current is null || key != currentKey)
                        {
                            if (current is not null)
                            {
                                current.Streaming = false;
                            }

                            current = new AssistantEntry { Planning = planning };
                            currentKey = key;
                            this.Add(current);
                        }

                        current.Text += update.Text;
                    }

                    notify();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this.Finish(current, tools.Values.Select(t => t.Entry).Where(t => t.State == ToolState.Running));
                this.Add(new NoticeEntry(this.L("⏹ Detenido. La sesión sigue siendo válida.", "⏹ Stopped. The session is still valid."), NoticeLevel.Warning));
                this.State = RunState.Idle;
                notify();
                return;
            }
            catch (Exception ex)
            {
                this.Finish(current, tools.Values.Select(t => t.Entry).Where(t => t.State == ToolState.Running));
                this.Add(new NoticeEntry(this.L(
                    $"❌ Error en el streaming ({ex.GetType().Name}): {ex.Message}. La sesión sigue siendo válida: escribe «Sigue».",
                    $"❌ Stream error ({ex.GetType().Name}): {ex.Message}. The session is still valid: type \"Continue\"."), NoticeLevel.Error));
                this.State = RunState.Idle;
                notify();
                return;
            }

            this.Finish(current, tools.Values.Select(t => t.Entry).Where(t => t.State == ToolState.Running));

            // 4. En modo plan, el último mensaje es el PlanningResponse.
            if (planning && current is not null && TryParsePlan(current.Text) is { } plan)
            {
                var entry = new PlanEntry(plan);
                lock (this._gate)
                {
                    int index = this._entries.IndexOf(current);
                    this._entries[index] = entry;
                    this._pendingPlan = entry;
                }
            }

            await this.RefreshTodosAsync().ConfigureAwait(false);
            this.Mode = await this.Modes!.GetModeAsync(session, CancellationToken.None).ConfigureAwait(false);

            lock (this._gate)
            {
                this._pendingApprovals.AddRange(approvals);
                if (this._pendingApprovals.Count > 0 || this._pendingPlan is not null)
                {
                    this.State = RunState.AwaitingUser;
                    notify();
                    return;
                }
            }

            next = null;
        }

        this.State = this.Build is null ? RunState.Off : RunState.Idle;
        notify();
    }

    private void Finish(AssistantEntry? current, IEnumerable<ToolEntry> tools)
    {
        // Una tool o una aprobación a mitad de mensaje deja atrás entradas de texto: al acabar, ninguna sigue "escribiendo".
        lock (this._gate)
        {
            foreach (AssistantEntry entry in this._entries.OfType<AssistantEntry>())
            {
                entry.Streaming = false;
            }
        }

        foreach (ToolEntry tool in tools.Where(t => t.State == ToolState.Running && t.Result is null))
        {
            tool.State = ToolState.Failed;
        }

        this.RunClock?.Stop();
    }

    private static bool LooksDenied(string? result) =>
        result is not null && (result.Contains("rejected by policy", StringComparison.OrdinalIgnoreCase)
            || result.StartsWith("User denied", StringComparison.Ordinal)
            || result.Contains("not approved", StringComparison.OrdinalIgnoreCase));

    private static PlanningResponse? TryParsePlan(string text)
    {
        try
        {
            PlanningResponse? plan = JsonSerializer.Deserialize<PlanningResponse>(text.Trim());
            return plan is { Questions.Count: > 0 } ? plan : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private FileChangePreview? PreviewFor(FunctionCallContent? call) =>
        call is null || this.Build is null ? null : Preview(call.Name, call.Arguments, this.Build.Options.Paths.RepoDir, this.Services?.Policy);

    /// <summary>Diff del cambio que se va a aprobar (file_access_write/replace) frente al fichero actual del repo.</summary>
    public static FileChangePreview? Preview(string tool, IDictionary<string, object?>? arguments, string repoDir, CodeOwnersPolicy? policy)
    {
        if (!ToolText.IsWrite(tool))
        {
            return null;
        }

        string? Arg(string name) => arguments is not null && arguments.TryGetValue(name, out object? v) && v is not null
            ? v is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : v.ToString()
            : null;

        string? fileName = Arg("fileName");
        if (fileName is null)
        {
            return null;
        }

        string repo = Path.GetFullPath(repoDir);
        string full = Path.GetFullPath(Path.Combine(repo, fileName));
        if (!full.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        string before = File.Exists(full) ? File.ReadAllText(full) : string.Empty;
        string? after = tool switch
        {
            "file_access_write" => Arg("content"),
            "file_access_replace" when Arg("oldString") is { } oldString && Arg("newString") is { } newString =>
                bool.TryParse(Arg("replaceAll"), out bool all) && all ? before.Replace(oldString, newString) : ReplaceFirst(before, oldString, newString),
            "file_access_delete" => string.Empty,
            _ => null,
        };

        bool isProtected = policy?.IsProtected(fileName) == true;
        return after is null ? null : new FileChangePreview(fileName, LineDiff.Compute(before, after), isProtected);
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        int index = text.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? text : string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }

    private void Add(TranscriptEntry entry)
    {
        lock (this._gate)
        {
            this._entries.Add(entry);
        }
    }
}
