// ─────────────────────────────────────────────────────────────────────────────
// step-3 · Tu harness
//
// La construcción del agente sale de aquí y pasa a UpgradeCaptainFactory (UpgradeCaptain.Core),
// que enchufa al HarnessAgent las piezas a medida de UpgradeCaptain.Harness:
//   · VerifiedCompletionEvaluator  "terminado" = compila y pasan los tests (lo comprueba el harness)
//   · CodeOwnerApprovalAgent       aprueba el propietario del código (CODEOWNERS), no quien pide
//   · GitCheckpoints               commit en verde, vuelta atrás si algo va mal
//   · RepoStateProvider            estado real del repo en cada ejecución (sobrevive a la compactación)
//   · BudgetGuard                  tope de tokens por sesión
//
//   upgrade-captain [--context normal|tiny] [--surface direct|codeact] [--as <handle>] [--state <estado>]
//   upgrade-captain run --job demo/jobs/upgrade-teide-geo.json
//   upgrade-captain approvals list | approve <id> --as <handle> | reject <id> --as <handle> --reason <texto> | audit
// ─────────────────────────────────────────────────────────────────────────────

using Harness.Shared.Console;
using Harness.Shared.Console.OpenAI;
using Harness.Shared.Console.ToolFormatters;
using Microsoft.Agents.AI;
using UpgradeCaptain.Console;
using UpgradeCaptain.Core;
using UpgradeCaptain.Harness;

DotEnv.Load();
CliArgs cli = CliArgs.Parse(args);
using IDisposable? telemetry = Telemetry.StartIfConfigured();

if (cli.State is not null)
{
    DemoReset.Run(cli.State); // scripts/reset-demo.sh --state ...
}

// 1. Opciones: todo activado en local; las flags eligen superficie, contexto y quién pide el trabajo.
UpgradeCaptainOptions options = UpgradeCaptainOptions.ForLocal(DemoPaths.Current.RepoDir);
options.CodeAct = cli.Surface == "codeact" ? CodeActBackend.Local : CodeActBackend.None;
(options.MaxContextWindowTokens, options.MaxOutputTokens) = cli.ContextWindow;
options.Requester = cli.As;

// 2. La factoría devuelve el agente y lo que hay que liberar al salir (shell, CodeAct).
await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(options);
AIAgent agent = build.Agent;

// 3. Subcomandos: aprobaciones (la terminal de María) y ejecución desatendida.
switch (cli.Rest.ToArray())
{
    case ["approvals", .. var rest]:
        return await ApprovalsCli.RunAsync(build, rest, actor: cli.As);
    case ["run", "--job", var jobFile]:
        return await ApprovalsCli.RunJobAsync(build, jobFile);
    case []:
        break;
    default:
        Console.Error.WriteLine($"Subcomando desconocido: {string.Join(' ', cli.Rest)}");
        return 1;
}

if (cli.Prompt is not null)
{
    return await Headless.RunOnceAsync(agent, cli.Prompt);
}

// 4. Consola atendida: el trabajo lo pide --as (por defecto @david-lorenzo).
string jobId = $"console-{DateTime.Now:yyyyMMdd-HHmmss}";
await HarnessConsole.RunAgentAsync(
    agent,
    userPrompt: $"Pides como {cli.As}. Prueba: «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo». /approvals /todos /mode /exit",
    new HarnessConsoleOptions
    {
        Observers =
        [
            new OpenAIResponsesErrorObserver(),
            .. HarnessConsoleOptions.BuildObserversWithPlanning(
                agent,
                planModeName: "plan",
                executionModeName: "execute",
                maxContextWindowTokens: options.MaxContextWindowTokens,
                maxOutputTokens: options.MaxOutputTokens,
                toolFormatters: ToolCallFormatter.BuildDefaultToolFormatters()),
        ],
        CommandHandlers = [.. HarnessConsoleOptions.BuildDefaultCommandHandlers(agent), new ApprovalsCommandHandler(build.Services!)],
        SessionFactory = async a =>
        {
            AgentSession session = await a.CreateSessionAsync();
            JobContext.Set(session, new JobContext(jobId, cli.As, Attended: true));
            return session;
        },
    });
return 0;
