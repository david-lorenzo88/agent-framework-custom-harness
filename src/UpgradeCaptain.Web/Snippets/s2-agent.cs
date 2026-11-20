//@source step-2:src/UpgradeCaptain.Console/Program.cs
//en: The same AsHarnessAgent as in step 1, plus three capabilities and the approval rules.
//es: El mismo AsHarnessAgent del step-1, más tres capacidades y las reglas de aprobación.
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = codeAct is null ? Prompt : Prompt + CodeActHint,
        Tools = codeAct is null
            ? [.. UpgradeTools.All, shell.AsAIFunction(requireApproval: true)] //! //en: run_shell ALWAYS asks //es: run_shell SIEMPRE pregunta
            : [shell.AsAIFunction(requireApproval: true)],
    },
    // …
    FileAccessStore = new FileSystemAgentFileStore(paths.RepoDir), //! //en: file_access_* confined to the repo //es: file_access_* confinado al repo
    AgentSkillsSource = new AgentFileSkillsSource( //!
        [paths.SkillsDir], //en: skills/ copied next to the executable //es: skills/ copiadas junto al ejecutable
        new DotnetScriptRunner(paths.RepoDir).RunAsync), //en: .cs scripts run with dotnet run script.cs (.NET 10) //es: los scripts .cs: dotnet run script.cs (.NET 10)
    ToolApprovalAgentOptions = new ToolApprovalAgentOptions
    {
        AutoApprovalRules =
        [
            FileAccessProvider.ReadOnlyToolsAutoApprovalRule, //! //en: reading files doesn't ask; writing does //es: leer ficheros no pregunta; escribir sí
            AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule, //! //en: loading skills doesn't ask; run_skill_script does //es: cargar skills no pregunta; run_skill_script sí
        ],
    },
    // …
});
