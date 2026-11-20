//@source src/UpgradeCaptain.Harness/BudgetGuard.cs
//en: A cost cap (the built-in harness only caps iterations). It wraps the "leaf" IChatClient,
//en: so it sees EVERY model call of the function-calling loop.
//es: Tope de coste (el harness integrado solo tiene topes de iteraciones). Envuelve el IChatClient "hoja",
//es: así que ve CADA llamada al modelo del bucle de function calling.
public sealed class BudgetGuard : DelegatingChatClient //!
{
    // …
    private Spent CheckBudget()
    {
        Spent spent = this._spent.GetOrCreateValue(AIAgent.CurrentRunContext?.Session ?? this._noSession); //! //en: tokens counted per session //es: tokens contados por sesión
        if (spent.Total > this.Budget)
        {
            throw new BudgetExceededException(spent.Total, this.Budget);
        }

        return spent;
    }
}
