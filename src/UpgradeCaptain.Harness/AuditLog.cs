using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeCaptain.Harness;

/// <summary>Una entrada del log de auditoría. <see cref="Hash"/> encadena con la entrada anterior.</summary>
public sealed record AuditEntry(
    long Seq,
    DateTimeOffset Timestamp,
    string Event,
    string JobId,
    string Actor,
    string? ApprovalId,
    JsonElement? Details,
    string PrevHash,
    string Hash);

/// <summary>Resultado de comprobar la cadena de hashes.</summary>
public sealed record AuditVerification(bool Ok, int Count, long? BrokenAtSeq, string? Reason);

/// <summary>
/// Log de auditoría append-only en JSONL (StateRoot/audit.jsonl) con cadena de hashes SHA-256:
/// cada entrada guarda el hash de la anterior, así que editar o borrar una línea rompe la cadena.
/// Eventos: requested, parked, refused, decided, resumed, verified, restored.
/// </summary>
public sealed class AuditLog
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AuditLog(string stateRoot) => this.FilePath = Path.Combine(stateRoot, "audit.jsonl");

    public string FilePath { get; }

    public AuditEntry Append(string @event, string jobId, string actor, object? details = null, string? approvalId = null)
    {
        using IDisposable _ = FileLock.Acquire(this.FilePath);
        AuditEntry? last = this.ReadAll().LastOrDefault();
        JsonElement? detailsElement = details is null ? null : JsonSerializer.SerializeToElement(details, JsonOptions);

        var draft = new AuditEntry(
            Seq: (last?.Seq ?? 0) + 1,
            Timestamp: DateTimeOffset.UtcNow,
            Event: @event,
            JobId: jobId,
            Actor: actor,
            ApprovalId: approvalId,
            Details: detailsElement,
            PrevHash: last?.Hash ?? Genesis,
            Hash: string.Empty);
        AuditEntry entry = draft with { Hash = ComputeHash(draft) };

        File.AppendAllText(this.FilePath, JsonSerializer.Serialize(entry, JsonOptions) + "\n");
        return entry;
    }

    public IReadOnlyList<AuditEntry> ReadAll() =>
        !File.Exists(this.FilePath)
            ? []
            : File.ReadAllLines(this.FilePath)
                .Where(line => line.Length > 0)
                .Select(line => JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions)!)
                .ToList();

    /// <summary>Recalcula toda la cadena: cada hash y cada enlace con la entrada anterior.</summary>
    public AuditVerification Verify()
    {
        string previous = Genesis;
        IReadOnlyList<AuditEntry> entries = this.ReadAll();
        foreach (AuditEntry entry in entries)
        {
            if (entry.PrevHash != previous)
            {
                return new AuditVerification(false, entries.Count, entry.Seq, "el enlace con la entrada anterior no coincide");
            }

            if (ComputeHash(entry with { Hash = string.Empty }) != entry.Hash)
            {
                return new AuditVerification(false, entries.Count, entry.Seq, "el contenido no coincide con su hash");
            }

            previous = entry.Hash;
        }

        return new AuditVerification(true, entries.Count, null, null);
    }

    /// <summary>SHA-256 de la entrada serializada sin el hash (el hash anterior sí entra: eso hace la cadena).</summary>
    private static string ComputeHash(AuditEntry entryWithoutHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entryWithoutHash, JsonOptions)))).ToLowerInvariant();
}
