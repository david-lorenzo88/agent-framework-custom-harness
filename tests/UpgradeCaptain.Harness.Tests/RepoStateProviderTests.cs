using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// Las aprobaciones las decide el harness, fuera de la conversación. RepoStateProvider se las cuenta al modelo para
/// que el informe final diga quién aprobó qué (en el ensayo del step-7 decía «Aprobaciones: ninguna»).
/// </summary>
public class RepoStateProviderTests : IDisposable
{
    private readonly string _state = Directory.CreateTempSubdirectory("uc-repostate-").FullName;

    [Fact]
    public async Task Instructions_ListPendingAndDecidedApprovalsOfTheJob()
    {
        var store = new ApprovalStore(this._state);
        Add(store, "job-1");
        Add(store, "job-1");
        Add(store, "otro-trabajo");
        store.Update("APR-0001", a => a with { Status = ApprovalStatus.Approved, DecidedBy = "@maria-payments", DecidedAt = DateTimeOffset.UtcNow });

        var provider = new RepoStateProvider(new RepoStateCache(), store, this._state);
        AIAgent agent = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok")).AsAIAgent();
        AgentSession session = await agent.CreateSessionAsync();
        JobContext.Set(session, new JobContext("job-1", "@dependabot", Attended: false));

        AIContext context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(agent, session, new AIContext()));

        Assert.Contains("Aprobaciones pendientes: APR-0002", context.Instructions);
        Assert.Contains("Aprobaciones decididas: APR-0001 src/Canarias.Tours.Payments/FareCalculator.cs aprobada por @maria-payments", context.Instructions);
        Assert.DoesNotContain("APR-0003", context.Instructions);
    }

    private static void Add(ApprovalStore store, string jobId) => store.Add(id => new PendingApproval
    {
        Id = id,
        JobId = jobId,
        BatchId = "b1",
        RequestId = id,
        ToolName = "file_access_write",
        ArgumentsJson = "{}",
        Paths = ["src/Canarias.Tours.Payments/FareCalculator.cs"],
        Owners = ["@maria-payments"],
        RequestedBy = "@dependabot",
        CreatedAt = DateTimeOffset.UtcNow,
        RequestJson = "{}",
        SessionSha256 = "x",
    });

    public void Dispose() => Directory.Delete(this._state, recursive: true);
}
