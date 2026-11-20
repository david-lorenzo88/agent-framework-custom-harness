using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness.Tests;

public class VerifiedCompletionEvaluatorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public async Task Green_CommitsCheckpoint_AndStops()
    {
        var (evaluator, verifier, cache, checkpoints) = this.Create(Results.Green());

        LoopEvaluation evaluation = await evaluator.EvaluateAsync(await ContextAsync("execute"));

        Assert.False(evaluation.ShouldReinvoke);
        Assert.Equal(1, verifier.Calls);
        Assert.Single(checkpoints.Commits);
        Assert.Empty(checkpoints.Restores);
        Assert.True(cache.Last!.IsGreen);
    }

    [Fact]
    public async Task Red_Continues_WithActionableFeedback()
    {
        var (evaluator, _, _, checkpoints) = this.Create(Results.FareTestsRed());

        LoopEvaluation evaluation = await evaluator.EvaluateAsync(await ContextAsync("execute"));

        Assert.True(evaluation.ShouldReinvoke);
        Assert.Contains("FareTests.Quote_LosCristianosToTeide_Is33_66Eur", evaluation.Feedback);
        Assert.Contains("tests/Canarias.Tours.Tests/FareTests.cs:21", evaluation.Feedback);
        Assert.Contains("30663.76", evaluation.Feedback);
        Assert.True(evaluation.Feedback!.Length <= 2000);
        Assert.Empty(checkpoints.Commits);
    }

    [Fact]
    public async Task SameFailureThreeTimes_StopsAndRestoresLastGreen()
    {
        var (evaluator, verifier, cache, checkpoints) = this.Create(Results.FareTestsRed());
        LoopContext context = await ContextAsync("execute");

        Assert.True((await evaluator.EvaluateAsync(context)).ShouldReinvoke);   // 1
        Assert.True((await evaluator.EvaluateAsync(context)).ShouldReinvoke);   // 2
        LoopEvaluation third = await evaluator.EvaluateAsync(context);           // 3 → atascado

        Assert.False(third.ShouldReinvoke);
        Assert.Equal(3, verifier.Calls);
        Assert.Equal(3, cache.ConsecutiveSameFailures);
        Assert.Equal(["stuck"], checkpoints.Restores);
    }

    [Fact]
    public async Task DifferentFailures_DoNotCountAsStuck()
    {
        var (evaluator, _, cache, checkpoints) = this.Create(Results.BuildRed(), Results.FareTestsRed(), Results.BuildRed());
        LoopContext context = await ContextAsync("execute");

        for (int i = 0; i < 3; i++)
        {
            Assert.True((await evaluator.EvaluateAsync(context)).ShouldReinvoke);
        }

        Assert.Equal(1, cache.ConsecutiveSameFailures);
        Assert.Empty(checkpoints.Restores);
    }

    [Fact]
    public async Task PlanMode_DoesNothing()
    {
        var (evaluator, verifier, _, checkpoints) = this.Create(Results.FareTestsRed());

        LoopEvaluation evaluation = await evaluator.EvaluateAsync(await ContextAsync("plan"));

        Assert.False(evaluation.ShouldReinvoke);
        Assert.Equal(0, verifier.Calls);
        Assert.Empty(checkpoints.Commits);
    }

    private (VerifiedCompletionEvaluator, FakeVerifier, RepoStateCache, FakeCheckpoints) Create(params VerificationResult[] results)
    {
        var verifier = new FakeVerifier(results);
        var cache = new RepoStateCache();
        var checkpoints = new FakeCheckpoints(this._temp.Path);
        return (new VerifiedCompletionEvaluator(verifier, cache, checkpoints), verifier, cache, checkpoints);
    }

    /// <summary>Un LoopContext real: ChatClientAgent con AgentModeProvider en el modo pedido.</summary>
    private static async Task<LoopContext> ContextAsync(string mode)
    {
        var modes = new AgentModeProvider(new AgentModeProviderOptions { DefaultMode = mode });
        var agent = new ChatClientAgent(
            new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "hecho")),
            new ChatClientAgentOptions { AIContextProviders = [modes] });
        AgentSession session = await agent.CreateSessionAsync();
        return new LoopContext(agent, session, [new ChatMessage(ChatRole.User, "termina")], new AgentResponse(new ChatMessage(ChatRole.Assistant, "hecho")));
    }
}
