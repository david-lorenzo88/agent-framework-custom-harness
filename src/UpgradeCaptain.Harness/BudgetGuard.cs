using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Harness;

/// <summary>Se ha superado el presupuesto de tokens de la sesión.</summary>
public sealed class BudgetExceededException(long spent, long budget)
    : Exception($"Presupuesto de tokens agotado: {spent:N0} de {budget:N0}.")
{
    public long Spent { get; } = spent;

    public long Budget { get; } = budget;
}

/// <summary>
/// Tope de coste (el harness integrado solo tiene topes de iteraciones). Envuelve el IChatClient "hoja",
/// antes de AsHarnessAgent, así que ve CADA llamada al modelo del bucle de function calling.
/// Suma Usage.TotalTokenCount (y UsageContent en streaming) por sesión (AIAgent.CurrentRunContext) y,
/// si la sesión ya ha gastado más de <see cref="Budget"/>, la siguiente llamada lanza BudgetExceededException.
/// </summary>
public sealed class BudgetGuard : DelegatingChatClient
{
    // Contador por sesión: la sesión es la clave, y al desaparecer la sesión desaparece su contador.
    private readonly ConditionalWeakTable<object, Spent> _spent = new();
    private readonly object _noSession = new();

    public BudgetGuard(IChatClient innerClient, long budget)
        : base(innerClient)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
        this.Budget = budget;
    }

    public long Budget { get; }

    /// <summary>Tokens gastados por una sesión (o fuera de sesión si es null).</summary>
    public long SpentBy(AgentSession? session) => this._spent.GetOrCreateValue(session ?? this._noSession).Total;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Spent spent = this.CheckBudget();
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Count(spent, response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Spent spent = this.CheckBudget();
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (UsageContent usage in update.Contents.OfType<UsageContent>())
            {
                Count(spent, usage.Details);
            }

            yield return update;
        }
    }

    private Spent CheckBudget()
    {
        Spent spent = this._spent.GetOrCreateValue(AIAgent.CurrentRunContext?.Session ?? this._noSession);
        if (spent.Total > this.Budget)
        {
            throw new BudgetExceededException(spent.Total, this.Budget);
        }

        return spent;
    }

    private static void Count(Spent spent, UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        long total = usage.TotalTokenCount ?? (usage.InputTokenCount ?? 0) + (usage.OutputTokenCount ?? 0);
        Interlocked.Add(ref spent.Total, total);
        HarnessDiagnostics.Tokens.Add(usage.InputTokenCount ?? 0, new KeyValuePair<string, object?>("token.type", "input"));
        HarnessDiagnostics.Tokens.Add(usage.OutputTokenCount ?? 0, new KeyValuePair<string, object?>("token.type", "output"));
    }

    private sealed class Spent
    {
        public long Total;
    }
}
