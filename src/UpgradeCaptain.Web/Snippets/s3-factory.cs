//@source src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs
//en: The custom harness is NOT a new loop: it's the HarnessAgent with four pieces plugged
//en: into its documented extension points. The same factory serves the console and the Hosted Agent.
//es: El harness a medida NO es un bucle nuevo: es el HarnessAgent con cuatro piezas enchufadas en sus
//es: puntos de extensión documentados. La misma factoría sirve a la consola y al Hosted Agent.
public static UpgradeCaptainBuild Create(UpgradeCaptainOptions options)
{
    IChatClient chatClient = options.ChatClient ?? CreateFoundryChatClient(options);
    // …
    chatClient = chatClient
        .WithInterruptedToolCallRepair()
        .WithContextWindowCompaction(options.MaxContextWindowTokens, options.MaxOutputTokens);
    BudgetGuard? budget = services is null ? null : new BudgetGuard(chatClient, options.TokenBudget); //! //en: ① token cap: inner DelegatingChatClient //es: ① tope de tokens: DelegatingChatClient interior
    // …
    HarnessAgentOptions harnessOptions = BuildHarnessOptions(options, shell, codeAct, services); //en: ② RepoStateProvider + ③ VerifiedCompletionEvaluator go in here //es: ② RepoStateProvider + ③ VerifiedCompletionEvaluator van aquí
    AIAgent agent = (budget ?? chatClient).AsHarnessAgent(harnessOptions);
    // …
    if (services is not null)
    {
        agent = new CodeOwnerApprovalAgent(agent, services, new CodeOwnerApprovalAgentOptions //! //en: ④ the code owner approves: outermost DelegatingAIAgent //es: ④ aprueba el propietario: DelegatingAIAgent más externo
        {
            DefaultRequester = options.Requester,
            DefaultAttended = options.Attended,
        });
    }

    return new UpgradeCaptainBuild(agent, harnessOptions, options, services, budget, shell, codeAct);
}
