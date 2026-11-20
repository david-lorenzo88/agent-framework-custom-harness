using Microsoft.AspNetCore.Components;
using UpgradeCaptain.Core;
using UpgradeCaptain.Web.Cockpit;

namespace UpgradeCaptain.Web.Components.Shared;

/// <summary>Base de los componentes de la cabina: se repintan cuando cambia el estado compartido.</summary>
public abstract class CockpitBase : ComponentBase, IDisposable
{
    [Inject]
    protected Cockpit.Cockpit Cockpit { get; set; } = default!;

    protected DemoLanguage Lang => this.Cockpit.Language;

    protected string T(string es, string en) => this.Cockpit.T(es, en);

    protected string T(Bi text) => text[this.Cockpit.Language];

    protected MarkupString Md(Bi text) => Inline.Render(text[this.Cockpit.Language]);

    protected MarkupString Md(string text) => Inline.Render(text);

    protected override void OnInitialized() => this.Cockpit.Changed += this.OnCockpitChanged;

    public virtual void Dispose()
    {
        this.Cockpit.Changed -= this.OnCockpitChanged;
        GC.SuppressFinalize(this);
    }

    private void OnCockpitChanged() => _ = this.InvokeAsync(this.StateHasChanged);
}
