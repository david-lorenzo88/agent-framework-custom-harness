//@source src/UpgradeCaptain.Harness/RepoStateProvider.cs
//en: The repo's truth on EVERY run: resolved version, last verification and pending approvals.
//en: It is injected as instructions on each run, so it survives compaction.
//es: La verdad del repo en CADA ejecución: versión resuelta, última verificación y aprobaciones pendientes.
//es: Se inyecta como instrucciones en cada ejecución, así que sobrevive a la compactación.
public sealed class RepoStateProvider(RepoStateCache cache, ApprovalStore approvals, string repoDir) : AIContextProvider //!
{
    // …
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        // …
        return new ValueTask<AIContext>(new AIContext { Instructions = text.Length <= MaxChars ? text : text[..(MaxChars - 1)] + "…" }); //!
    }
}
