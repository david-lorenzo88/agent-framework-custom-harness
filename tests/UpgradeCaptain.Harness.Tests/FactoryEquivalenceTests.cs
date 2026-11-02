using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// PLAN.md fase 7a: con EnableCustomHarness = false, la factoría produce EXACTAMENTE el agente del step-2.
/// Con true, lo mismo más las cuatro piezas a medida.
/// </summary>
public class FactoryEquivalenceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public async Task CustomHarnessOff_IsIdenticalToStep2()
    {
        await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(this.Options(customHarness: false));

        Assert.Equal(Step2Shape(), Shape(build.HarnessOptions));
        Assert.IsType<HarnessAgent>(build.Agent);
        Assert.Null(build.Services);
        Assert.Null(build.Budget);
    }

    [Fact]
    public async Task CustomHarnessOn_IsStep2PlusTheFourPieces()
    {
        await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(this.Options(customHarness: true));

        HarnessShape expected = Step2Shape() with
        {
            ContextProviders = ["ShellEnvironmentProvider", "RepoStateProvider"],
            LoopEvaluators = ["TodoCompletionLoopEvaluator", "VerifiedCompletionEvaluator"],
        };
        Assert.Equal(expected, Shape(build.HarnessOptions));
        Assert.IsType<CodeOwnerApprovalAgent>(build.Agent);
        Assert.NotNull(build.Budget);
        Assert.NotNull(build.Agent.GetService<AgentModeProvider>());
    }

    [Fact]
    public async Task CodeActSurface_MovesTheFiveToolsIntoExecuteCode()
    {
        UpgradeCaptainOptions options = this.Options(customHarness: false);
        options.CodeAct = CodeActBackend.Local;
        await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(options);

        HarnessShape shape = Shape(build.HarnessOptions);
        Assert.Equal(["run_shell"], shape.Tools);
        Assert.Equal(["ShellEnvironmentProvider", "LocalCodeActProvider"], shape.ContextProviders);
        Assert.EndsWith(Prompts.CodeActHint, build.HarnessOptions.ChatOptions!.Instructions);
    }

    /// <summary>Lo que hacía el Program.cs del step-2, descrito campo a campo.</summary>
    private static HarnessShape Step2Shape() => new(
        Tools: ["list_projects", "list_source_files", "read_source", "get_advisories", "get_migration_notes", "run_shell"],
        Instructions: Prompts.Prompt,
        ContextProviders: ["ShellEnvironmentProvider"],
        LoopEvaluators: ["TodoCompletionLoopEvaluator"],
        MaxIterations: 10,
        DefaultMode: "plan",
        DisableWebSearch: true,
        FileAccess: true,
        SkillsSource: "AgentFileSkillsSource",
        AutoApprovalRules: ["FileAccess.ReadOnly", "Skills.ReadOnly"],
        MaxContextWindowTokens: null,   // la compactación va en CompactingChatClient (D-07), no en HarnessAgentOptions
        MaxOutputTokens: null,
        OpenTelemetrySourceName: "UpgradeCaptain");

    private static HarnessShape Shape(HarnessAgentOptions o) => new(
        Tools: o.ChatOptions!.Tools!.Select(t => t.Name).ToArray(),
        Instructions: o.ChatOptions.Instructions,
        ContextProviders: o.AIContextProviders!.Select(p => p.GetType().Name).ToArray(),
        LoopEvaluators: o.LoopEvaluators!.Select(e => e.GetType().Name).ToArray(),
        MaxIterations: o.LoopAgentOptions?.MaxIterations,
        DefaultMode: o.AgentModeProviderOptions?.DefaultMode,
        DisableWebSearch: o.DisableWebSearch,
        FileAccess: o.FileAccessStore is not null,
        SkillsSource: o.AgentSkillsSource?.GetType().Name,
        AutoApprovalRules: o.ToolApprovalAgentOptions!.AutoApprovalRules!.Select(RuleName).ToArray(),
        MaxContextWindowTokens: o.MaxContextWindowTokens,
        MaxOutputTokens: o.MaxOutputTokens,
        OpenTelemetrySourceName: o.OpenTelemetrySourceName);

    private static string RuleName(Func<ToolAutoApprovalRuleContext, ValueTask<bool>> rule) =>
        rule == FileAccessProvider.ReadOnlyToolsAutoApprovalRule ? "FileAccess.ReadOnly"
        : rule == AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule ? "Skills.ReadOnly"
        : rule == AgentSkillsProvider.AllToolsAutoApprovalRule ? "Skills.All"
        : "?";

    private UpgradeCaptainOptions Options(bool customHarness)
    {
        string root = this._temp.Path;
        var paths = new DemoPaths(root, this._temp.Sub("repo"), Path.Combine(root, "advisories.json"), this._temp.Sub("notes"), this._temp.Sub("skills"));
        return new UpgradeCaptainOptions
        {
            Paths = paths,
            ChatClient = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok")),
            EnableShell = true,
            EnableFileAccess = true,
            EnableCustomHarness = customHarness,
            PythonExecutable = "python3",
            MemoryRoot = Path.Combine(root, "memory"),
            StateRoot = Path.Combine(root, "state"),
        };
    }

    /// <summary>Descripción comparable de unas HarnessAgentOptions (las colecciones como string para que Equals funcione).</summary>
    private sealed record HarnessShape(
        IReadOnlyList<string> Tools,
        string? Instructions,
        IReadOnlyList<string> ContextProviders,
        IReadOnlyList<string> LoopEvaluators,
        int? MaxIterations,
        string? DefaultMode,
        bool DisableWebSearch,
        bool FileAccess,
        string? SkillsSource,
        IReadOnlyList<string> AutoApprovalRules,
        int? MaxContextWindowTokens,
        int? MaxOutputTokens,
        string? OpenTelemetrySourceName)
    {
        public bool Equals(HarnessShape? other) => other is not null && this.ToString() == other.ToString();

        public override int GetHashCode() => this.ToString().GetHashCode(StringComparison.Ordinal);

        public override string ToString() =>
            $"tools=[{string.Join(",", this.Tools)}] providers=[{string.Join(",", this.ContextProviders)}] evaluators=[{string.Join(",", this.LoopEvaluators)}] " +
            $"rules=[{string.Join(",", this.AutoApprovalRules)}] maxIter={this.MaxIterations} mode={this.DefaultMode} noWeb={this.DisableWebSearch} files={this.FileAccess} " +
            $"skills={this.SkillsSource} ctx={this.MaxContextWindowTokens}/{this.MaxOutputTokens} otel={this.OpenTelemetrySourceName} instructions={this.Instructions?.GetHashCode(StringComparison.Ordinal)}";
    }
}
