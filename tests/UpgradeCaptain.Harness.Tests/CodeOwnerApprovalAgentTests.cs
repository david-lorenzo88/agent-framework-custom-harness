#pragma warning disable MAAI001 // FileSystemAgentFileStore / FileAccessStore son experimentales en Agent Framework.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// El "broker" de aprobaciones de punta a punta, con un HarnessAgent REAL y un modelo guionizado:
/// el modelo edita FareCalculator.cs (Payments, protegido) → se aparca → reglas de decisión → reanudar.
/// </summary>
public class CodeOwnerApprovalAgentTests : IDisposable
{
    private const string FarePath = "src/Canarias.Tours.Payments/FareCalculator.cs";
    private const string DomainPath = "src/Canarias.Tours.Domain/RoutePlanner.cs";
    private const string Original = "double km = GeoCalculator.Distance(from, to);";
    private const string Fixed = "double km = GeoCalculator.Distance(from, to, DistanceUnit.Kilometers);";

    private readonly TempDir _temp = new();
    private readonly string _repo;
    private readonly string _state;

    public CodeOwnerApprovalAgentTests()
    {
        this._repo = this._temp.Sub("repo");
        this._state = this._temp.Sub("state");
        Directory.CreateDirectory(Path.Combine(this._repo, ".github"));
        File.WriteAllText(Path.Combine(this._repo, ".github", "CODEOWNERS"),
            "*                              @david-lorenzo\n/src/Canarias.Tours.Payments/  @maria-payments\n");
        foreach (string file in new[] { FarePath, DomainPath })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(this._repo, file))!);
            File.WriteAllText(Path.Combine(this._repo, file), Original + "\n");
        }
    }

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public async Task ProtectedEdit_Unattended_IsParked_NotExecuted()
    {
        var (agent, _) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-001", "@dependabot", attended: false);

        AgentResponse response = await agent.RunAsync("Arregla las tarifas", session);

        Assert.Contains("⏸ Pendiente de aprobación de @maria-payments (APR-0001)", response.Text);
        Assert.DoesNotContain(response.Messages.SelectMany(m => m.Contents), c => c is ToolApprovalRequestContent);
        PendingApproval parked = Assert.Single(agent.Services.Approvals.List());
        Assert.Equal(ApprovalStatus.Pending, parked.Status);
        Assert.Equal([FarePath], parked.Paths);
        Assert.Equal(["@maria-payments"], parked.Owners);
        Assert.Equal("@dependabot", parked.RequestedBy);
        Assert.Equal(Original, this.ReadFare());
    }

    [Fact]
    public async Task Requester_CannotApprove_NonOwner_CannotApprove()
    {
        var (agent, _) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-001", "@david-lorenzo", attended: false);
        await agent.RunAsync("Arregla las tarifas", session);

        // David pidió el trabajo y además no es propietario de Payments: las dos razones.
        ApprovalDecisionResult byRequester = await agent.DecideAsync("APR-0001", "@david-lorenzo", true, "ok");
        Assert.False(byRequester.Accepted);
        Assert.Contains("no es propietario de src/Canarias.Tours.Payments/FareCalculator.cs", byRequester.Message);
        Assert.Contains("separación de funciones", byRequester.Message);

        // Otro que no es propietario de Payments.
        ApprovalDecisionResult byStranger = await agent.DecideAsync("APR-0001", "@otro-dev", true, "ok");
        Assert.False(byStranger.Accepted);
        Assert.Contains("no es propietario", byStranger.Message);

        Assert.Equal(ApprovalStatus.Pending, agent.Services.Approvals.Get("APR-0001")!.Status);
        Assert.Equal(2, agent.Services.Audit.ReadAll().Count(e => e.Event == "refused"));
        Assert.Equal(Original, this.ReadFare());
    }

    [Fact]
    public async Task Owner_WhoRequestedTheJob_CannotApprove()
    {
        var (agent, _) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-002", "@maria-payments", attended: false);
        await agent.RunAsync("Arregla las tarifas", session);

        ApprovalDecisionResult result = await agent.DecideAsync("APR-0001", "@maria-payments", true, "mío");

        Assert.False(result.Accepted);
        Assert.DoesNotContain("no es propietario", result.Message);
        Assert.Contains("separación de funciones", result.Message);
    }

    [Fact]
    public async Task OwnerApproves_ResumesFromSessionVault_InAFreshAgent()
    {
        var (agent, _) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-001", "@dependabot", attended: false);
        await agent.RunAsync("Arregla las tarifas", session);

        // Otro proceso: agente y servicios nuevos sobre el mismo estado en disco (como el Hosted Agent tras escalar a cero).
        var (fresh, freshModel) = this.CreateFresh(FarePath, attended: false);
        ApprovalDecisionResult result = await fresh.DecideAsync("APR-0001", "@maria-payments", true, "Revisado: tarifa en km");

        Assert.True(result.Accepted, result.Message);
        Assert.NotNull(result.Response);
        Assert.Contains("▶ APR-0001 aprobada por @maria-payments", result.Response!.Text);
        Assert.Contains("Listo", result.Response.Text);
        Assert.Equal(Fixed, this.ReadFare());
        Assert.Equal(1, freshModel.Calls); // el modelo del agente nuevo solo ve el resultado de la tool aprobada

        AuditLog audit = fresh.Services.Audit;
        Assert.Equal(["requested", "parked", "decided", "resumed"], audit.ReadAll().Select(e => e.Event).ToArray());
        Assert.True(audit.Verify().Ok);
        Assert.NotNull(fresh.Services.Approvals.Get("APR-0001")!.AppliedAt);
    }

    [Fact]
    public async Task OwnerRejects_RestoresLastGreen_AndToolDoesNotRun()
    {
        var (agent, checkpoints) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-001", "@dependabot", attended: false);
        await agent.RunAsync("Arregla las tarifas", session);

        ApprovalDecisionResult result = await agent.DecideAsync("APR-0001", "@maria-payments", false, "No toques tarifas sin ticket");

        Assert.True(result.Accepted);
        Assert.Contains("✋ APR-0001 rechazada por @maria-payments", result.Response!.Text);
        Assert.Equal(["approval rejected"], checkpoints.Restores);
        Assert.Equal(Original, this.ReadFare());
    }

    [Fact]
    public async Task NonProtectedEdit_Attended_PassesThroughToConsole()
    {
        var (agent, _) = this.Create(DomainPath, attended: true);
        AgentSession session = await NewSessionAsync(agent, "console-1", "@david-lorenzo", attended: true);

        AgentResponse response = await agent.RunAsync("Arregla el planificador", session);

        Assert.Contains(response.Messages.SelectMany(m => m.Contents), c => c is ToolApprovalRequestContent);
        Assert.Empty(agent.Services.Approvals.List());
    }

    [Fact]
    public async Task ProtectedEdit_Attended_WaitsForOwner_AndContinuesInTheSameRun()
    {
        var (agent, _) = this.Create(FarePath, attended: true);
        AgentSession session = await NewSessionAsync(agent, "console-1", "@david-lorenzo", attended: true);

        Task<AgentResponse> run = agent.RunAsync("Arregla las tarifas", session);

        // "María, en otra terminal": espera a que aparezca la aprobación aparcada y la decide.
        PendingApproval parked = await WaitForAsync(() => agent.Services.Approvals.Get("APR-0001"));
        Assert.Equal(Environment.ProcessId, parked.WaiterPid);
        ApprovalDecisionResult decision = await agent.DecideAsync("APR-0001", "@maria-payments", true, "Revisado");
        Assert.Contains("La ejecución que espera", decision.Message);
        Assert.Null(decision.Response); // no reanuda aquí: reanuda la consola que está esperando

        AgentResponse response = await run.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("⏸ Pendiente de aprobación de @maria-payments", response.Text);
        Assert.Contains("▶ APR-0001 aprobada por @maria-payments", response.Text);
        Assert.Equal(Fixed, this.ReadFare());
    }

    [Fact]
    public async Task WhileParked_TheSessionDoesNotAdvance()
    {
        var (agent, _) = this.Create(FarePath, attended: false);
        AgentSession session = await NewSessionAsync(agent, "job-001", "@dependabot", attended: false);
        await agent.RunAsync("Arregla las tarifas", session);

        AgentResponse again = await agent.RunAsync("¿Sigues ahí?", session);

        Assert.Contains("⏸ La sesión sigue esperando", again.Text);
        Assert.Equal(ApprovalStatus.Pending, agent.Services.Approvals.Get("APR-0001")!.Status);
    }

    // ── Montaje ───────────────────────────────────────────────────────────────

    /// <summary>
    /// HarnessAgent real (sin web, sin memoria, sin skills) con acceso a ficheros sobre el repo temporal,
    /// envuelto en CodeOwnerApprovalAgent. El modelo guionizado pide UNA edición y luego dice "Listo".
    /// </summary>
    private (CodeOwnerApprovalAgent Agent, ScriptedChatClient Model) CreateWithModel(string editPath, bool attended, out FakeCheckpoints checkpoints)
    {
        var model = new ScriptedChatClient((messages, _) =>
            messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
                ? new ChatMessage(ChatRole.Assistant, "Listo: tarifa en kilómetros.")
                : new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_fix", "file_access_replace", new Dictionary<string, object?>
                    {
                        ["fileName"] = editPath,
                        ["oldString"] = Original,
                        ["newString"] = Fixed,
                    }),
                ]));

        HarnessAgent inner = model.AsHarnessAgent(new HarnessAgentOptions
        {
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableAgentSkillsProvider = true,
            FileAccessStore = new FileSystemAgentFileStore(this._repo),
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions { AutoApprovalRules = [FileAccessProvider.ReadOnlyToolsAutoApprovalRule] },
            AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
        });

        checkpoints = new FakeCheckpoints(this._state);
        var audit = new AuditLog(this._state);
        var services = new HarnessServices(
            this._repo,
            this._state,
            new FakeVerifier(Results.Green()),
            new RepoStateCache(),
            checkpoints,
            new SessionVault(this._state),
            new ApprovalStore(this._state),
            audit,
            CodeOwnersPolicy.Load(this._repo));

        var agent = new CodeOwnerApprovalAgent(inner, services, new CodeOwnerApprovalAgentOptions
        {
            DefaultAttended = attended,
            DecisionPollInterval = TimeSpan.FromMilliseconds(50),
            IsProcessAlive = pid => pid == Environment.ProcessId,
        });
        return (agent, model);
    }

    private (CodeOwnerApprovalAgent Agent, FakeCheckpoints Checkpoints) Create(string editPath, bool attended)
    {
        var (agent, _) = this.CreateWithModel(editPath, attended, out FakeCheckpoints checkpoints);
        return (agent, checkpoints);
    }

    private (CodeOwnerApprovalAgent Agent, ScriptedChatClient Model) CreateFresh(string editPath, bool attended) =>
        this.CreateWithModel(editPath, attended, out _);

    private static async Task<AgentSession> NewSessionAsync(AIAgent agent, string jobId, string requester, bool attended)
    {
        AgentSession session = await agent.CreateSessionAsync();
        JobContext.Set(session, new JobContext(jobId, requester, attended));
        return session;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe)
        where T : class
    {
        for (int i = 0; i < 200; i++)
        {
            if (probe() is { } value)
            {
                return value;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("No llegó a aparcarse la aprobación.");
    }

    private string ReadFare() => File.ReadAllText(Path.Combine(this._repo, FarePath)).Trim();
}
