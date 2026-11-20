using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// D-37 (docs/verification-log.md): si la conexión se corta justo después de que el modelo pida una tool, la
/// sesión se queda con una llamada sin resultado y la Responses API rechaza todas las peticiones siguientes.
/// ToolCallRepairChatClient le pone un resultado de "interrumpido" y «Sigue» vuelve a funcionar.
/// </summary>
public class ToolCallRepairTests
{
    [Fact]
    public void Repair_CompleteHistory_IsReturnedAsIs()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "hola"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "ping")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "pong")]),
        ];

        Assert.Same(messages, ToolCallRepairChatClient.Repair(messages));
    }

    [Fact]
    public void Repair_OrphanCall_GetsAnInterruptedResultRightAfterIt_WithoutTouchingTheOriginal()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "hola"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "ping"), new FunctionCallContent("c2", "ping")]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", "pong")]),
            new(ChatRole.User, "Sigue"),
        ];

        List<ChatMessage> repaired = [.. ToolCallRepairChatClient.Repair(messages)];

        Assert.Equal(4, messages.Count);
        Assert.Equal(5, repaired.Count);
        FunctionResultContent added = Assert.Single(repaired[2].Contents.OfType<FunctionResultContent>());
        Assert.Equal("c1", added.CallId);
        Assert.Equal(ToolCallRepairChatClient.InterruptedResult, added.Result);
        Assert.Equal("Sigue", repaired[^1].Text);
    }

    [Fact]
    public async Task HarnessAgent_1_24_0_KeepsAnOrphanCallAfterANetworkCut_KnownBehavior()
    {
        // Si este test empieza a fallar tras actualizar Agent Framework, el harness ya no guarda la llamada
        // huérfana y ToolCallRepairChatClient sobra.
        var model = new FlakyModel();
        HarnessAgent agent = model.AsHarnessAgent(Options());
        AgentSession session = await agent.CreateSessionAsync();

        await Assert.ThrowsAsync<IOException>(() => agent.RunAsync("haz ping", session));
        InvalidOperationException rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("Sigue", session));

        Assert.Contains("No tool output found for function call c1", rejected.Message);
    }

    [Fact]
    public async Task WithRepair_SigueWorksAfterANetworkCut()
    {
        var model = new FlakyModel();
        HarnessAgent agent = model.WithInterruptedToolCallRepair().AsHarnessAgent(Options());
        AgentSession session = await agent.CreateSessionAsync();

        await Assert.ThrowsAsync<IOException>(() => agent.RunAsync("haz ping", session));
        AgentResponse response = await agent.RunAsync("Sigue", session);

        Assert.Equal("ok", response.Text);
        Assert.Contains(model.LastMessages.SelectMany(m => m.Contents).OfType<FunctionResultContent>(),
            r => r.CallId == "c1" && Equals(r.Result, ToolCallRepairChatClient.InterruptedResult));
    }

    private static HarnessAgentOptions Options() => new()
    {
        ChatOptions = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "pong", "ping")] },
        DisableWebSearch = true,
        DisableFileMemory = true,
        DisableAgentSkillsProvider = true,
        AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
    };

    /// <summary>
    /// 1ª llamada: pide la tool ping. 2ª (la que lleva el resultado): se corta la conexión.
    /// Después: valida como la Responses API (cada llamada necesita su resultado) y responde "ok".
    /// </summary>
    private sealed class FlakyModel : IChatClient
    {
        private int _calls;

        public List<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            this.LastMessages = [.. messages];
            switch (++this._calls)
            {
                case 1:
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "ping")])));
                case 2:
                    throw new IOException("Unable to read data from the transport connection: Connection reset by peer.");
            }

            var answered = this.LastMessages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToHashSet();
            if (this.LastMessages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault(c => !answered.Contains(c.CallId)) is { } orphan)
            {
                throw new InvalidOperationException($"HTTP 400 (invalid_request_error) Parameter: input. No tool output found for function call {orphan.CallId}.");
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await this.GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
