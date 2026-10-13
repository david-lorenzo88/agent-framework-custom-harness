#pragma warning disable MAAI001 // Las estrategias de compactación son experimentales en Agent Framework.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Core;

/// <summary>
/// Compactación de contexto con la estrategia INTEGRADA de Agent Framework (ContextWindowCompactionStrategy),
/// aplicada en cada llamada al modelo sobre un índice nuevo.
///
/// ¿Por qué no HarnessAgentOptions.MaxContextWindowTokens/MaxOutputTokens? En 1.24.0 el CompactionProvider
/// del harness guarda un índice entre llamadas y CompactionMessageIndex.Update deja de añadir mensajes
/// nuevos cuando el último mensaje es igual al último procesado. TodoProvider añade siempre el mismo
/// "### Current todo list" al final, así que se pierden: la segunda pregunta del usuario, el feedback
/// del LoopAgent y el resultado de una tool tras aprobarla. Ver docs/verification-log.md (D-07).
/// Aquí el índice se reconstruye en cada llamada (CompactionProvider.CompactAsync): sin estado, sin el fallo.
/// </summary>
public sealed class CompactingChatClient(IChatClient innerClient, CompactionStrategy strategy) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        await base.GetResponseAsync(await this.CompactAsync(messages, cancellationToken).ConfigureAwait(false), options, cancellationToken).ConfigureAwait(false);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IEnumerable<ChatMessage> compacted = await this.CompactAsync(messages, cancellationToken).ConfigureAwait(false);
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(compacted, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async Task<IEnumerable<ChatMessage>> CompactAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        List<ChatMessage> before = [.. messages];
        List<ChatMessage> after = [.. await CompactionProvider.CompactAsync(strategy, before, cancellationToken: cancellationToken).ConfigureAwait(false)];

        // Visible en Aspire: cuántos mensajes y bytes ve el modelo antes y después de compactar.
        using Activity? span = Telemetry.ActivitySource.StartActivity("upgrade_captain.compaction");
        span?.SetTag("messages.before", before.Count);
        span?.SetTag("messages.after", after.Count);
        span?.SetTag("compacted", !ReferenceEquals(before, after) && (after.Count != before.Count || !after.SequenceEqual(before)));
        return after;
    }
}

public static class CompactingChatClientExtensions
{
    /// <summary>Compacta el contexto con ContextWindowCompactionStrategy(maxContextWindowTokens, maxOutputTokens).</summary>
    public static IChatClient WithContextWindowCompaction(this IChatClient client, int maxContextWindowTokens, int maxOutputTokens) =>
        new CompactingChatClient(client, new ContextWindowCompactionStrategy(maxContextWindowTokens, maxOutputTokens));
}
