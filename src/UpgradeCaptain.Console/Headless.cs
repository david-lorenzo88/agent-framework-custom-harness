using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Console;

/// <summary>
/// Ejecución sin consola interactiva (--prompt). La usa scripts/measure-surfaces.sh para comparar las
/// superficies direct y codeact: cuenta llamadas al modelo (spans "chat"), llamadas a tools (spans
/// "execute_tool"), tokens y tiempo. Escucha la misma fuente "UpgradeCaptain" que exporta a Aspire.
/// </summary>
internal static class Headless
{
    public static async Task<int> RunOnceAsync(AIAgent agent, string prompt)
    {
        using var metrics = new RunMetrics();
        AgentSession session = await agent.CreateSessionAsync();

        // Para medir, siempre en modo "execute" (en "plan" el agente se pararía a preguntar).
        if (agent.GetService<AgentModeProvider>() is { } modes)
        {
            await modes.SetModeAsync(session, "execute", disableNotification: true); // sin aviso: el filtro de contenido de Azure lo bloquea (D-31)
        }

        var clock = Stopwatch.StartNew();
        int denied = 0;
        AgentResponse response;
        try
        {
            response = await agent.RunAsync(prompt, session);

            // Sin consola nadie aprueba: se deniegan (con motivo) y el agente sigue con las tools de lectura.
            // Así las mediciones de las superficies terminan siempre y son comparables.
            for (int round = 0; round < 5; round++)
            {
                var requests = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
                if (requests.Count == 0)
                {
                    break;
                }

                denied += requests.Count;
                response = await agent.RunAsync(
                    new ChatMessage(ChatRole.User, requests.Select(r => (AIContent)r.CreateResponse(false, "Modo sin consola: no se aprueba nada. Usa las tools de solo lectura.")).ToList()),
                    session);
            }
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            // Errores del servicio (por ejemplo, el filtro de contenido de Azure): se registran y la medición sigue.
            string raw = ex.GetRawResponse()?.Content?.ToString() ?? string.Empty;
            System.Console.WriteLine($"ERROR {ex.Status}: {ex.Message}");
            System.Console.WriteLine(raw.Length > 2000 ? raw[..2000] : raw);
            System.Console.WriteLine(
                $"METRICS model_calls={metrics.ModelCalls} tool_calls={metrics.ToolCalls} " +
                $"input_tokens={metrics.InputTokens} output_tokens={metrics.OutputTokens} wall_s={clock.Elapsed.TotalSeconds:F1} error={ex.Status}");
            return 3;
        }

        clock.Stop();

        System.Console.WriteLine(response.Text);

        int pending = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Count();
        if (denied > 0 || pending > 0)
        {
            System.Console.WriteLine($"⚠ Aprobaciones denegadas: {denied}; pendientes al final: {pending} (el modo sin consola nunca aprueba).");
        }

        System.Console.WriteLine(
            $"METRICS model_calls={metrics.ModelCalls} tool_calls={metrics.ToolCalls} " +
            $"input_tokens={metrics.InputTokens} output_tokens={metrics.OutputTokens} wall_s={clock.Elapsed.TotalSeconds:F1} denied={denied}");
        return pending > 0 ? 2 : 0;
    }

    /// <summary>Suma lo que cuentan los spans gen_ai de Agent Framework mientras dura la ejecución.</summary>
    private sealed class RunMetrics : IDisposable
    {
        private readonly ActivityListener _listener;

        public RunMetrics()
        {
            this._listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Telemetry.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = this.OnStopped,
            };
            ActivitySource.AddActivityListener(this._listener);
        }

        public int ModelCalls { get; private set; }
        public int ToolCalls { get; private set; }
        public long InputTokens { get; private set; }
        public long OutputTokens { get; private set; }

        private void OnStopped(Activity activity)
        {
            switch (activity.GetTagItem("gen_ai.operation.name") as string)
            {
                case "chat":
                    this.ModelCalls++;
                    this.InputTokens += Convert.ToInt64(activity.GetTagItem("gen_ai.usage.input_tokens") ?? 0);
                    this.OutputTokens += Convert.ToInt64(activity.GetTagItem("gen_ai.usage.output_tokens") ?? 0);
                    break;
                case "execute_tool":
                    this.ToolCalls++;
                    break;
            }
        }

        public void Dispose() => this._listener.Dispose();
    }
}
