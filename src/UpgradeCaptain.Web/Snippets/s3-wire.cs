//@source src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs
//en: In UpgradeCaptainFactory.BuildHarnessOptions: one line plugs it into LoopEvaluators.
//es: En UpgradeCaptainFactory.BuildHarnessOptions: una línea lo enchufa en LoopEvaluators.
List<LoopEvaluator> evaluators =
[
    new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions
    {
        Modes = ["execute"],
        FeedbackMessageTemplate = Prompts.TodoFeedbackFor(options.Language),
    }),
];
if (services is not null && options.EnableVerifiedCompletion)
{
    //en: "Done" = it builds and the tests pass. It goes after the todos evaluator.
    //es: "Terminado" = compila y pasan los tests. Va después del evaluador de todos.
    evaluators.Add(new VerifiedCompletionEvaluator(services.Verifier, services.Cache, services.Checkpoints)); //!
}
