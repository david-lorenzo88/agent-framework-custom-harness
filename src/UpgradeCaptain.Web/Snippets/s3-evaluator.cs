//@source src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs
using Microsoft.Agents.AI;

namespace UpgradeCaptain.Harness;

//en: "Done" means the repo builds and the tests pass, whatever the model says.
//en: It goes AFTER TodoCompletionLoopEvaluator: it acts once no todos are left open.
//es: "Terminado" significa que el repo compila y los tests pasan, diga lo que diga el modelo.
//es: Va DESPUÉS de TodoCompletionLoopEvaluator: actúa cuando ya no quedan todos abiertos.
public sealed class VerifiedCompletionEvaluator(RepoVerifier verifier, RepoStateCache cache, GitCheckpoints checkpoints) : LoopEvaluator //!
{
    public override async ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        //en: 1. Only in "execute" mode: while planning there's nothing to verify.
        //es: 1. Solo en modo "execute": mientras planifica no hay nada que verificar.
        AgentModeProvider? modes = context.Agent.GetService<AgentModeProvider>();
        if (modes is null || await modes.GetModeAsync(context.Session, cancellationToken) != "execute")
        {
            return LoopEvaluation.Stop();
        }

        //en: 2. The HARNESS builds and runs the tests, not the model.
        //es: 2. Compila y pasa los tests el HARNESS, no el modelo.
        VerificationResult result = await verifier.VerifyAsync(cancellationToken); //!
        cache.Update(result);

        //en: 3. Green: git checkpoint and end of the loop.
        //es: 3. Verde: checkpoint en git y fin del bucle.
        if (result.IsGreen)
        {
            await checkpoints.CommitGreenAsync($"chore(deps): green verified by Upgrade Captain ({result.Summary})", cancellationToken);
            return LoopEvaluation.Stop();
        }

        //en: 4. Stuck: the same failure 3 times in a row → stop and go back to the last green.
        //es: 4. Atascado: el mismo fallo 3 veces seguidas → paramos y volvemos al último verde.
        if (cache.IsStuck)
        {
            await checkpoints.RestoreLastGreenAsync("stuck", cancellationToken);
            return LoopEvaluation.Stop();
        }

        //en: 5. Red: the concrete failures (file, line, test) go back to the model as feedback.
        //es: 5. Rojo: los fallos concretos (fichero, línea, test) vuelven al modelo como feedback.
        return LoopEvaluation.Continue(result.ToFeedback()); //!
    }
}
