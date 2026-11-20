using System.Text.Json;
using Microsoft.Agents.AI;
using UpgradeCaptain.Core;
using UpgradeCaptain.Harness;
using SysConsole = System.Console;

namespace UpgradeCaptain.Console;

/// <summary>
/// Subcomandos del step-3 (la "terminal de María"):
///   approvals list
///   approvals approve &lt;id|next&gt; --as &lt;handle&gt; [--reason &lt;texto&gt;]   (next = la pendiente más antigua)
///   approvals reject  &lt;id&gt; --as &lt;handle&gt; --reason &lt;texto&gt;
///   approvals audit
///   run --job &lt;fichero&gt;   (desatendido: todas las aprobaciones se aparcan)
/// </summary>
internal static class ApprovalsCli
{
    public static async Task<int> RunAsync(UpgradeCaptainBuild build, IReadOnlyList<string> args, string actor)
    {
        CodeOwnerApprovalAgent approvals = build.Approvals
            ?? throw new InvalidOperationException("Las aprobaciones por propietario necesitan EnableCustomHarness = true.");
        string? reason = ValueOf(args, "--reason");

        switch (args)
        {
            case ["list", ..]:
                PrintList(approvals.Services.Approvals.List());
                return 0;

            case ["approve", "next", ..] when NextPending(approvals) is null:
                Write("✘ No hay aprobaciones pendientes.", ConsoleColor.Red);
                return 1;

            case ["approve", "next", ..]:
                return await DecideAsync(approvals, NextPending(approvals)!, actor, approved: true, reason);

            case ["approve", var id, ..]:
                return await DecideAsync(approvals, id, actor, approved: true, reason);

            case ["reject", var id, ..] when reason is not null:
                return await DecideAsync(approvals, id, actor, approved: false, reason);

            case ["reject", ..]:
                Write("✘ reject necesita --reason <texto>", ConsoleColor.Red);
                return 1;

            case ["audit", ..]:
                PrintAudit(approvals.Services.Audit);
                return 0;

            default:
                SysConsole.WriteLine(DemoLanguages.Current.Pick("Uso: approvals list | approve <id|next> --as <handle> [--reason <texto>] | reject <id> --as <handle> --reason <texto> | audit", "Usage: approvals list | approve <id|next> --as <handle> [--reason <text>] | reject <id> --as <handle> --reason <text> | audit"));
                return 1;
        }
    }

    /// <summary>run --job: ejecución desatendida de un trabajo (como Dependabot): todo lo que pida aprobación se aparca.</summary>
    public static async Task<int> RunJobAsync(UpgradeCaptainBuild build, string jobFile)
    {
        UpgradeRequest job = JsonSerializer.Deserialize<UpgradeRequest>(File.ReadAllText(jobFile))
            ?? throw new InvalidOperationException($"Trabajo no válido: {jobFile}");

        AIAgent agent = build.Agent;
        AgentSession session = await agent.CreateSessionAsync();
        JobContext.Set(session, new JobContext(job.JobId, job.RequestedBy, Attended: false));
        if (agent.GetService<AgentModeProvider>() is { } modes)
        {
            // Desatendido: nadie a quien preguntar en modo plan. Sin aviso de cambio de modo: el filtro de contenido de Azure lo bloquea (D-31).
            await modes.SetModeAsync(session, "execute", disableNotification: true);
        }

        Write($"▶ {job.JobId}: {job.Package} {job.From} → {job.To}, pedido por {job.RequestedBy} (desatendido)", ConsoleColor.Cyan);
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(job.ToPrompt(), session))
        {
            SysConsole.Write(update.Text);
        }

        SysConsole.WriteLine();
        if (build.Services is { } services)
        {
            PrintList(services.Approvals.List().Where(a => a.JobId == job.JobId).ToList());
        }

        return 0;
    }

    /// <summary>"next": la aprobación pendiente más antigua. En el escenario María repite el comando sin leer ids.</summary>
    private static string? NextPending(CodeOwnerApprovalAgent approvals) =>
        approvals.Services.Approvals.List()
            .Where(a => a.Status == ApprovalStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefault()?.Id;

    private static async Task<int> DecideAsync(CodeOwnerApprovalAgent approvals, string id, string actor, bool approved, string? reason)
    {
        Write(approved ? DemoLanguages.Current.Pick($"Aprobando {id} como {actor}…", $"Approving {id} as {actor}…") : DemoLanguages.Current.Pick($"Rechazando {id} como {actor}…", $"Rejecting {id} as {actor}…"), ConsoleColor.Gray);
        ApprovalDecisionResult result = await approvals.DecideAsync(id, actor, approved, reason);
        Write($"{(result.Accepted ? "✔" : "✘")} {result.Message}", result.Accepted ? ConsoleColor.Green : ConsoleColor.Red);
        if (result.Response is { } response)
        {
            SysConsole.WriteLine(response.Text);
        }

        return result.Accepted ? 0 : 2;
    }

    internal static void PrintList(IReadOnlyList<PendingApproval> all)
    {
        if (all.Count == 0)
        {
            Write(DemoLanguages.Current.Pick("No hay aprobaciones.", "There are no approvals."), ConsoleColor.Gray);
            return;
        }

        foreach (PendingApproval a in all)
        {
            ConsoleColor color = a.Status switch
            {
                ApprovalStatus.Pending => ConsoleColor.Yellow,
                ApprovalStatus.Approved => ConsoleColor.Green,
                _ => ConsoleColor.Red,
            };
            string decided = a.Status == ApprovalStatus.Pending ? "pendiente" : $"{a.Status.ToString().ToLowerInvariant()} por {a.DecidedBy}";
            Write($"{a.Id}  {a.JobId}  {a.ToolName} {string.Join(",", a.Paths)}  → {string.Join(" ", a.Owners)}  (pide {a.RequestedBy})  {decided}", color);
        }
    }

    private static void PrintAudit(AuditLog audit)
    {
        foreach (AuditEntry e in audit.ReadAll())
        {
            Write($"#{e.Seq,-3} {e.Timestamp.ToLocalTime():HH:mm:ss} {e.Event,-9} {e.JobId,-24} {e.Actor,-16} {e.ApprovalId} {e.Details?.GetRawText()}  [{e.Hash[..10]}]", ConsoleColor.Gray);
        }

        AuditVerification check = audit.Verify();
        Write(check.Ok
                ? DemoLanguages.Current.Pick($"✔ Cadena de hashes verificada: {check.Count} entradas íntegras.", $"✔ Hash chain verified: {check.Count} intact entries.")
                : $"✘ Cadena rota en la entrada #{check.BrokenAtSeq}: {check.Reason}.",
            check.Ok ? ConsoleColor.Green : ConsoleColor.Red);
    }

    private static string? ValueOf(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag)
            {
                return string.Join(' ', args.Skip(i + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)));
            }
        }

        return null;
    }

    private static void Write(string text, ConsoleColor color)
    {
        SysConsole.ForegroundColor = color;
        SysConsole.WriteLine(text);
        SysConsole.ResetColor();
    }
}
