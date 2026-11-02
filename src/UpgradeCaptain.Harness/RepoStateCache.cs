namespace UpgradeCaptain.Harness;

/// <summary>
/// Última verificación del repo y cuántas veces seguidas se ha repetido el mismo fallo.
/// La escribe VerifiedCompletionEvaluator; la leen RepoStateProvider (para el modelo) y
/// CodeOwnerApprovalAgent (para la línea de estado que ve el usuario).
/// </summary>
public sealed class RepoStateCache
{
    /// <summary>Veces seguidas con el mismo fallo a partir de las cuales el agente se considera atascado.</summary>
    public const int StuckLimit = 3;

    private readonly Lock _gate = new();

    public VerificationResult? Last { get; private set; }

    /// <summary>Veces seguidas que la verificación ha dado exactamente el mismo conjunto de fallos (0 si verde).</summary>
    public int ConsecutiveSameFailures { get; private set; }

    /// <summary>El mismo conjunto de fallos <see cref="StuckLimit"/> veces seguidas.</summary>
    public bool IsStuck => this.ConsecutiveSameFailures >= StuckLimit;

    /// <summary>Se incrementa en cada Update: permite saber si hubo verificación durante una ejecución.</summary>
    public int Version { get; private set; }

    /// <summary>BudgetGuard cortó la ejecución (estado budget_exceeded del trabajo).</summary>
    public bool BudgetExceeded { get; private set; }

    public void MarkBudgetExceeded() => this.BudgetExceeded = true;

    public void Update(VerificationResult result)
    {
        lock (this._gate)
        {
            if (result.IsGreen)
            {
                this.ConsecutiveSameFailures = 0;
            }
            else if (this.Last is { IsGreen: false } previous && previous.Fingerprint == result.Fingerprint)
            {
                this.ConsecutiveSameFailures++;
            }
            else
            {
                this.ConsecutiveSameFailures = 1;
            }

            this.Last = result;
            this.Version++;
        }
    }
}
