//@source src/UpgradeCaptain.Core/UpgradeCaptainOptions.cs
//en: Same factory, different options: unattended, allow-listed shell, requester @dependabot.
//es: La misma factoría, otras opciones: desatendido, shell con lista de permitidos, pide @dependabot.
public static UpgradeCaptainOptions ForHosted(string home)
{
    // …
    return new UpgradeCaptainOptions
    {
        // …
        ShellAllowList = //!
        [
            @"^\s*dotnet(\s|$)",
            @"^\s*git\s+(status|diff|log|add|commit)(\s|$)",
        ],
        CodeAct = CodeActBackend.Local,
        TrustSkillScripts = true,
        AutoApproveUnprotected = true, //! //en: only protected paths wait for their owner //es: solo lo protegido espera a su propietario
        EnableCustomHarness = true,
        MemoryRoot = Path.Combine(home, "memory"),
        StateRoot = Path.Combine(home, "state"), //en: survives scale to zero //es: sobrevive a escalar a cero
        Requester = "@dependabot",
        Attended = false, //! //en: nobody in front: protected edits are parked //es: nadie delante: lo protegido se aparca
    };
}
