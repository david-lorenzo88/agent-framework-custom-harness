using Microsoft.Extensions.AI;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Una entrada del panel en directo. Las que cambian mientras llega el streaming son mutables.</summary>
public abstract class TranscriptEntry
{
    private static int s_next;

    public int Id { get; } = Interlocked.Increment(ref s_next);

    public DateTimeOffset At { get; } = DateTimeOffset.Now;
}

/// <summary>Lo que escribe la persona (o un prompt de la cabina).</summary>
public sealed class UserEntry(string text) : TranscriptEntry
{
    public string Text { get; } = text;
}

/// <summary>Texto del modelo. En modo plan es el JSON del plan mientras llega (se pinta como «redactando el plan»).</summary>
public sealed class AssistantEntry : TranscriptEntry
{
    public string Text { get; set; } = string.Empty;

    public bool Streaming { get; set; } = true;

    public bool Planning { get; init; }
}

public enum ToolState
{
    Running,
    Done,
    Failed,
    AwaitingApproval,
    Denied,
}

/// <summary>Una llamada a una tool y su resultado.</summary>
public sealed class ToolEntry : TranscriptEntry
{
    public required string CallId { get; init; }

    public required string Name { get; init; }

    public IDictionary<string, object?>? Arguments { get; init; }

    public string? Result { get; set; }

    public ToolState State { get; set; }

    public TimeSpan? Duration { get; set; }

    public string Summary => ToolText.Summarize(this.Name, this.Arguments);
}

public enum ApprovalChoice
{
    Approve,
    AlwaysTool,
    AlwaysArguments,
    Deny,
}

/// <summary>Una aprobación integrada del harness (ToolApprovalRequestContent): la decide quien está delante.</summary>
public sealed class ApprovalEntry : TranscriptEntry
{
    public required ToolApprovalRequestContent Request { get; init; }

    public FunctionCallContent? Call => this.Request.ToolCall as FunctionCallContent;

    public string ToolName => this.Call?.Name ?? this.Request.ToolCall.GetType().Name;

    public string Summary => ToolText.Summarize(this.ToolName, this.Call?.Arguments);

    /// <summary>Vista previa del cambio (diff) si la tool edita un fichero del repo.</summary>
    public FileChangePreview? Preview { get; init; }

    public ApprovalChoice? Answer { get; set; }
}

/// <summary>El plan estructurado (PlanningResponse): preguntas con opciones o un plan para aprobar.</summary>
public sealed class PlanEntry : TranscriptEntry
{
    public PlanEntry(PlanningResponse plan)
    {
        this.Plan = plan;
        this.Answers = new string?[plan.Questions.Count];
    }

    public PlanningResponse Plan { get; }

    public string?[] Answers { get; }

    public bool IsApproval => this.Plan.Type == PlanningResponseType.Approval;

    public bool Complete => this.Answers.All(a => a is not null);
}

public enum HarnessKind
{
    Parked,
    Waiting,
    Resumed,
    Rejected,
    Restored,
    Green,
    Red,
    Stuck,
    Budget,
    Verification,
    Loop,
    Info,
}

/// <summary>Lo que dice el harness por su cuenta: estado del CodeOwnerApprovalAgent o feedback del bucle.</summary>
public sealed class HarnessEntry(string text, HarnessKind kind) : TranscriptEntry
{
    public string Text { get; } = text.Trim();

    public HarnessKind Kind { get; } = kind;

    public static HarnessKind KindOf(string text) => text.TrimStart() switch
    {
        var t when t.StartsWith('⏸') => HarnessKind.Parked,
        var t when t.StartsWith('⏳') => HarnessKind.Waiting,
        var t when t.StartsWith('▶') => HarnessKind.Resumed,
        var t when t.StartsWith('✋') => HarnessKind.Rejected,
        var t when t.StartsWith('↩') => HarnessKind.Restored,
        var t when t.StartsWith('✅') => HarnessKind.Green,
        var t when t.StartsWith('❌') => HarnessKind.Red,
        var t when t.StartsWith('⛔') => HarnessKind.Stuck,
        var t when t.StartsWith("💸", StringComparison.Ordinal) => HarnessKind.Budget,
        var t when t.StartsWith("🔎", StringComparison.Ordinal) => HarnessKind.Verification,
        _ => HarnessKind.Info,
    };
}

public enum NoticeLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Avisos de la propia cabina: agente construido, modo cambiado, errores de red…</summary>
public sealed class NoticeEntry(string text, NoticeLevel level = NoticeLevel.Info) : TranscriptEntry
{
    public string Text { get; } = text;

    public NoticeLevel Level { get; } = level;
}

/// <summary>Diff de un cambio de fichero que espera aprobación.</summary>
public sealed record FileChangePreview(string Path, IReadOnlyList<DiffLine> Lines, bool Protected);
