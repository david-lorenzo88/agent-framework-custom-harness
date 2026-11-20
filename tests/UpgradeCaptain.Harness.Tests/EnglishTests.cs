#pragma warning disable MAAI001 // FileSystemAgentFileStore / FileAccessStore son experimentales en Agent Framework.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// La demo en inglés (cabina web y UPGRADE_CAPTAIN_LANG=en): lo que ve el modelo y lo que ve la persona sale en
/// inglés, y el español sigue siendo el idioma por defecto.
/// </summary>
public class EnglishTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [Fact]
    public void Language_DefaultsToSpanish_AndParsesEnglish()
    {
        Assert.Equal(DemoLanguage.Es, DemoLanguages.Parse(null));
        Assert.Equal(DemoLanguage.Es, DemoLanguages.Parse("es"));
        Assert.Equal(DemoLanguage.En, DemoLanguages.Parse(" EN "));
        Assert.Equal(DemoLanguage.En, DemoLanguages.Parse("en-GB"));
    }

    [Fact]
    public void Tools_InEnglish_UseEnglishDescriptionsAndData()
    {
        DemoPaths paths = this.Paths();
        File.WriteAllText(paths.AdvisoriesFile, """[{ "id": "TEIDE-2026-0042", "package": "Teide.Geo", "title": "Denegación de servicio" }]""");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(paths.AdvisoriesFile)!, "advisories.en.json"), """[{ "id": "TEIDE-2026-0042", "package": "Teide.Geo", "title": "Denial of service" }]""");
        File.WriteAllText(Path.Combine(paths.MigrationNotesDir, "2.0.0.md"), "# Notas en español");
        File.WriteAllText(Path.Combine(paths.MigrationNotesDir, "2.0.0.en.md"), "# English notes");

        var en = UpgradeTools.Create(paths, DemoLanguage.En).OfType<AIFunction>().ToDictionary(f => f.Name);
        var es = UpgradeTools.Create(paths, DemoLanguage.Es).OfType<AIFunction>().ToDictionary(f => f.Name);

        Assert.Equal(es.Keys, en.Keys);
        Assert.StartsWith("Lists the .NET projects", en["list_projects"].Description);
        Assert.Contains("NuGet package id", en["get_advisories"].JsonSchema.GetRawText());
        Assert.Contains("Id del paquete NuGet", es["get_advisories"].JsonSchema.GetRawText());

        Assert.Contains("Denial of service", Invoke(en["get_advisories"], ("package", "Teide.Geo")));
        Assert.Contains("Denegación de servicio", Invoke(es["get_advisories"], ("package", "Teide.Geo")));
        Assert.Equal("# English notes", Invoke(en["get_migration_notes"], ("package", "Teide.Geo"), ("fromVersion", "1.8.0"), ("toVersion", "2.0.0")));
        Assert.Equal("# Notas en español", Invoke(es["get_migration_notes"], ("package", "Teide.Geo"), ("fromVersion", "1.8.0"), ("toVersion", "2.0.0")));
        Assert.Equal("Access denied: '../secret' is outside the repository.", Invoke(en["read_source"], ("path", "../secret")));
    }

    [Fact]
    public void Prompts_HaveBothLanguages_WithTheSameRules()
    {
        foreach (string rule in new[] { "list_projects", "get_advisories", "get_migration_notes", "Directory.Packages.props", "file_access_", "file_access_write" })
        {
            Assert.Contains(rule, Prompts.PromptEs);
            Assert.Contains(rule, Prompts.PromptEn);
        }

        Assert.Contains("always answer in English", Prompts.PromptFor(DemoLanguage.En));
        Assert.Contains("`await call_tool(\"read_source\", path=", Prompts.CodeActHintFor(DemoLanguage.En));
        Assert.StartsWith("There are still open tasks.", Prompts.TodoFeedbackFor(DemoLanguage.En));
        Assert.EndsWith(TodoCompletionLoopEvaluator.RemainingTodosPlaceholder, Prompts.TodoFeedbackFor(DemoLanguage.En));
    }

    [Fact]
    public void VerificationFeedback_InEnglish()
    {
        string feedback = (Results.FareTestsRed() with { English = true }).ToFeedback();

        Assert.StartsWith("🔎 Harness verification", feedback);
        Assert.Contains("Tests: 1 of 24 fail:", feedback);
        Assert.EndsWith("The task is not done until this verification is green.", feedback);
        Assert.Equal("build ❌ (1 errors) · tests not run", (Results.BuildRed() with { English = true }).Summary);
    }

    [Fact]
    public async Task RepoState_InEnglish()
    {
        string state = this._temp.Sub("state");
        var provider = new RepoStateProvider(new RepoStateCache(), new ApprovalStore(state), this._temp.Sub("repo")) { English = true };
        AIAgent agent = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok")).AsAIAgent();
        AgentSession session = await agent.CreateSessionAsync();

        AIContext context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(agent, session, new AIContext()));

        Assert.StartsWith("## Verified repository state", context.Instructions);
        Assert.Contains("- Last verification: none yet", context.Instructions);
        Assert.Contains("- Pending approvals: none", context.Instructions);
    }

    [Fact]
    public async Task ApprovalAgent_InEnglish_MarksItsStatusLines()
    {
        string repo = this._temp.Sub("repo");
        string state = this._temp.Sub("state");
        const string Fare = "src/Canarias.Tours.Payments/FareCalculator.cs";
        Directory.CreateDirectory(Path.Combine(repo, ".github"));
        File.WriteAllText(Path.Combine(repo, ".github", "CODEOWNERS"), "* @david-lorenzo\n/src/Canarias.Tours.Payments/ @maria-payments\n");
        Directory.CreateDirectory(Path.Combine(repo, "src", "Canarias.Tours.Payments"));
        File.WriteAllText(Path.Combine(repo, Fare), "old\n");

        var model = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "file_access_replace", new Dictionary<string, object?> { ["fileName"] = Fare, ["oldString"] = "old", ["newString"] = "new" }),
        ]));
        HarnessAgent inner = model.AsHarnessAgent(new HarnessAgentOptions
        {
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableAgentSkillsProvider = true,
            FileAccessStore = new FileSystemAgentFileStore(repo),
            AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
        });
        var services = new HarnessServices(repo, state, new FakeVerifier(Results.Green()), new RepoStateCache(), new FakeCheckpoints(state),
            new SessionVault(state), new ApprovalStore(state), new AuditLog(state), CodeOwnersPolicy.Load(repo)) { English = true };
        var agent = new CodeOwnerApprovalAgent(inner, services, new CodeOwnerApprovalAgentOptions { DefaultAttended = false });

        List<AgentResponseUpdate> updates = [];
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("Fix the fares"))
        {
            updates.Add(update);
        }

        AgentResponseUpdate status = Assert.Single(updates, u => u.AdditionalProperties?.ContainsKey(CodeOwnerApprovalAgent.StatusProperty) == true);
        Assert.Contains("⏸ Pending approval from @maria-payments (APR-0001)", status.Text);

        ApprovalDecisionResult refused = await agent.DecideAsync("APR-0001", "@david-lorenzo", approved: true, reason: null);
        Assert.False(refused.Accepted);
        Assert.Contains("is not an owner of src/Canarias.Tours.Payments/FareCalculator.cs according to CODEOWNERS", refused.Message);
    }

    [Fact]
    public async Task Factory_ExpressesEveryStep_InEnglish()
    {
        UpgradeCaptainOptions Step(bool shell, bool skills, bool custom, bool verified) => new()
        {
            Paths = this.Paths(),
            ChatClient = new ScriptedChatClient((_, _) => new ChatMessage(ChatRole.Assistant, "ok")),
            Language = DemoLanguage.En,
            EnableShell = shell,
            EnableFileAccess = shell,
            EnableSkills = skills,
            EnableCustomHarness = custom,
            EnableVerifiedCompletion = verified,
            MemoryRoot = this._temp.Sub("memory"),
            StateRoot = this._temp.Sub("state"),
        };

        await using UpgradeCaptainBuild step1 = UpgradeCaptainFactory.Create(Step(shell: false, skills: false, custom: false, verified: false));
        Assert.Equal(["list_projects", "list_source_files", "read_source", "get_advisories", "get_migration_notes"], step1.HarnessOptions.ChatOptions!.Tools!.Select(t => t.Name));
        Assert.Null(step1.HarnessOptions.AgentSkillsSource);
        Assert.Null(step1.HarnessOptions.FileAccessStore);
        Assert.Equal(Prompts.PromptEn, step1.HarnessOptions.ChatOptions.Instructions);

        await using UpgradeCaptainBuild step3Pre = UpgradeCaptainFactory.Create(Step(shell: true, skills: true, custom: true, verified: false));
        Assert.Equal(["TodoCompletionLoopEvaluator"], step3Pre.HarnessOptions.LoopEvaluators!.Select(e => e.GetType().Name));
        Assert.IsType<CodeOwnerApprovalAgent>(step3Pre.Agent);
        Assert.True(step3Pre.Services!.English);

        await using UpgradeCaptainBuild step3 = UpgradeCaptainFactory.Create(Step(shell: true, skills: true, custom: true, verified: true));
        Assert.Equal(["TodoCompletionLoopEvaluator", "VerifiedCompletionEvaluator"], step3.HarnessOptions.LoopEvaluators!.Select(e => e.GetType().Name));
    }

    [Fact]
    public void Skills_ExistInBothLanguages()
    {
        string root = DemoPaths.ForLocal().ProjectRoot;
        string[] Files(string dir) => [.. Directory.EnumerateFiles(Path.Combine(root, dir), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Path.Combine(root, dir), f)).Order(StringComparer.Ordinal)];

        Assert.Equal(Files("skills"), Files("skills-en"));
        Assert.Contains("name: nuget-upgrade", File.ReadAllText(Path.Combine(root, "skills-en", "nuget-upgrade", "SKILL.md")));
    }

    private DemoPaths Paths()
    {
        string root = this._temp.Path;
        string security = this._temp.Sub("security");
        return new DemoPaths(root, this._temp.Sub("repo"), Path.Combine(security, "advisories.json"), this._temp.Sub("notes"), this._temp.Sub("skills"));
    }

    private static string Invoke(AIFunction function, params (string Name, object? Value)[] arguments) =>
        function.InvokeAsync(new AIFunctionArguments(arguments.ToDictionary(a => a.Name, a => a.Value))).AsTask().GetAwaiter().GetResult() switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } json => json.GetString()!,
            var other => other?.ToString() ?? string.Empty,
        };
}
