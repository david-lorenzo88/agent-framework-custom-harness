using Harness.Shared.Console;
using Harness.Shared.Console.Commands;
using Microsoft.Agents.AI;
using UpgradeCaptain.Harness;

namespace UpgradeCaptain.Console;

/// <summary>/approvals en la consola: aprobaciones del trabajo actual y estado del log de auditoría.</summary>
internal sealed class ApprovalsCommandHandler(HarnessServices services) : CommandHandler
{
    public override string? GetHelpText() => "/approvals";

    public override async ValueTask<bool> TryHandleAsync(string input, AgentSession session, IUXStateDriver ux)
    {
        if (!input.Trim().Equals("/approvals", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        JobContext? job = JobContext.Get(session);
        var mine = services.Approvals.List().Where(a => job is null || a.JobId == job.JobId).ToList();
        if (mine.Count == 0)
        {
            await ux.WriteInfoLineAsync("No hay aprobaciones en este trabajo.", ConsoleColor.Gray);
        }

        foreach (PendingApproval a in mine)
        {
            string state = a.Status == ApprovalStatus.Pending ? "⏸ pendiente" : $"{(a.Status == ApprovalStatus.Approved ? "✅" : "❌")} {a.DecidedBy}";
            await ux.WriteInfoLineAsync($"{a.Id} {a.ToolName} {string.Join(",", a.Paths)} → {string.Join(" ", a.Owners)} · {state}",
                a.Status == ApprovalStatus.Pending ? ConsoleColor.Yellow : ConsoleColor.Gray);
        }

        AuditVerification audit = services.Audit.Verify();
        await ux.WriteInfoLineAsync(audit.Ok ? $"🔗 Auditoría íntegra ({audit.Count} entradas)" : $"⚠ Auditoría rota en #{audit.BrokenAtSeq}",
            audit.Ok ? ConsoleColor.Green : ConsoleColor.Red);
        return true;
    }
}
