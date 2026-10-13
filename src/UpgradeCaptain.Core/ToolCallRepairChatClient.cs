using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Core;

/// <summary>
/// Repara la historia antes de enviarla al modelo: a cada llamada a tool que se quedó sin resultado le añade
/// uno que explica qué pasó.
///
/// ¿Cuándo pasa? Si la conexión se corta (connection reset, timeout de red) justo después de que el modelo
/// pida una tool, la sesión guarda la llamada pero no su resultado. Con "store" desactivado se envía la
/// historia completa en cada petición, y la Responses API rechaza TODAS las siguientes con
/// HTTP 400 "No tool output found for function call". Escribir «Sigue» en la consola no serviría de nada.
/// Con esta reparación, «Sigue» funciona y el modelo decide si repite la tool. Ver docs/verification-log.md (D-37).
/// </summary>
public sealed class ToolCallRepairChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <summary>El resultado que recibe el modelo en lugar del que se perdió.</summary>
    public const string InterruptedResult =
        "Error: la ejecución se interrumpió (error de red) antes de tener resultado. Si lo necesitas, vuelve a llamar a la tool.";

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Repair(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Repair(messages), options, cancellationToken);

    /// <summary>
    /// Los mensajes tal cual si todas las llamadas tienen resultado. Si no, una copia con un mensaje de tool
    /// justo detrás de cada mensaje que tiene llamadas huérfanas. Nunca modifica la lista original.
    /// </summary>
    public static IEnumerable<ChatMessage> Repair(IEnumerable<ChatMessage> messages)
    {
        IReadOnlyList<ChatMessage> list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        var answered = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => r.CallId)
            .ToHashSet(StringComparer.Ordinal);

        // Ojo: no se filtra por InformationalOnly. FunctionInvokingChatClient marca así las llamadas que ya ejecutó,
        // y la huérfana es justo una de esas (se ejecutó, pero su resultado no llegó a guardarse).
        static bool Orphan(AIContent c, HashSet<string> answered) =>
            c is FunctionCallContent call && !answered.Contains(call.CallId);

        if (!list.Any(m => m.Contents.Any(c => Orphan(c, answered))))
        {
            return list;
        }

        var repaired = new List<ChatMessage>(list.Count + 1);
        foreach (ChatMessage message in list)
        {
            repaired.Add(message);
            List<AIContent> results = message.Contents
                .Where(c => Orphan(c, answered))
                .Select(c => (AIContent)new FunctionResultContent(((FunctionCallContent)c).CallId, InterruptedResult))
                .ToList();
            if (results.Count > 0)
            {
                repaired.Add(new ChatMessage(ChatRole.Tool, results));
            }
        }

        return repaired;
    }
}

public static class ToolCallRepairChatClientExtensions
{
    /// <summary>Tras un corte de red, la sesión sigue siendo válida: ver <see cref="ToolCallRepairChatClient"/>.</summary>
    public static IChatClient WithInterruptedToolCallRepair(this IChatClient client) => new ToolCallRepairChatClient(client);
}
