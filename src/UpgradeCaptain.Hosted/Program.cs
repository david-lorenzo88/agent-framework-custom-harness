// ─────────────────────────────────────────────────────────────────────────────
// step-7 · Hosted Agent
//
// La MISMA factoría del step-6 (UpgradeCaptainFactory.Create) detrás de un host Invocations.
// En Foundry cada sesión es una VM con su $HOME, que se conserva cuando la sesión escala a cero:
// por eso todo el estado (sesión aparcada, aprobaciones, checkpoints, auditoría) vive en $HOME/state.
//
// Webhook: POST /invocations   ·   Salud: GET /readiness   ·   Puerto 8088
// ─────────────────────────────────────────────────────────────────────────────

using Azure.AI.AgentServer.Core;
using Azure.AI.AgentServer.Invocations;
using OpenTelemetry.Metrics;
using UpgradeCaptain.Core;
using UpgradeCaptain.Hosted;

bool inContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

// $HOME de la sesión. En local, UPGRADE_CAPTAIN_HOME evita escribir en tu $HOME real (scripts/hosted.sh --local).
string home = Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_HOME")
    ?? (inContainer
        ? Environment.GetEnvironmentVariable("HOME") ?? "/home/app"
        : throw new InvalidOperationException("Fuera de un contenedor define UPGRADE_CAPTAIN_HOME (usa scripts/hosted.sh --local)."));
string demoRoot = Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_DEMO_ROOT") ?? "/opt/demo";

// 1. Opciones del Hosted Agent: shell y ficheros confinados a $HOME/work/canarias-tours, CodeAct local,
//    desatendido (lo protegido se aparca para su propietario) y estado en $HOME/state.
UpgradeCaptainOptions options = UpgradeCaptainOptions.ForHosted(home);

void AddUpgradeCaptain(IServiceCollection services)
{
    services.AddSingleton(options);
    services.AddSingleton(sp => new UpgradeJobs(options, demoRoot, sp.GetRequiredService<ILogger<UpgradeJobs>>()));
    services.AddSingleton(new IdentityMap(Path.Combine(demoRoot, "identities.json")));
    services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(Telemetry.SourceName));
}

if (inContainer)
{
    // 2. En Foundry: AgentHost (SDK de Foundry). Trae /readiness, el puerto 8088 y la telemetría: con la
    //    APPLICATIONINSIGHTS_CONNECTION_STRING que inyecta Foundry exporta a Application Insights sin código de
    //    exportador. Con WebApplication a secas solo se veían las trazas de la plataforma (D-43).
    AgentHostBuilder host = AgentHost.CreateBuilder(args);
    AddUpgradeCaptain(host.Services);
    host.AddInvocations<UpgradeInvocationHandler>();
    host.ConfigureTracing(tracing => tracing.AddSource(Telemetry.SourceName, "Experimental.Microsoft.Agents.AI"));
    host.Build().Run();
}
else
{
    // 2. En local (scripts/hosted.sh serve --local): WebApplication escuchando SOLO en localhost (ASPNETCORE_URLS).
    //    AgentHost escucha en todas las interfaces, y este host local no tiene autenticación (la identidad se
    //    simula con una cabecera): en la wifi de un congreso, eso no.
    WebApplicationBuilder web = WebApplication.CreateBuilder(args);
    AddUpgradeCaptain(web.Services);
    web.Services.AddInvocationsServer();
    web.Services.AddScoped<InvocationHandler, UpgradeInvocationHandler>();
    WebApplication app = web.Build();
    app.MapGet("/readiness", () => Results.Ok()); // el SDK de Invocations no lo mapea por sí solo
    app.MapInvocationsServer();
    app.Run();
}
