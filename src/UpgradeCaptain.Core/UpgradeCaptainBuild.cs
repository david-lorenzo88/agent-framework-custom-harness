using Microsoft.Agents.AI;
using Microsoft.Agents.AI.LocalCodeAct;
using Microsoft.Agents.AI.Tools.Shell;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Core;

/// <summary>
/// Lo que devuelve la factoría: el agente listo para usar y lo que hay que liberar al terminar
/// (el shell persistente y CodeAct). También expone las piezas para la consola y el Hosted Agent.
/// </summary>
public sealed class UpgradeCaptainBuild : IAsyncDisposable
{
    private readonly LocalShellExecutor? _shell;
    private readonly LocalCodeActProvider? _codeAct;

    internal UpgradeCaptainBuild(
        AIAgent agent,
        HarnessAgentOptions harnessOptions,
        UpgradeCaptainOptions options,
        HarnessServices? services,
        BudgetGuard? budget,
        LocalShellExecutor? shell,
        LocalCodeActProvider? codeAct)
    {
        this.Agent = agent;
        this.HarnessOptions = harnessOptions;
        this.Options = options;
        this.Services = services;
        this.Budget = budget;
        this._shell = shell;
        this._codeAct = codeAct;
    }

    /// <summary>El agente: CodeOwnerApprovalAgent → HarnessAgent (o solo HarnessAgent sin el harness a medida).</summary>
    public AIAgent Agent { get; }

    /// <summary>Las opciones con las que se creó el HarnessAgent (el test de equivalencia con el step-5 las compara).</summary>
    public HarnessAgentOptions HarnessOptions { get; }

    public UpgradeCaptainOptions Options { get; }

    /// <summary>Servicios de las piezas a medida (null si EnableCustomHarness = false).</summary>
    public HarnessServices? Services { get; }

    public BudgetGuard? Budget { get; }

    /// <summary>El decorador de aprobaciones por propietario (null sin el harness a medida).</summary>
    public CodeOwnerApprovalAgent? Approvals => this.Agent as CodeOwnerApprovalAgent;

    public async ValueTask DisposeAsync()
    {
        this._codeAct?.Dispose();
        if (this._shell is not null)
        {
            await this._shell.DisposeAsync().ConfigureAwait(false);
        }
    }
}
