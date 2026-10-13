using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Harness.Tests;

/// <summary>
/// El beat del step-6 de punta a punta con la factoría REAL, el repo de la demo REAL (after-mechanical-fix),
/// dotnet build/test y git REALES, y un modelo guionizado que se comporta como en el escenario:
///   1. "Hecho" sin ejecutar los tests  →  el evaluador los ejecuta: 8 rojos
///   2. arregla Domain (no protegido)   →  aprobación en consola
///   3. arregla Payments (protegido)    →  se aparca para @maria-payments; @david-lorenzo es rechazado
///   4. María aprueba                   →  verde, checkpoint en upgrade/teide-geo-2.0, auditoría íntegra
/// </summary>
public partial class Step6BeatSimulationTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => this._temp.Dispose();

    [IntegrationFact]
    public async Task WallAndFix_EndToEnd_WithScriptedModel()
    {
        DemoPaths local = DemoPaths.ForLocal();
        string repo = Path.Combine(this._temp.Path, "canarias-tours");
        Run("bash", Path.Combine(local.ProjectRoot, "scripts", "reset-demo.sh"), "--state", "after-mechanical-fix", "--workspace", repo, "--quiet");

        var model = new ScriptedStageModel(repo);
        UpgradeCaptainOptions options = UpgradeCaptainOptions.ForLocal(repo);
        options.ChatClient = model;
        options.StateRoot = Path.Combine(this._temp.Path, "state");
        options.MemoryRoot = Path.Combine(this._temp.Path, "memory");
        await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(options);
        CodeOwnerApprovalAgent agent = build.Approvals!;

        AgentSession session = await agent.CreateSessionAsync();
        JobContext.Set(session, new JobContext("console-test", "@david-lorenzo", Attended: true));
        await agent.GetService<AgentModeProvider>()!.SetModeAsync(session, "execute");

        // 1-2. "Termina y ciérrala" → el harness verifica (rojo) → el modelo edita Domain → aprobación en consola.
        AgentResponse first = await agent.RunAsync("Termina la actualización y ciérrala. No ejecutes los tests, ya los pasará CI.", session);
        ToolApprovalRequestContent domainEdit = Assert.Single(first.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Contains("Verificación del harness", first.Text);
        Assert.Contains("8 de 24 fallan", first.Text);

        // 3. David aprueba en consola lo de Domain; el modelo pasa a Payments y se aparca (la ejecución espera a María).
        Task<AgentResponse> second = agent.RunAsync(new ChatMessage(ChatRole.User, [domainEdit.CreateResponse(true, "ok")]), session);

        PendingApproval fare = await WaitForPendingAsync(build.Services!, "APR-0001");
        Assert.Equal(["@maria-payments"], fare.Owners);
        Assert.False((await agent.DecideAsync(fare.Id, "@david-lorenzo", true, "yo mismo")).Accepted);  // separación de funciones
        Assert.True((await agent.DecideAsync(fare.Id, "@maria-payments", true, "Revisado: tarifa en km")).Accepted);

        // El modelo editó dos ficheros de Payments en la misma respuesta: el segundo se aparca después.
        PendingApproval pricing = await WaitForPendingAsync(build.Services!, "APR-0002");
        Assert.True((await agent.DecideAsync(pricing.Id, "@maria-payments", true, "Revisado")).Accepted);

        AgentResponse final = await second.WaitAsync(TimeSpan.FromMinutes(3));

        // 4. Verde verificado, checkpoint en la rama de la actualización, auditoría íntegra.
        Assert.Contains("✅ Verde verificado por el harness", final.Text);
        Assert.True(build.Services!.Cache.Last!.IsGreen);
        Assert.Equal("upgrade/teide-geo-2.0", Git(repo, "branch", "--show-current"));
        Assert.Contains("verde verificado por Upgrade Captain", Git(repo, "log", "-1", "--format=%s"));
        Assert.True(build.Services.Audit.Verify().Ok);
        string[] events = build.Services.Audit.ReadAll().Select(e => e.Event).Distinct().ToArray();
        Assert.Superset(new HashSet<string>(["verified", "requested", "parked", "refused", "decided", "resumed"]), new HashSet<string>(events));
    }

    private static async Task<PendingApproval> WaitForPendingAsync(HarnessServices services, string id)
    {
        for (int i = 0; i < 600; i++)
        {
            if (services.Approvals.Get(id) is { Status: ApprovalStatus.Pending } approval)
            {
                return approval;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"{id} no llegó a aparcarse.");
    }

    private static string Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output.Trim();
    }

    private static string Git(string repo, params string[] args) => Run("git", ["-C", repo, .. args]);

    /// <summary>
    /// Modelo guionizado con el comportamiento del escenario. Decide por lo que ya hay en la conversación;
    /// los arreglos los calcula leyendo el fichero del disco (Distance(...) → Distance(..., DistanceUnit.Kilometers)).
    /// </summary>
    private sealed partial class ScriptedStageModel(string repo) : IChatClient
    {
        private int _call;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            int results = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Count();
            bool sawFeedback = list.Any(m => m.Role == ChatRole.User && m.Text.Contains("Verificación del harness", StringComparison.Ordinal));
            int call = Interlocked.Increment(ref this._call);

            ChatMessage reply = (sawFeedback, results) switch
            {
                (false, _) => new(ChatRole.Assistant, "Listo: la actualización compila. Doy la tarea por cerrada (los tests los pasará CI)."),
                (true, 0) => new(ChatRole.Assistant, [this.Fix("c-domain", "src/Canarias.Tours.Domain/RoutePlanner.cs")]),
                (true, 1) => new(ChatRole.Assistant,
                [
                    this.Fix("c-fare", "src/Canarias.Tours.Payments/FareCalculator.cs"),
                    this.Fix("c-pricing", "src/Canarias.Tours.Payments/TourPackagePricing.cs"),
                ]),
                _ => new(ChatRole.Assistant, "Corregido: Distance devuelve metros en 2.0; ahora pedimos kilómetros explícitamente."),
            };

            return Task.FromResult(new ChatResponse(reply) { Usage = new UsageDetails { TotalTokenCount = 1000 + call } });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await this.GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private FunctionCallContent Fix(string callId, string relativePath)
        {
            string fixedText = DistanceCall().Replace(File.ReadAllText(Path.Combine(repo, relativePath)), "GeoCalculator.Distance($1, DistanceUnit.Kilometers)");
            return new FunctionCallContent(callId, "file_access_write", new Dictionary<string, object?>
            {
                ["fileName"] = relativePath,
                ["content"] = fixedText,
                ["overwrite"] = true,
            });
        }

        [GeneratedRegex(@"GeoCalculator\.Distance\(((?:[^()]|\([^()]*\))*)\)")]
        private static partial Regex DistanceCall();
    }
}
