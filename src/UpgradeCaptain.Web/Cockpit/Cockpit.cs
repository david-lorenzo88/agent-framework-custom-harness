using UpgradeCaptain.Core;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>El reloj de la charla (50 min): se arranca al empezar y se compara con el guion.</summary>
public sealed class TalkClock
{
    private DateTimeOffset? _startedAt;
    private TimeSpan _accumulated;

    public bool Running => this._startedAt is not null;

    public TimeSpan Elapsed => this._accumulated + (this._startedAt is { } start ? DateTimeOffset.Now - start : TimeSpan.Zero);

    public void Toggle()
    {
        if (this._startedAt is { } start)
        {
            this._accumulated += DateTimeOffset.Now - start;
            this._startedAt = null;
        }
        else
        {
            this._startedAt = DateTimeOffset.Now;
        }
    }

    public void Reset()
    {
        this._startedAt = null;
        this._accumulated = TimeSpan.Zero;
    }
}

/// <summary>
/// El estado de la cabina: uno por proceso y compartido por todas las ventanas (la del ponente y la de María).
/// Lo que cambia en segundo plano (streaming, procesos) marca la cabina como "sucia"; un temporizador avisa a
/// las páginas como mucho cada 70 ms, para que el streaming no repinte por cada token.
/// </summary>
public sealed class Cockpit : IAsyncDisposable
{
    private readonly PeriodicTimer _pump = new(TimeSpan.FromMilliseconds(70));
    private readonly Task _pumpTask;
    private int _dirty;

    public Cockpit()
    {
        this.Language = Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_LANG") is { Length: > 0 } lang ? DemoLanguages.Parse(lang) : DemoLanguage.En;
        ApplyCliLanguage(this.Language);
        this.Agent = new AgentRunner(this.MarkDirty);
        this.Reality = new Terminal("canarias-tours", this.MarkDirty);
        this.Hosted = new HostedClient(this.MarkDirty);
        this.Preflight = new Preflight(this, this.MarkDirty);
        this._pumpTask = this.PumpAsync();
    }

    /// <summary>Algo ha cambiado: las páginas se repintan (en su propio hilo, con InvokeAsync).</summary>
    public event Action? Changed;

    public DemoLanguage Language { get; private set; }

    public AgentRunner Agent { get; }

    /// <summary>El terminal del repo de la demo: reset, dotnet build/test y git, sin agente.</summary>
    public Terminal Reality { get; }

    public HostedClient Hosted { get; }

    public Preflight Preflight { get; }

    public TalkClock Clock { get; } = new();

    /// <summary>Casillas marcadas de las listas «Haz» de cada paso.</summary>
    public HashSet<string> Done { get; } = [];

    /// <summary>step-3b: el evaluador y el cableado ya se han enseñado (el «live coding» de la web).</summary>
    public bool EvaluatorRevealed { get; set; }

    public bool WiringRevealed { get; set; }

    /// <summary>Tamaño de letra para el proyector (1 = normal).</summary>
    public double FontScale { get; set; } = 1.0;

    public string? Operation { get; private set; }

    /// <summary>El borrador del cuadro de mensaje: los prompts del lanzador ⚡ lo rellenan.</summary>
    public string Draft { get; set; } = string.Empty;

    /// <summary>
    /// La pestaña visible del contenido (script, code, reality, approvals…). La cambian también los lanzadores del
    /// chat: «dotnet test» enseña Realidad, «Revelar» enseña el evaluador.
    /// </summary>
    public string ContentTab { get; set; } = "script";

    /// <summary>Ancho del chat: "narrow" (más sitio para el código), "normal" o "wide" (para leer la conversación).</summary>
    public string ChatWidth { get; set; } = "normal";

    private readonly Dictionary<int, string> _parts = [];

    /// <summary>La parte elegida de un paso (el step-3 tiene 3a, 3b y 3c); null = la primera.</summary>
    public string? PartFor(int step) => this._parts.GetValueOrDefault(step);

    public void SetPart(int step, string part) => this._parts[step] = part;

    public DemoPaths Paths => DemoPaths.Current;

    public string T(string es, string en) => this.Language.Pick(es, en);

    public string T(Bi text) => text[this.Language];

    /// <summary>Cambia el idioma de toda la demo. Si hay un agente montado, se vuelve a montar en el idioma nuevo.</summary>
    public async Task SetLanguageAsync(DemoLanguage language)
    {
        if (language == this.Language)
        {
            return;
        }

        this.Language = language;
        ApplyCliLanguage(language);
        this.NotifyNow();
        if (this.Agent.Stage is { } stage)
        {
            await this.Agent.StartAsync(stage, language, this.Agent.TinyContext);
        }

        await this.Preflight.RunLocalAsync();
        this.NotifyNow();
    }

