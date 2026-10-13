#pragma warning disable MAAI001 // Opciones del harness experimentales en Agent Framework.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// La skill nuget-upgrade de punta a punta: AgentFileSkillsSource descubre scripts/bump_package.cs y
/// DotnetScriptRunner lo ejecuta con `dotnet run` (sin Python). Integración: necesita el SDK de .NET 10.
/// </summary>
public class SkillScriptTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [IntegrationFact]
    public async Task BumpPackage_IsDiscovered_AndRunsWithDotnetRun()
    {
        string repo = this._temp.Sub("repo");
        File.WriteAllText(Path.Combine(repo, "Directory.Packages.props"),
            "<Project><ItemGroup><PackageVersion Include=\"Teide.Geo\" Version=\"1.8.0\" /></ItemGroup></Project>");
        string skills = Path.Combine(DemoPaths.ForLocal().ProjectRoot, "skills");

        var model = new ScriptedChatClient((messages, _) =>
            messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
                ? new ChatMessage(ChatRole.Assistant, "hecho")
                : new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("c1", "run_skill_script", new Dictionary<string, object?>
                    {
                        ["skillName"] = "nuget-upgrade",
                        ["scriptName"] = "scripts/bump_package.cs",
                        ["arguments"] = new[] { "Directory.Packages.props", "Teide.Geo", "2.0.0" }, // relativa al repo
                    }),
                ]));

        HarnessAgent agent = model.AsHarnessAgent(new HarnessAgentOptions
        {
            DisableWebSearch = true,
            DisableFileMemory = true,
            AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
            AgentSkillsSource = new AgentFileSkillsSource([skills], new DotnetScriptRunner(repo).RunAsync),
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions { AutoApprovalRules = [AgentSkillsProvider.AllToolsAutoApprovalRule] },
        });

        AgentResponse response = await agent.RunAsync("sube Teide.Geo");

        FunctionResultContent result = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>());
        Assert.Contains("\"changed\":true", result.Result?.ToString());
        Assert.Contains("Version=\"2.0.0\"", File.ReadAllText(Path.Combine(repo, "Directory.Packages.props")));
    }
}
