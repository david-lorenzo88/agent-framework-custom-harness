using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness.Tests;

public class BudgetGuardTests
{
    [Fact]
    public async Task OverBudget_NextCallThrows()
    {
        var model = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok"), tokensPerCall: 600);
        var guard = new BudgetGuard(model, budget: 1000);
        var agent = new ChatClientAgent(guard);
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("uno", session);          // 600
        await agent.RunAsync("dos", session);          // 1200 (> 1000 tras la llamada)
        var ex = await Assert.ThrowsAsync<BudgetExceededException>(() => agent.RunAsync("tres", session));

        Assert.Equal(1200, ex.Spent);
        Assert.Equal(2, model.Calls); // la tercera llamada nunca llega al modelo
        Assert.Equal(1200, guard.SpentBy(session));
    }

    [Fact]
    public async Task Budget_IsPerSession()
    {
        var model = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok"), tokensPerCall: 600);
        var guard = new BudgetGuard(model, budget: 1000);
        var agent = new ChatClientAgent(guard);
        AgentSession first = await agent.CreateSessionAsync();
        AgentSession second = await agent.CreateSessionAsync();

        await agent.RunAsync("uno", first);
        await agent.RunAsync("dos", first);
        await agent.RunAsync("uno", second); // otra sesión, otro contador

        Assert.Equal(1200, guard.SpentBy(first));
        Assert.Equal(600, guard.SpentBy(second));
    }

    [Fact]
    public async Task Streaming_CountsUsageContent()
    {
        var model = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok"), tokensPerCall: 700);
        var guard = new BudgetGuard(model, budget: 500);
        var agent = new ChatClientAgent(guard);
        AgentSession session = await agent.CreateSessionAsync();

        await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("uno", session))
        {
        }

        Assert.Equal(700, guard.SpentBy(session));
        await Assert.ThrowsAsync<BudgetExceededException>(async () =>
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("dos", session))
            {
            }
        });
    }
}
