//@source step-2:src/UpgradeCaptain.Console/Program.cs
//en: A local shell confined to the demo repo. It is NOT a sandbox: the deny list is a UX filter,
//en: the real boundary is the human approval (and, in step 4, Foundry's per-session VM).
//es: Shell local confinado al repo de la demo. NO es un sandbox: la lista de denegación es un filtro
//es: de UX, la frontera real es la aprobación humana (y, en el step-4, la VM por sesión de Foundry).
await using var shell = new LocalShellExecutor(new LocalShellExecutorOptions //!
{
    WorkingDirectory = paths.RepoDir,
    ConfineWorkingDirectory = true, //en: every command starts again in the repo //es: cada comando vuelve a empezar en el repo
    CleanEnvironment = true, //! //en: inherits no FOUNDRY_* or AZURE_*: only PATH, HOME, USER… //es: no hereda FOUNDRY_* ni AZURE_*: solo PATH, HOME, USER…
    Environment = new Dictionary<string, string?> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1" },
    Timeout = TimeSpan.FromSeconds(120), //en: a cold dotnet build/test can take a while //es: dotnet build/test en frío puede tardar
    Policy = new ShellPolicy(denyList: //!
    [
        @"\brm\s+-rf\b",
        @"\bsudo\b",
        @"\bgit\s+push\b",
        @"\bdotnet\s+nuget\s+push\b",
    ]),
});
