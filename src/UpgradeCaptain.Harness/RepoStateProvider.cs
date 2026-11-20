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

    /// <summary>Idioma de las instrucciones que se inyectan (false = español).</summary>
    public bool English { get; init; }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder(this.L("## Estado verificado del repositorio\n", "## Verified repository state\n"));
        VerificationResult? last = cache.Last;

        // Versión de Teide.Geo: la resuelta en la última verificación o, si aún no la hay, la de Directory.Packages.props.
        string version = last?.ResolvedVersions.GetValueOrDefault("Teide.Geo") ?? ReadRequestedVersion(repoDir) ?? this.L("desconocida", "unknown");
        sb.AppendLine(this.L($"- Teide.Geo resuelto: {version}", $"- Teide.Geo resolved: {version}"));

        if (last is null)
        {
            sb.AppendLine(this.L(
                "- Última verificación: ninguna todavía (el harness compila y ejecuta los tests al terminar en modo execute).",
                "- Last verification: none yet (the harness builds and runs the tests when you finish in execute mode)."));
        }
        else
        {
            string verdict = last.IsGreen ? this.L("✅ VERDE", "✅ GREEN") : this.L("❌ ROJO", "❌ RED");
            sb.AppendLine(this.L(
                $"- Última verificación ({last.VerifiedAt.ToLocalTime():HH:mm:ss}): {verdict} · {last.Summary}",
                $"- Last verification ({last.VerifiedAt.ToLocalTime():HH:mm:ss}): {verdict} · {last.Summary}"));
            foreach (string item in last.Errors.Concat(last.FailedTests.Select(t => $"{t.Name}: {t.Message}")).Take(5))
            {
                sb.AppendLine($"  · {item}");
            }
        }

        JobContext? job = JobContext.Get(context.Session);
        var pending = job is null ? [] : approvals.OpenBatch(job.JobId).Where(a => a.Status == ApprovalStatus.Pending).ToList();
        string pendingList = string.Join("; ", pending.Select(a => $"{a.Id} {a.ToolName} {string.Join(",", a.Paths)} → {string.Join(" ", a.Owners)}"));
        sb.AppendLine(pending.Count == 0
            ? this.L("- Aprobaciones pendientes: ninguna", "- Pending approvals: none")
            : this.L($"- Aprobaciones pendientes: {pendingList}", $"- Pending approvals: {pendingList}"));

        // Las decisiones las toma el harness, fuera de la conversación: sin esto el modelo no sabe quién aprobó qué
        // y el informe final diría «Aprobaciones: ninguna» (ensayo del step-4).
        var decided = job is null ? [] : approvals.List().Where(a => a.JobId == job.JobId && a.Status != ApprovalStatus.Pending).ToList();
        if (decided.Count > 0)
        {
            string decidedList = string.Join("; ", decided.Select(a =>
                $"{a.Id} {string.Join(",", a.Paths)} {(a.Status == ApprovalStatus.Approved ? this.L("aprobada", "approved") : this.L("rechazada", "rejected"))} {this.L("por", "by")} {a.DecidedBy}"));
            sb.AppendLine(this.L($"- Aprobaciones decididas: {decidedList}", $"- Decided approvals: {decidedList}"));
        }

        string text = sb.ToString();
        return new ValueTask<AIContext>(new AIContext { Instructions = text.Length <= MaxChars ? text : text[..(MaxChars - 1)] + "…" });
    }

    private string L(string es, string en) => this.English ? en : es;

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
