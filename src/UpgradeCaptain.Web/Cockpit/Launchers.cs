using Microsoft.JSInterop;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Lo que ofrece el lanzador ⚡ del chat en la página actual: los prompts del guion y las acciones.</summary>
public sealed record LauncherSet(Bi Title, IReadOnlyList<PromptChip> Prompts, IReadOnlyList<DoItem> Actions, IReadOnlyList<DoItem> More);

/// <summary>
/// Los lanzadores de la cabina: qué hace cada acción, cómo se llama, cuándo no se puede usar y qué pestaña enseña
/// después (un «dotnet test» lanzado desde el chat abre la pestaña Realidad para ver el resultado).
/// </summary>
public static class Launchers
{
    /// <summary>Acciones generales, disponibles en cualquier página (debajo de las del paso).</summary>
    private static readonly DoItem[] General =
    [
        new("gen.reset-clean", new("Repo limpio (+ build)", "Clean repo (+ build)"), CockpitAction.ResetClean),
        new("gen.reset-fix", new("Repo after-mechanical-fix (+ build)", "Repo after-mechanical-fix (+ build)"), CockpitAction.ResetAfterFix),
        new("gen.build", new("dotnet build", "dotnet build"), CockpitAction.Build),
        new("gen.test", new("dotnet test", "dotnet test"), CockpitAction.Test),
        new("gen.gitlog", new("git log", "git log"), CockpitAction.GitLog),
    ];

    private static readonly DoItem[] OpeningActions =
    [
        new("open.stage", new("Preparar el escenario: repo limpio + build", "Prepare the stage: clean repo + build"), CockpitAction.ResetClean),
        new("open.azure", new("Comprobar Azure y calentar el modelo", "Check Azure and warm up the model"), CockpitAction.CheckAzure),
        new("open.feed", new("Empaquetar el feed de Teide.Geo", "Pack the Teide.Geo feed"), CockpitAction.PackFeed),
        new("open.aspire", new("Abrir Aspire", "Open Aspire"), CockpitAction.OpenAspire),
    ];

    /// <summary>El lanzador de la página: paso y parte, o la portada (step = 0) y el cierre (step = null).</summary>
    public static LauncherSet For(int? step, string? partId)
    {
        if (step is int number && StepCatalog.Find(number) is { } content)
        {
            StepPart part = content.Parts.FirstOrDefault(p => p.Id == partId) ?? content.Parts[0];
            DoItem[] actions = [.. part.Do.Where(d => d.Action != CockpitAction.None)];
            Bi title = content.Parts.Count > 1
                ? new($"{part.Id} · {part.Title.Es}", $"{part.Id} · {part.Title.En}")
                : new($"{number} · {content.Title.Es}", $"{number} · {content.Title.En}");
            return new LauncherSet(title, part.Prompts, actions, [.. General.Where(g => actions.All(a => a.Action != g.Action))]);
        }

        return step == 0
            ? new LauncherSet(new("Apertura", "Opening"), [], OpeningActions, [.. General.Where(g => g.Action != CockpitAction.ResetClean)])
            : new LauncherSet(new("Cierre", "Close"), [], [], General);
    }

    public static string Icon(CockpitAction action) => action switch
    {
        CockpitAction.StartStep1 or CockpitAction.StartStep2 or CockpitAction.StartStep3Pre or CockpitAction.StartStep3 => "▶",
        CockpitAction.ResetClean or CockpitAction.ResetAfterFix => "↺",
        CockpitAction.ModeExecute => "◐",
        CockpitAction.Build or CockpitAction.Test => "🧪",
        CockpitAction.GitLog => "⎇",
        CockpitAction.RevealEvaluator => "✨",
        CockpitAction.RevealWiring => "🔌",
        CockpitAction.OpenOwner => "👩‍💻",
        CockpitAction.OpenAspire => "📈",
        CockpitAction.HostedStatus or CockpitAction.HostedWatch => "☁",
        CockpitAction.PackFeed => "📦",
        CockpitAction.CheckAzure => "🔑",
        _ => "•",
    };

    public static bool Disabled(Cockpit cockpit, CockpitAction action) => action switch
    {
        CockpitAction.ModeExecute => cockpit.Agent.State != RunState.Idle,
        CockpitAction.Build or CockpitAction.Test or CockpitAction.GitLog or CockpitAction.PackFeed => cockpit.Reality.Running,
        CockpitAction.ResetClean or CockpitAction.ResetAfterFix => cockpit.Operation is not null || cockpit.Agent.State == RunState.Running,
        CockpitAction.StartStep1 or CockpitAction.StartStep2 or CockpitAction.StartStep3Pre or CockpitAction.StartStep3 =>
            cockpit.Agent.State is RunState.Running or RunState.Building,
        CockpitAction.CheckAzure => cockpit.Preflight.Running,
        _ => false,
    };

    /// <summary>Ejecuta una acción, marca su casilla del guion y enseña la pestaña donde se ve el resultado.</summary>
    public static async Task RunAsync(Cockpit cockpit, IJSRuntime js, DoItem item)
    {
        cockpit.Done.Add(item.Id);
        switch (item.Action)
        {
            case CockpitAction.StartStep1: await cockpit.StartAgentAsync(AgentStage.Step1); break;
            case CockpitAction.StartStep2: await cockpit.StartAgentAsync(AgentStage.Step2); break;
            case CockpitAction.StartStep3Pre: await cockpit.StartAgentAsync(AgentStage.Step3Pre); break;
            case CockpitAction.StartStep3: await cockpit.StartAgentAsync(AgentStage.Step3); break;
            case CockpitAction.ModeExecute: await cockpit.Agent.SetModeAsync("execute"); break;
            case CockpitAction.ResetClean:
                cockpit.ContentTab = "reality";
                await cockpit.ResetDemoAsync("clean");
                break;
            case CockpitAction.ResetAfterFix:
                cockpit.ContentTab = "reality";
                await cockpit.ResetDemoAsync("after-mechanical-fix");
                break;
            case CockpitAction.Build:
                cockpit.ContentTab = "reality";
                await cockpit.BuildRepoAsync();
                break;
            case CockpitAction.Test:
                cockpit.ContentTab = "reality";
                await cockpit.TestRepoAsync();
                break;
            case CockpitAction.GitLog:
                cockpit.ContentTab = "reality";
                await cockpit.GitLogAsync();
                break;
            case CockpitAction.PackFeed:
                cockpit.ContentTab = "reality";
                await cockpit.PackFeedAsync();
                break;
            case CockpitAction.CheckAzure:
                cockpit.ContentTab = "preflight";
                await cockpit.Preflight.RunRemoteAsync();
                break;
            case CockpitAction.RevealEvaluator:
                cockpit.EvaluatorRevealed = true;
                cockpit.ContentTab = "evaluator";
                break;
            case CockpitAction.RevealWiring:
                cockpit.EvaluatorRevealed = true;
                cockpit.WiringRevealed = true;
                cockpit.ContentTab = "evaluator";
                break;
            case CockpitAction.OpenOwner:
                cockpit.ContentTab = "approvals";
                await js.InvokeVoidAsync("uc.open", "/maria", "maria");
                break;
            case CockpitAction.OpenAspire: await js.InvokeVoidAsync("uc.open", "http://localhost:18888", "aspire"); break;
            case CockpitAction.HostedStatus:
                cockpit.ContentTab = "hosted";
                await cockpit.Hosted.StatusAsync();
                break;
            case CockpitAction.HostedWatch:
                cockpit.ContentTab = "hosted";
                cockpit.Hosted.ToggleWatch();
                break;
        }

        cockpit.NotifyNow();
    }
}
