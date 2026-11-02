using System.Text;
using Microsoft.Agents.AI;

namespace UpgradeCaptain.Harness;

/// <summary>
/// Verdad del repo en cada ejecución: versión resuelta, última verificación y aprobaciones pendientes.
/// Se inyecta como instrucciones en CADA ejecución, así que sobrevive a la compactación
/// (que puede resumir y perder los errores exactos del compilador).
/// </summary>
public sealed class RepoStateProvider(RepoStateCache cache, ApprovalStore approvals, string repoDir) : AIContextProvider
{
    public const int MaxChars = 1500;

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("## Estado verificado del repositorio\n");
        VerificationResult? last = cache.Last;

        // Versión de Teide.Geo: la resuelta en la última verificación o, si aún no la hay, la de Directory.Packages.props.
        string version = last?.ResolvedVersions.GetValueOrDefault("Teide.Geo") ?? ReadRequestedVersion(repoDir) ?? "desconocida";
        sb.AppendLine($"- Teide.Geo resuelto: {version}");

        if (last is null)
        {
            sb.AppendLine("- Última verificación: ninguna todavía (el harness compila y ejecuta los tests al terminar en modo execute).");
        }
        else
        {
            sb.AppendLine($"- Última verificación ({last.VerifiedAt.ToLocalTime():HH:mm:ss}): {(last.IsGreen ? "✅ VERDE" : "❌ ROJO")} · {last.Summary}");
            foreach (string item in last.Errors.Concat(last.FailedTests.Select(t => $"{t.Name}: {t.Message}")).Take(5))
            {
                sb.AppendLine($"  · {item}");
            }
        }

        JobContext? job = JobContext.Get(context.Session);
        var pending = job is null ? [] : approvals.OpenBatch(job.JobId).Where(a => a.Status == ApprovalStatus.Pending).ToList();
        sb.AppendLine(pending.Count == 0
            ? "- Aprobaciones pendientes: ninguna"
            : $"- Aprobaciones pendientes: {string.Join("; ", pending.Select(a => $"{a.Id} {a.ToolName} {string.Join(",", a.Paths)} → {string.Join(" ", a.Owners)}"))}");

        // Las decisiones las toma el harness, fuera de la conversación: sin esto el modelo no sabe quién aprobó qué
        // y el informe final diría «Aprobaciones: ninguna» (ensayo del step-4).
        var decided = job is null ? [] : approvals.List().Where(a => a.JobId == job.JobId && a.Status != ApprovalStatus.Pending).ToList();
        if (decided.Count > 0)
        {
            sb.AppendLine($"- Aprobaciones decididas: {string.Join("; ", decided.Select(a => $"{a.Id} {string.Join(",", a.Paths)} {(a.Status == ApprovalStatus.Approved ? "aprobada" : "rechazada")} por {a.DecidedBy}"))}");
        }

        string text = sb.ToString();
        return new ValueTask<AIContext>(new AIContext { Instructions = text.Length <= MaxChars ? text : text[..(MaxChars - 1)] + "…" });
    }

    private static string? ReadRequestedVersion(string repoDir)
    {
        string props = Path.Combine(repoDir, "Directory.Packages.props");
        if (!File.Exists(props))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(props), "Include=\"Teide\\.Geo\"\\s+Version=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
