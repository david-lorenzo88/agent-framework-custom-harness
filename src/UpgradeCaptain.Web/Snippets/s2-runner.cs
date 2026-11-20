//@source src/UpgradeCaptain.Core/DotnetScriptRunner.cs
//en: Skill scripts are .NET 10 single-file apps: dotnet run script.cs -- arg1 arg2 …
//en: Clean environment like the shell, working directory = the demo repo, 60 s maximum.
//es: Los scripts de las skills son apps de un solo fichero de .NET 10: dotnet run script.cs -- arg1 arg2 …
//es: Entorno limpio como el shell, directorio de trabajo = el repo de la demo, 60 s como máximo.
public async Task<object?> RunAsync( //!
    AgentFileSkill skill,
    AgentFileSkillScript script,
    JsonElement? arguments,
    IServiceProvider? serviceProvider,
    CancellationToken cancellationToken)
{
    // …
    startInfo.ArgumentList.Add("run"); //!
    startInfo.ArgumentList.Add(script.FullPath);
    startInfo.ArgumentList.Add("--");
    // …
}
