#pragma warning disable OPENAI001 // La Responses API es experimental en el SDK de OpenAI.
#pragma warning disable MAAI001  // Bucle, compactación, ficheros y el cliente sin "store" son experimentales en Agent Framework.

using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.LocalCodeAct;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Core;

/// <summary>
/// step-3 · la construcción del agente sale de Program.cs y se convierte en una factoría:
/// la misma para la consola (ForLocal) y para el Hosted Agent (ForHosted).
///
/// El harness a medida NO es un bucle nuevo: es el HarnessAgent con cuatro piezas enchufadas en sus
/// puntos de extensión documentados:
///   1. BudgetGuard               → DelegatingChatClient alrededor del cliente "hoja"
///   2. RepoStateProvider         → AIContextProviders
///   3. VerifiedCompletionEvaluator → LoopEvaluators (después del de todos)
///   4. CodeOwnerApprovalAgent    → DelegatingAIAgent, el decorador más externo
/// </summary>
public static class UpgradeCaptainFactory
{
    /// <summary>Lista de denegación del shell (PLAN.md §4). Es un filtro de UX, no una frontera de seguridad.</summary>
    public static readonly IReadOnlyList<string> ShellDenyList =
    [
        @"\brm\s+-rf\b",
        @"\bsudo\b",
        @"\bgit\s+push\b",
        @"\bdotnet\s+nuget\s+push\b",
    ];

    public static UpgradeCaptainBuild Create(UpgradeCaptainOptions options)
    {
        // 1. Cliente del modelo: Foundry con "store" desactivado (el historial lo guarda el harness).
        IChatClient chatClient = options.ChatClient ?? CreateFoundryChatClient(options);

        // 2. Servicios de las piezas a medida, sobre el repo y el directorio de estado.
        HarnessServices? services = options.EnableCustomHarness
            ? HarnessServices.Create(options.Paths.RepoDir, options.StateRoot, options.Paths.AdvisoriesFile)
            : null;

        // 3. Reparación tras cortes de red (D-37), compactación integrada en cada llamada (ver CompactingChatClient)
        //    y, con el harness a medida, BudgetGuard alrededor: los tres ven cada llamada al modelo del bucle.
        chatClient = chatClient
            .WithInterruptedToolCallRepair()
            .WithContextWindowCompaction(options.MaxContextWindowTokens, options.MaxOutputTokens);
        BudgetGuard? budget = services is null ? null : new BudgetGuard(chatClient, options.TokenBudget);

        // 4. Shell confinado y CodeAct (step-2).
        LocalShellExecutor? shell = options.EnableShell ? new LocalShellExecutor(CreateShellOptions(options)) : null;
        LocalCodeActProvider? codeAct = CreateCodeAct(options);

        // 5. El HarnessAgent con las mismas opciones que en el step-2, más las piezas a medida.
        HarnessAgentOptions harnessOptions = BuildHarnessOptions(options, shell, codeAct, services);
        AIAgent agent = (budget ?? chatClient).AsHarnessAgent(harnessOptions);

        // Con CodeAct el modelo tiene que VER el error de su Python para corregirse (D-32, igual que en el step-2).
        if (codeAct is not null)
        {
            agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true;
        }

        // 6. Aprueba el propietario del código, no quien pide: decorador más externo.
        if (services is not null)
        {
            agent = new CodeOwnerApprovalAgent(agent, services, new CodeOwnerApprovalAgentOptions
            {
                DefaultRequester = options.Requester,
                DefaultAttended = options.Attended,
            });
        }

        return new UpgradeCaptainBuild(agent, harnessOptions, options, services, budget, shell, codeAct);
    }

