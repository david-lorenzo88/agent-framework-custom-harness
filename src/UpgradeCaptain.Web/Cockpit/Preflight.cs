using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Web.Cockpit;

public enum CheckState
{
    Pending,
    Running,
    Ok,
    Warn,
    Fail,
}

public sealed class Check(string id, Bi title, bool remote = false)
{
    public string Id { get; } = id;

    public Bi Title { get; } = title;

    /// <summary>Llama a Azure (token o modelo): solo se ejecuta al pulsar el botón.</summary>
    public bool Remote { get; } = remote;

    public CheckState State { get; set; }

    public string Detail { get; set; } = string.Empty;

    public Bi? Hint { get; set; }
}

/// <summary>Procesos cortos (git, docker) cuya salida hace falta leer, no enseñar.</summary>
public static class Proc
{
    public static async Task<(int Exit, string Output)> RunAsync(string file, string workingDirectory, params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo(file) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(startInfo)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}

/// <summary>Lo que hay que mirar antes de la charla (la antigua lista de T-60), en una página.</summary>
public sealed class Preflight(Cockpit cockpit, Action notify)
{
    public Check Foundry { get; } = new("foundry", new("Proyecto de Foundry", "Foundry project"));

    public Check SignIn { get; } = new("signin", new("Sesión de Azure (az login)", "Azure sign-in (az login)"), remote: true);

    public Check Model { get; } = new("model", new("Modelo (calentar)", "Model (warm-up)"), remote: true);

    public Check Repo { get; } = new("repo", new("Repo de la demo", "Demo repo"));

    public Check Feed { get; } = new("feed", new("Feed local de Teide.Geo", "Local Teide.Geo feed"));

    public Check Telemetry { get; } = new("telemetry", new("Trazas (Aspire)", "Traces (Aspire)"));

    public Check Hosted { get; } = new("hosted", new("Hosted Agent (step-4)", "Hosted Agent (step 4)"));

    public IReadOnlyList<Check> All => [this.Foundry, this.SignIn, this.Model, this.Repo, this.Feed, this.Telemetry, this.Hosted];

    public bool Running { get; private set; }

    /// <summary>Las comprobaciones locales (sin red). Las remotas, con <see cref="RunRemoteAsync"/>.</summary>
    public async Task RunLocalAsync()
    {
        this.Running = true;
        notify();
        try
        {
            this.CheckFoundry();
            await this.CheckRepoAsync();
            this.CheckFeed();
            await this.CheckTelemetryAsync();
            this.CheckHosted();
        }
        finally
        {
            this.Running = false;
            notify();
        }
    }

    public async Task RunRemoteAsync()
    {
        this.Running = true;
        notify();
        try
        {
            await this.CheckSignInAsync();
            await this.CheckModelAsync();
        }
        finally
        {
            this.Running = false;
            notify();
        }
    }

    private void CheckFoundry()
    {
        string? endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT");
        string model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "gpt-5.4";
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            Set(this.Foundry, CheckState.Fail, "FOUNDRY_PROJECT_ENDPOINT", new("Ponlo en .env (ver .env.example).", "Add it to .env (see .env.example)."));
            return;
        }

        Set(this.Foundry, CheckState.Ok, $"{uri.Host} · {model}");
    }

