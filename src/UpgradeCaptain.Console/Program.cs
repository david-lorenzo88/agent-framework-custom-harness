// ─────────────────────────────────────────────────────────────────────────────
// step-4 · CodeAct
//
// Dos superficies para las mismas 5 tools (--surface direct|codeact):
//   · direct:  el modelo llama a cada tool y recibe cada resultado completo en su contexto
//   · codeact: el modelo escribe UN programa Python que llama a las tools con
//              `await call_tool(...)` y solo devuelve el resumen (execute_code)
// LocalCodeActProvider ejecuta Python en local: NO es un sandbox.
// ─────────────────────────────────────────────────────────────────────────────

#pragma warning disable OPENAI001 // La Responses API es experimental en el SDK de OpenAI.
#pragma warning disable MAAI001  // Compactación, FileMemoryStore, el cliente sin "store" y el bucle son experimentales en Agent Framework.

using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Harness.Shared.Console;
using Harness.Shared.Console.OpenAI;
using Harness.Shared.Console.ToolFormatters;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.LocalCodeAct;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Console;
using UpgradeCaptain.Core;
using static UpgradeCaptain.Core.Prompts;

DotEnv.Load();
CliArgs cli = CliArgs.Parse(args);
using IDisposable? telemetry = Telemetry.StartIfConfigured(); // OTLP solo si OTEL_EXPORTER_OTLP_ENDPOINT existe

string endpoint = DotEnv.Require("FOUNDRY_PROJECT_ENDPOINT");
string model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4";
DemoPaths paths = DemoPaths.Current;
var (maxContextWindowTokens, maxOutputTokens) = cli.ContextWindow;

// 1. IChatClient con "store" desactivado: el historial lo guarda el harness, no el servicio.
//    Si el servicio guardara la conversación, la compactación del lado cliente no cambiaría lo que ve el modelo.
//    La compactación (ContextWindowCompactionStrategy, integrada) se aplica en cada llamada con CompactingChatClient:
//    la de HarnessAgentOptions.MaxContextWindowTokens pierde mensajes en 1.24.0 (docs/verification-log.md, D-07).
IChatClient chatClient =
    new AIProjectClient(
        new Uri(endpoint),
        new DefaultAzureCredential(),
        new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
    .GetProjectOpenAIClient()
    .GetProjectResponsesClientForModel(model)
    .AsIChatClientWithStoredOutputDisabled(model)
    .WithInterruptedToolCallRepair()                                  // tras un corte de red, la sesión sigue siendo válida (D-37)
    .WithContextWindowCompaction(maxContextWindowTokens, maxOutputTokens); // compactación integrada, en cada llamada al modelo

// 2. Shell local confinado al repo de la demo. NO es un sandbox: la lista de denegación es un filtro
//    de UX, la frontera real es la aprobación humana (y, en el step-7, la VM por sesión de Foundry).
await using var shell = new LocalShellExecutor(new LocalShellExecutorOptions
{
    WorkingDirectory = paths.RepoDir,
    ConfineWorkingDirectory = true,          // cada comando vuelve a empezar en el repo
    CleanEnvironment = true,                 // no hereda variables del proceso (ni FOUNDRY_*, ni AZURE_*): solo PATH, HOME, USER...
    Environment = new Dictionary<string, string?> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1" },
    Timeout = TimeSpan.FromSeconds(120),     // dotnet build/test en frío puede tardar
    Policy = new ShellPolicy(denyList:
    [
        @"\brm\s+-rf\b",
        @"\bsudo\b",
        @"\bgit\s+push\b",
        @"\bdotnet\s+nuget\s+push\b",
    ]),
});

// 3. CodeAct (solo con --surface codeact): las 5 tools pasan a vivir DENTRO de execute_code.
//    Python dedicado en .venv/ (scripts/setup-venv.sh), con su ruta explícita.
using LocalCodeActProvider? codeAct = cli.Surface == "codeact"
    ? new LocalCodeActProvider(
        Path.Combine(paths.ProjectRoot, ".venv", "bin", "python"),
        new LocalCodeActProviderOptions
        {
            Tools = UpgradeTools.All.OfType<AIFunction>(),
            ExecutionLimits = new ProcessExecutionLimits { TimeoutSeconds = 20 },
        })
    : null;

// 4. El HarnessAgent: function calling, historial por llamada, compactación, todos, modos, memoria y OTel.
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = codeAct is null ? Prompt : Prompt + CodeActHint,
        Tools = codeAct is null
            ? [.. UpgradeTools.All, shell.AsAIFunction(requireApproval: true)] // direct: el modelo ve las 5 tools
            : [shell.AsAIFunction(requireApproval: true)],                   // codeact: las ve execute_code
    },
    AIContextProviders = codeAct is null
        ? [new ShellEnvironmentProvider(shell)]                               // qué shell, SO y herramientas hay
        : [new ShellEnvironmentProvider(shell), codeAct],                     // + la tool execute_code
    FileAccessStore = new FileSystemAgentFileStore(paths.RepoDir),      // file_access_* confinado al repo
    ToolApprovalAgentOptions = new ToolApprovalAgentOptions
    {
        AutoApprovalRules = [FileAccessProvider.ReadOnlyToolsAutoApprovalRule], // leer no pregunta; escribir sí
    },
    DisableWebSearch = true,                                             // nada de web: solo el repo
    AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "plan" },    // primero planifica y pregunta
    LoopEvaluators =
    [
        // Solo en modo "execute": mientras queden todos abiertos, vuelve a invocar al agente.
        new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions
        {
            Modes = ["execute"],
            FeedbackMessageTemplate =
                "Aún quedan tareas abiertas. Sigue trabajando y marca cada una como completada al terminarla. Pendientes:\n"
                + TodoCompletionLoopEvaluator.RemainingTodosPlaceholder,
        }),
    ],
    LoopAgentOptions = new LoopAgentOptions { MaxIterations = 10 },     // tope de seguridad del bucle
    FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(paths.ProjectRoot, "agent-file-memory")),
    OpenTelemetrySourceName = Telemetry.SourceName,                      // "UpgradeCaptain": una sola traza
});

// Con CodeAct el modelo tiene que VER el error de su Python (excepción y traceback) para corregirse.
// Por defecto FunctionInvokingChatClient lo oculta tras "Error: Function failed." (D-32).
if (codeAct is not null)
{
    agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true;
}

// 5. Sin consola (--prompt): una ejecución y métricas, para scripts/measure-surfaces.sh.
if (cli.Prompt is not null)
{
    return await Headless.RunOnceAsync(agent, cli.Prompt);
}

// 6. Consola reactiva de los samples del harness: streaming, uso de contexto, /todos, /mode, /session-export.
await HarnessConsole.RunAgentAsync(
    agent,
    userPrompt: $"Superficie {cli.Surface}. Prueba: «Haz inventario de las llamadas a Teide.Geo que rompen en 2.0». /exit para salir",
    new HarnessConsoleOptions
    {
        Observers =
        [
            new OpenAIResponsesErrorObserver(),
            .. HarnessConsoleOptions.BuildObserversWithPlanning(
                agent,
                planModeName: "plan",
                executionModeName: "execute",
                maxContextWindowTokens: maxContextWindowTokens,
                maxOutputTokens: maxOutputTokens,
                toolFormatters: ToolCallFormatter.BuildDefaultToolFormatters()),
        ],
        CommandHandlers = HarnessConsoleOptions.BuildDefaultCommandHandlers(agent),
    });
return 0;