    /// <summary>Opciones del HarnessAgent. Pública para que el test de equivalencia con el step-2 las compare.</summary>
    public static HarnessAgentOptions BuildHarnessOptions(
        UpgradeCaptainOptions options,
        ShellExecutor? shell,
        LocalCodeActProvider? codeAct,
        HarnessServices? services)
    {
        // Tools: en direct el modelo ve las 5 tools; en codeact viven dentro de execute_code.
        List<AITool> tools = codeAct is null ? [.. UpgradeTools.Create(options.Paths)] : [];
        if (shell is not null)
        {
            tools.Add(shell.AsAIFunction(requireApproval: true));
        }

        List<AIContextProvider> providers = [];
        if (shell is not null)
        {
            providers.Add(new ShellEnvironmentProvider(shell));
        }

        if (codeAct is not null)
        {
            providers.Add(codeAct);
        }

        if (services is not null)
        {
            providers.Add(new RepoStateProvider(services.Cache, services.Approvals, services.RepoDir)); // verdad del repo
        }

        List<LoopEvaluator> evaluators =
        [
            new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions
            {
                Modes = ["execute"],
                FeedbackMessageTemplate =
                    "Aún quedan tareas abiertas. Sigue trabajando y marca cada una como completada al terminarla. Pendientes:\n"
                    + TodoCompletionLoopEvaluator.RemainingTodosPlaceholder,
            }),
        ];
        if (services is not null)
        {
            // "Terminado" = compila y pasan los tests. Va después del evaluador de todos.
            evaluators.Add(new VerifiedCompletionEvaluator(services.Verifier, services.Cache, services.Checkpoints));
        }

        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
        [
            FileAccessProvider.ReadOnlyToolsAutoApprovalRule,
            options.TrustSkillScripts
                ? AgentSkillsProvider.AllToolsAutoApprovalRule        // Hosted: skills empaquetadas y de confianza
                : AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,  // Local: run_skill_script pregunta
        ];
        if (options.AutoApproveUnprotected && services is not null)
        {
            rules.Add(UnprotectedChangesRule(services.Policy));       // Hosted: solo lo protegido espera a su propietario
        }

        return new HarnessAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Instructions = codeAct is null ? Prompts.Prompt : Prompts.Prompt + Prompts.CodeActHint,
                Tools = tools,
            },
            DisableWebSearch = true,
            AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "plan" },
            LoopEvaluators = evaluators,
            LoopAgentOptions = new LoopAgentOptions { MaxIterations = 10 },
            FileMemoryStore = new FileSystemAgentFileStore(options.MemoryRoot),
            FileAccessStore = options.EnableFileAccess ? new FileSystemAgentFileStore(options.Paths.RepoDir) : null,
            AgentSkillsSource = new AgentFileSkillsSource([options.Paths.SkillsDir], new DotnetScriptRunner(options.Paths.RepoDir).RunAsync),
            AIContextProviders = providers,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions { AutoApprovalRules = rules },
            OpenTelemetrySourceName = Telemetry.SourceName,
        };
    }

    /// <summary>
    /// Regla del Hosted Agent: aprueba run_shell (la lista de permitidos ya lo limita) y las escrituras cuyas rutas
    /// no están protegidas por CODEOWNERS. Las protegidas siguen pidiendo aprobación y se aparcan para su propietario.
    /// </summary>
    private static Func<ToolAutoApprovalRuleContext, ValueTask<bool>> UnprotectedChangesRule(CodeOwnersPolicy policy) => context =>
    {
        FunctionCallContent call = context.FunctionCallContent;
        if (call.Name == "run_shell")
        {
            return ValueTask.FromResult(true);
        }

        IReadOnlyList<string> paths = CodeOwnersPolicy.ExtractPaths(call);
        return ValueTask.FromResult(paths.Count > 0 && !paths.Any(policy.IsProtected));
    };

    private static IChatClient CreateFoundryChatClient(UpgradeCaptainOptions options) =>
        new AIProjectClient(
            new Uri(options.ProjectEndpoint ?? throw new InvalidOperationException("Falta FOUNDRY_PROJECT_ENDPOINT.")),
            new DefaultAzureCredential(),
            new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(3) })
        .GetProjectOpenAIClient()
        .GetProjectResponsesClientForModel(options.Model)
        .AsIChatClientWithStoredOutputDisabled(options.Model);

    private static LocalShellExecutorOptions CreateShellOptions(UpgradeCaptainOptions options)
    {
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };

        // En el contenedor los paquetes están precargados en /opt/nuget: el shell limpio no lo heredaría.
        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } nugetPackages)
        {
            environment["NUGET_PACKAGES"] = nugetPackages;
        }

        return new LocalShellExecutorOptions
        {
            WorkingDirectory = options.Paths.RepoDir,
            ConfineWorkingDirectory = true,
            CleanEnvironment = true,
            Environment = environment,
            Timeout = TimeSpan.FromSeconds(120),
            Policy = new ShellPolicy(denyList: ShellDenyList, allowList: options.ShellAllowList),
        };
    }

    private static LocalCodeActProvider? CreateCodeAct(UpgradeCaptainOptions options) => options.CodeAct switch
    {
        CodeActBackend.None => null,
        CodeActBackend.Local => new LocalCodeActProvider(options.PythonExecutable, new LocalCodeActProviderOptions
        {
            Tools = UpgradeTools.Create(options.Paths).OfType<AIFunction>(),
            ExecutionLimits = new ProcessExecutionLimits { TimeoutSeconds = 20 },
        }),
        CodeActBackend.Hyperlight => throw new NotSupportedException(
            "Hyperlight necesita KVM (Linux x64) o WHP (Windows) y no está incluido en esta build. Ver PLAN.md fase 10."),
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };
}
