//@source step-1:src/UpgradeCaptain.Console/Program.cs
//en: 2. One call brings the whole harness: function-calling loop, history persisted after every
//en:    model call, compaction, todos, modes, file memory and OpenTelemetry.
//es: 2. Una llamada trae el harness completo: bucle de function calling, historial persistido tras
//es:    cada llamada al modelo, compactación, todos, modos, memoria en ficheros y OpenTelemetry.
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions //!
{
    ChatOptions = new ChatOptions
    {
        Instructions = Prompt,
        Tools = UpgradeTools.All, //en: our 5 read-only tools //es: nuestras 5 tools de solo lectura
    },
    DisableWebSearch = true, //en: no web: only the repo //es: nada de web: solo el repo
    AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "plan" }, //! //en: plan first and ask //es: primero planifica y pregunta
    LoopEvaluators =
    [
        //en: Only in "execute" mode: while todos are still open, invoke the agent again.
        //es: Solo en modo "execute": mientras queden todos abiertos, vuelve a invocar al agente.
        new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions //!
        {
            Modes = ["execute"],
            // … FeedbackMessageTemplate
        }),
    ],
    LoopAgentOptions = new LoopAgentOptions { MaxIterations = 10 }, //! //en: the loop's safety cap //es: tope de seguridad del bucle
    FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(paths.ProjectRoot, "agent-file-memory")),
    OpenTelemetrySourceName = Telemetry.SourceName, //en: one trace: "UpgradeCaptain" //es: una sola traza: "UpgradeCaptain"
});