    private async Task CheckSignInAsync()
    {
        Set(this.SignIn, CheckState.Running, "…");
        notify();
        try
        {
            AccessToken token = await new DefaultAzureCredential().GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]));
            (string? who, string? tenant) = ReadClaims(token.Token);
            Set(this.SignIn, CheckState.Ok, $"{who ?? "?"} · tenant {tenant?[..8]}…",
                new("Comprueba que es el tenant de gpt-5.4 (D-26).", "Check it's the tenant that has gpt-5.4 (D-26)."));
        }
        catch (Exception ex)
        {
            Set(this.SignIn, CheckState.Fail, First(ex.Message), new("az login con la cuenta de la demo.", "az login with the demo account."));
        }
    }

    private async Task CheckModelAsync()
    {
        Set(this.Model, CheckState.Running, "…");
        notify();
        try
        {
            UpgradeCaptainOptions options = UpgradeCaptainOptions.ForLocal(DemoPaths.Current.RepoDir);
            IChatClient client = UpgradeCaptainFactory.CreateFoundryChatClient(options);
            var clock = Stopwatch.StartNew();
            ChatResponse response = await client.GetResponseAsync("Reply with the single word: ready", new ChatOptions { MaxOutputTokens = 16 });
            Set(this.Model, CheckState.Ok, $"{options.Model} · {clock.ElapsedMilliseconds:N0} ms · «{response.Text.Trim()}»");
        }
        catch (Exception ex)
        {
            Set(this.Model, CheckState.Fail, First(ex.Message));
        }
    }

    private async Task CheckRepoAsync()
    {
        string repo = DemoPaths.Current.RepoDir;
        if (!System.IO.Directory.Exists(Path.Combine(repo, ".git")))
        {
            Set(this.Repo, CheckState.Fail, cockpit.T("no existe", "missing"), new("Pulsa «Preparar el escenario».", "Press \"Prepare the stage\"."));
            return;
        }

        string branch = (await Proc.RunAsync("git", repo, "branch", "--show-current")).Output;
        string head = (await Proc.RunAsync("git", repo, "log", "-1", "--format=%s")).Output;
        string dirty = (await Proc.RunAsync("git", repo, "status", "--porcelain")).Output;
        string readme = Path.Combine(repo, "README.md");
        DemoLanguage repoLanguage = File.Exists(readme) && File.ReadAllText(readme).Contains("Booking backend", StringComparison.Ordinal) ? DemoLanguage.En : DemoLanguage.Es;
        string state = branch == "upgrade/teide-geo-2.0" ? "after-mechanical-fix" : "clean";
        string detail = $"{branch} · {state} · {repoLanguage.Code()} · «{head}»{(dirty.Length > 0 ? cockpit.T(" · con cambios", " · modified") : string.Empty)}";
        if (repoLanguage != cockpit.Language)
        {
            Set(this.Repo, CheckState.Warn, detail, new("El repo está en otro idioma: vuelve a prepararlo.", "The repo is in the other language: prepare it again."));
            return;
        }

        Set(this.Repo, dirty.Length > 0 ? CheckState.Warn : CheckState.Ok, detail);
    }

    private void CheckFeed()
    {
        string feed = Path.Combine(DemoPaths.Current.ProjectRoot, "demo", "feed");
        bool packages = File.Exists(Path.Combine(feed, "Teide.Geo.1.8.0.nupkg")) && File.Exists(Path.Combine(feed, "Teide.Geo.2.0.0.nupkg"));
        bool english = File.Exists(Path.Combine(feed, "notes", "2.0.0.en.md"));
        if (!packages)
        {
            Set(this.Feed, CheckState.Fail, cockpit.T("faltan los paquetes", "packages missing"), new("Pulsa «Empaquetar feed».", "Press \"Pack feed\"."));
        }
        else if (!english)
        {
            Set(this.Feed, CheckState.Warn, cockpit.T("faltan las notas en inglés", "English release notes missing"), new("Pulsa «Empaquetar feed».", "Press \"Pack feed\"."));
        }
        else
        {
            Set(this.Feed, CheckState.Ok, "1.8.0 · 2.0.0 · notes es/en");
        }
    }

    private async Task CheckTelemetryAsync()
    {
        string? otlp = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        bool dashboard = await IsOpenAsync(18888);
        if (string.IsNullOrWhiteSpace(otlp))
        {
            Set(this.Telemetry, CheckState.Warn, cockpit.T("sin OTEL_EXPORTER_OTLP_ENDPOINT", "no OTEL_EXPORTER_OTLP_ENDPOINT"),
                new("scripts/aspire.sh y relanza con scripts/cockpit.sh (lo exporta solo).", "Run scripts/aspire.sh and relaunch with scripts/cockpit.sh (it exports it)."));
            return;
        }

        Set(this.Telemetry, dashboard ? CheckState.Ok : CheckState.Warn, dashboard ? $"{otlp} · http://localhost:18888" : cockpit.T($"{otlp} · el dashboard no responde", $"{otlp} · dashboard not reachable"));
    }

    private void CheckHosted()
    {
        HostedClient hosted = cockpit.Hosted;
        if (hosted.Endpoint is null)
        {
            Set(this.Hosted, CheckState.Warn, cockpit.T("sin endpoint", "no endpoint"));
            return;
        }

        string? session = hosted.SessionId;
        Set(this.Hosted, session is null ? CheckState.Warn : CheckState.Ok,
            session is null ? cockpit.T("sin sesión: lanza el trabajo ≥ 30 min antes", "no session: trigger the job ≥ 30 min before") : $"{hosted.JobId} · {session[..8]}…");
    }

    private static async Task<bool> IsOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Quién y qué tenant, del propio token (sin enviarlo a ninguna parte).</summary>
    private static (string? Who, string? Tenant) ReadClaims(string jwt)
    {
        try
        {
            string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            string? Claim(string name) => doc.RootElement.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
            return (Claim("upn") ?? Claim("unique_name") ?? Claim("email") ?? Claim("appid"), Claim("tid"));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string First(string message) => message.Split('\n')[0].Trim();

    private static void Set(Check check, CheckState state, string detail, Bi? hint = null)
    {
        check.State = state;
        check.Detail = detail;
        check.Hint = hint;
    }
}
