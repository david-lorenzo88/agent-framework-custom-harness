using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeCaptain.Harness;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Una aprobación aparcada (PLAN.md §9.5): qué tool, sobre qué rutas, de quién es el código y quién lo pidió.
/// <see cref="RequestJson"/> guarda el ToolApprovalRequestContent original: al reanudar se responde a ESE
/// request (CreateResponse), que es lo que acepta el binding de aprobaciones del harness (Q3).
/// </summary>
public sealed record PendingApproval
{
    public required string Id { get; init; }
    public required string JobId { get; init; }

    /// <summary>Las aprobaciones de una misma respuesta del modelo se aparcan y se reanudan juntas.</summary>
    public required string BatchId { get; init; }

    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Propietarios que deben aprobar (CODEOWNERS de las rutas tocadas).</summary>
    public required IReadOnlyList<string> Owners { get; init; }

    public required string RequestedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string RequestJson { get; init; }

    /// <summary>SHA-256 de la sesión guardada en SessionVault al aparcar (se comprueba al reanudar).</summary>
    public required string SessionSha256 { get; init; }

    public bool Attended { get; init; }

    /// <summary>PID de la consola que está esperando la decisión (si la hay): esa consola reanuda.</summary>
    public int? WaiterPid { get; init; }

    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;
    public string? DecidedBy { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public string? Reason { get; init; }

    /// <summary>Cuándo se envió la respuesta al agente (null mientras no se haya reanudado).</summary>
    public DateTimeOffset? AppliedAt { get; init; }
}

/// <summary>
/// Aprobaciones aparcadas, en StateRoot/approvals.json. Compartido entre procesos (consola de David,
/// terminal de María, Hosted Agent) con cerrojo de fichero y escrituras atómicas.
/// </summary>
public sealed class ApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ApprovalStore(string stateRoot) => this.FilePath = Path.Combine(stateRoot, "approvals.json");

    public string FilePath { get; }

    public IReadOnlyList<PendingApproval> List() => this.Read();

    public PendingApproval? Get(string id) =>
        this.Read().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// El lote abierto de un trabajo: aparcado y todavía sin aplicar (pendiente o ya decidido).
    /// Solo puede haber uno: mientras está abierto, la sesión no avanza.
    /// </summary>
    public IReadOnlyList<PendingApproval> OpenBatch(string jobId)
    {
        var open = this.Read().Where(a => a.JobId == jobId && a.AppliedAt is null).ToList();
        string? batch = open.OrderBy(a => a.CreatedAt).Select(a => a.BatchId).FirstOrDefault();
        return batch is null ? [] : open.Where(a => a.BatchId == batch).ToList();
    }

    /// <summary>Guarda una aprobación nueva y le asigna un id legible: APR-0001, APR-0002...</summary>
    public PendingApproval Add(Func<string, PendingApproval> create)
    {
        using IDisposable _ = FileLock.Acquire(this.FilePath);
        List<PendingApproval> all = this.Read();
        int next = all.Select(a => int.TryParse(a.Id.AsSpan(4), out int n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        PendingApproval approval = create($"APR-{next:0000}");
        all.Add(approval);
        this.Write(all);
        return approval;
    }

    public PendingApproval Update(string id, Func<PendingApproval, PendingApproval> change)
    {
        using IDisposable _ = FileLock.Acquire(this.FilePath);
        List<PendingApproval> all = this.Read();
        int index = all.FindIndex(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new KeyNotFoundException($"No existe la aprobación {id}.");
        }

        all[index] = change(all[index]);
        this.Write(all);
        return all[index];
    }

    private List<PendingApproval> Read() =>
        File.Exists(this.FilePath)
            ? JsonSerializer.Deserialize<List<PendingApproval>>(File.ReadAllText(this.FilePath), JsonOptions) ?? []
            : [];

    private void Write(List<PendingApproval> all) =>
        FileLock.WriteAllTextAtomic(this.FilePath, JsonSerializer.Serialize(all, JsonOptions));
}
