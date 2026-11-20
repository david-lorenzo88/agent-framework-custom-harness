using System.Text.Json.Serialization;

namespace UpgradeCaptain.Hosted;

/// <summary>Estados del trabajo (PLAN.md fase 8).</summary>
public static class JobStates
{
    public const string Running = "running";
    public const string WaitingApproval = "waiting_approval";
    public const string Green = "green";
    public const string Blocked = "blocked";
    public const string BudgetExceeded = "budget_exceeded";

    /// <summary>No hay rastro del trabajo en esta sesión (jobId o agent_session_id equivocados).</summary>
    public const string Unknown = "unknown";
}

/// <summary>Una aprobación pendiente, tal como se expone por HTTP.</summary>
public sealed record ApprovalView(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths,
    [property: JsonPropertyName("owners")] IReadOnlyList<string> Owners,
    [property: JsonPropertyName("requestedBy")] string RequestedBy);

/// <summary>Respuesta de TODOS los eventos: <c>{ jobId, status, pendingApprovals[], lastVerification, reportPath }</c>.</summary>
public sealed record JobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingApprovals")] IReadOnlyList<ApprovalView> PendingApprovals,
    [property: JsonPropertyName("lastVerification")] string? LastVerification,
    [property: JsonPropertyName("reportPath")] string? ReportPath,
    [property: JsonPropertyName("message")] string? Message = null);
