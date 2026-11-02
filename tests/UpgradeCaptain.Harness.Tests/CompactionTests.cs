using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// D-07 (docs/verification-log.md): la compactación integrada del HarnessAgent 1.24.0 pierde mensajes nuevos;
/// CompactingChatClient aplica la misma estrategia sin ese fallo.
/// </summary>
public class CompactionTests
{
    [Fact]
    public async Task BuiltInCompaction_1_24_0_DropsTheSecondTurn_KnownBug()
    {
        // Si este test empieza a fallar tras actualizar Agent Framework, el fallo está corregido upstream:
        // se puede volver a HarnessAgentOptions.MaxContextWindowTokens/MaxOutputTokens y retirar CompactingChatClient.
        var model = new RecordingChatClient();
        HarnessAgent agent = model.AsHarnessAgent(Options(builtInCompaction: true));
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("pregunta uno", session);
        await agent.RunAsync("pregunta dos", session);

        Assert.DoesNotContain("pregunta dos", model.LastUserText);
    }

    [Fact]
    public async Task CompactingChatClient_KeepsTheSecondTurn()
    {
        var model = new RecordingChatClient();
        HarnessAgent agent = model.WithContextWindowCompaction(1_050_000, 128_000).AsHarnessAgent(Options(builtInCompaction: false));
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("pregunta uno", session);
        await agent.RunAsync("pregunta dos", session);

        Assert.Contains("pregunta dos", model.LastUserText);
    }

    [Fact]
    public async Task CompactingChatClient_CollapsesOldToolResults_WhenTheWindowIsTiny()
    {
        var model = new RecordingChatClient();
        IChatClient client = model.WithContextWindowCompaction(maxContextWindowTokens: 3_000, maxOutputTokens: 1_000);

        // 10 ficheros "leídos" (resultados grandes de tools) y una pregunta final.
        var messages = new List<ChatMessage> { new(ChatRole.User, "Haz inventario de Teide.Geo") };
        for (int i = 0; i < 10; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"c{i}", "read_source", new Dictionary<string, object?> { ["path"] = $"F{i}.cs" })]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"c{i}", new string('x', 2_000))]));
        }

        messages.Add(new ChatMessage(ChatRole.User, "¿Y ahora?"));
        await client.GetResponseAsync(messages);

        int bytesSent = model.LastMessages.Sum(m => m.Contents.OfType<FunctionResultContent>().Sum(r => r.Result?.ToString()?.Length ?? 0));
        Assert.True(bytesSent < 10 * 2_000, $"Se enviaron {bytesSent} bytes de resultados de tools: no se compactó.");
        Assert.Equal("¿Y ahora?", model.LastMessages[^1].Text);
    }

    private static HarnessAgentOptions Options(bool builtInCompaction) => new()
    {
        DisableWebSearch = true,
        DisableFileMemory = true,
        DisableAgentSkillsProvider = true,
        AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
        MaxContextWindowTokens = builtInCompaction ? 1_050_000 : null,
        MaxOutputTokens = builtInCompaction ? 128_000 : null,
    };

    private sealed class RecordingChatClient : IChatClient
    {
        public List<ChatMessage> LastMessages { get; private set; } = [];

        public string LastUserText => string.Join(" / ", this.LastMessages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            this.LastMessages = [.. messages];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "respuesta")));
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
