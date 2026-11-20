// ─────────────────────────────────────────────────────────────────────────────
// UpgradeCaptain.Web · la cabina de la demo
//
// Una web local (Blazor Server) para dar la charla desde el navegador: cada paso con su explicación, el código
// que se enseña y un panel para ejecutarlo en directo, en inglés o en español. Los agentes salen de
// UpgradeCaptainFactory, como en la consola (que sigue ahí como plan B) y en el Hosted Agent.
//
//   scripts/cockpit.sh            → http://localhost:5180 (solo localhost: shell y ficheros del agente están detrás)
// ─────────────────────────────────────────────────────────────────────────────

using UpgradeCaptain.Core;
using UpgradeCaptain.Web.Components;
using UpgradeCaptain.Web.Cockpit;

DotEnv.Load();
using IDisposable? telemetry = Telemetry.StartIfConfigured("upgrade-captain-cockpit"); // OTLP solo si OTEL_EXPORTER_OTLP_ENDPOINT existe

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Solo localhost: desde esta web se aprueban comandos de shell y escrituras en el repo de la demo.
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_COCKPIT_URL") ?? "http://localhost:5180");

builder.Services.AddSingleton<Cockpit>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Comprobaciones locales al arrancar (sin red): la portada las enseña ya hechas.
_ = app.Services.GetRequiredService<Cockpit>().Preflight.RunLocalAsync();

app.Run();
