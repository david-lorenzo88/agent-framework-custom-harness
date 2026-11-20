//@source step-1:src/UpgradeCaptain.Console/Program.cs
//en: 1. A Foundry chat client with server-side storage OFF: the history is kept by the harness, not by the service.
//es: 1. Cliente de Foundry con "store" desactivado: el historial lo guarda el harness, no el servicio.
//en:    With "store" on, client-side compaction would change nothing the model sees.
//es:    Con "store" activado, la compactación del lado cliente no cambiaría lo que ve el modelo.
IChatClient chatClient =
    new AIProjectClient(
        new Uri(endpoint),
        new DefaultAzureCredential(),
        new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
    .GetProjectOpenAIClient()
    .GetProjectResponsesClientForModel(model)
    .AsIChatClientWithStoredOutputDisabled(model) //!
    .WithInterruptedToolCallRepair() //en: after a network cut the session is still valid (D-37) //es: tras un corte de red, la sesión sigue siendo válida (D-37)
    .WithContextWindowCompaction(maxContextWindowTokens, maxOutputTokens); //! //en: compaction on EVERY model call (D-07) //es: compactación en CADA llamada al modelo (D-07)
