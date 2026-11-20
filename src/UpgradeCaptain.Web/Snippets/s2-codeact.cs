//@source step-2:src/UpgradeCaptain.Console/Program.cs
//en: CodeAct (--surface codeact, slide only): the 5 tools move INSIDE execute_code.
//en: The model writes one Python block that loops over the tools and only sees the result.
//es: CodeAct (--surface codeact, solo diapositiva): las 5 tools pasan a vivir DENTRO de execute_code.
//es: El modelo escribe un único bloque de Python que recorre las tools y solo ve el resultado.
using LocalCodeActProvider? codeAct = cli.Surface == "codeact"
    ? new LocalCodeActProvider( //!
        Path.Combine(paths.ProjectRoot, ".venv", "bin", "python"), //en: Python on THIS laptop: not a sandbox //es: Python en ESTE portátil: no es un sandbox
        new LocalCodeActProviderOptions
        {
            Tools = UpgradeTools.All.OfType<AIFunction>(), //!
            ExecutionLimits = new ProcessExecutionLimits { TimeoutSeconds = 20 },
        })
    : null;
// …
//en: The model must SEE its Python error (exception and traceback) to fix it (D-32).
//es: El modelo tiene que VER el error de su Python (excepción y traceback) para corregirse (D-32).
if (codeAct is not null)
{
    agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true;
}