    public Task StartAgentAsync(AgentStage stage, bool tinyContext = false) => this.Agent.StartAsync(stage, this.Language, tinyContext);

    /// <summary>
    /// scripts/reset-demo.sh en el idioma de la cabina y, después, dotnet build en el repo: el build refresca obj/
    /// (el agente compila con --no-restore y un obj/ viejo da un NU1101 falso). Si había un agente, se vuelve a montar:
    /// el reset borra state/ y la memoria.
    /// </summary>
    public async Task ResetDemoAsync(string state)
    {
        this.Operation = this.T($"Preparando el repo ({state})…", $"Preparing the repo ({state})…");
        AgentStage? stage = this.Agent.Stage;
        await this.Agent.StopAsync();
        this.Reality.Clear();
        this.NotifyNow();
        try
        {
            int exit = await this.Reality.RunAsync(this.T("reset", "reset"), this.Paths.ProjectRoot, this.CliEnvironment,
                ("bash", ["scripts/reset-demo.sh", "--state", state, "--lang", this.Language.Code()]));
            if (exit == 0)
            {
                await this.BuildRepoAsync();
            }

            this.EvaluatorRevealed = this.EvaluatorRevealed && state == "after-mechanical-fix";
            if (stage is { } active)
            {
                await this.StartAgentAsync(active, this.Agent.TinyContext);
            }

            await this.Preflight.RunLocalAsync();
        }
        finally
        {
            this.Operation = null;
            this.NotifyNow();
        }
    }

    public Task BuildRepoAsync() =>
        this.Reality.RunAsync("dotnet build", this.Paths.RepoDir, this.CliEnvironment, ("dotnet", ["build", "--nologo", "-v", "q", "-clp:ErrorsOnly"]));

    public Task TestRepoAsync() =>
        this.Reality.RunAsync("dotnet test", this.Paths.RepoDir, this.CliEnvironment, ("dotnet", ["test", "--nologo"]));

    public Task GitLogAsync() =>
        this.Reality.RunAsync("git log", this.Paths.RepoDir, this.CliEnvironment,
            ("git", ["--no-pager", "log", "--format=%h %<(16,trunc)%an %s", "-8"]), ("git", ["status", "--short", "--branch"]));

    public Task GitDiffAsync() =>
        this.Reality.RunAsync("git diff", this.Paths.RepoDir, this.CliEnvironment, ("git", ["--no-pager", "diff", "--stat", "HEAD"]), ("git", ["--no-pager", "diff", "HEAD"]));

    public async Task PackFeedAsync()
    {
        await this.Reality.RunAsync("pack-feed", this.Paths.ProjectRoot, this.CliEnvironment, ("bash", ["scripts/pack-feed.sh"]));
        await this.Preflight.RunLocalAsync();
    }

    /// <summary>La auditoría del harness a medida (cadena de hashes de audit.jsonl).</summary>
    public AuditVerification? VerifyAudit() => this.Agent.Services?.Audit.Verify();

    public void MarkDirty() => Interlocked.Exchange(ref this._dirty, 1);

    public void NotifyNow()
    {
        Interlocked.Exchange(ref this._dirty, 0);
        this.Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        this._pump.Dispose();
        await this.Agent.DisposeAsync();
    }

    /// <summary>
    /// dotnet y MSBuild en el idioma de la charla, y en inglés también el formato de los números (LC_ALL), para que
    /// los fallos de los tests digan 30663.76 y no 30.663,76. Lo heredan los procesos del harness (RepoVerifier).
    /// </summary>
    private IReadOnlyDictionary<string, string> CliEnvironment => this.Language == DemoLanguage.En
        ? new Dictionary<string, string> { ["DOTNET_CLI_UI_LANGUAGE"] = "en", ["LC_ALL"] = "en_US.UTF-8" }
        : new Dictionary<string, string> { ["DOTNET_CLI_UI_LANGUAGE"] = "es", ["LC_ALL"] = OriginalLcAll ?? string.Empty };

    private static readonly string? OriginalLcAll = Environment.GetEnvironmentVariable("LC_ALL");

    private static void ApplyCliLanguage(DemoLanguage language)
    {
        Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", language.Code());
        Environment.SetEnvironmentVariable("LC_ALL", language == DemoLanguage.En ? "en_US.UTF-8" : OriginalLcAll);
    }

    private async Task PumpAsync()
    {
        while (await this._pump.WaitForNextTickAsync())
        {
            if (Interlocked.Exchange(ref this._dirty, 0) == 1)
            {
                try
                {
                    this.Changed?.Invoke();
                }
                catch
                {
                    // una página cerrada no debe parar el resto
                }
            }
        }
    }
}
