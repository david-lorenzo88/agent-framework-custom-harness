// ─────────────────────────────────────────────────────────────────────────────
// step-1 · Harness
//
// Mismo IChatClient, pero ahora AsHarnessAgent monta el arnés completo:
//   · bucle de function calling con nuestras 5 tools (UpgradeTools.All)
//   · historial persistido tras CADA llamada al modelo (sesión exportable)
//   · compactación de contexto (--context tiny para verla)
//   · todos, modos, memoria en ficheros y telemetría OpenTelemetry
// ─────────────────────────────────────────────────────────────────────────────

#pragma warning disable OPENAI001 // La Responses API es experimental en el SDK de OpenAI.
#pragma warning disable MAAI001  // Compactación, FileMemoryStore y el cliente sin "store" son experimentales en Agent Framework.

using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Harness.Shared.Console;
using Harness.Shared.Console.OpenAI;
using Harness.Shared.Console.ToolFormatters;
using Microsoft.Agents.AI;
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

// 2. El HarnessAgent: function calling, historial por llamada, compactación, todos, modos, memoria y OTel.
AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
{
    ChatOptions = new ChatOptions
    {
        Instructions = Prompt,
        Tools = UpgradeTools.All,
    },
    DisableWebSearch = true,                                             // nada de web: solo el repo
    AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" }, // planificar llega en el step-2
    FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(paths.ProjectRoot, "agent-file-memory")),
    OpenTelemetrySourceName = Telemetry.SourceName,                      // "UpgradeCaptain": una sola traza
});

// 3. Consola reactiva de los samples del harness: streaming, uso de contexto, /todos, /mode, /session-export.
await HarnessConsole.RunAgentAsync(
    agent,
    userPrompt: "Prueba: «Haz inventario de dónde usamos Teide.Geo». Comandos: /todos /mode /session-export /session-import /exit",
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
