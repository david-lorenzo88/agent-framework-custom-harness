using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UpgradeCaptain.Harness;

/// <summary>
/// Sesiones serializadas (AIAgent.SerializeSessionAsync) por trabajo, en StateRoot/sessions/&lt;jobId&gt;.json.
/// La sesión aparcada ES la autoridad de la aprobación (contiene el request pendiente): por eso se guarda
/// su SHA-256 al aparcar y se comprueba al reanudar.
/// </summary>
public sealed class SessionVault
{
    private readonly string _dir;

    public SessionVault(string stateRoot) => this._dir = Path.Combine(stateRoot, "sessions");

    public bool Exists(string jobId) => File.Exists(this.PathFor(jobId));

    /// <summary>Guarda la sesión y devuelve su SHA-256.</summary>
    public string Save(string jobId, JsonElement session)
    {
        string json = session.GetRawText();
        FileLock.WriteAllTextAtomic(this.PathFor(jobId), json);
        return Sha256(json);
    }

    /// <summary>Carga la sesión; si se indica <paramref name="expectedSha256"/>, falla si no coincide.</summary>
    public JsonElement Load(string jobId, string? expectedSha256 = null)
    {
        string path = this.PathFor(jobId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No hay sesión guardada para el trabajo {jobId}.", path);
        }

        string json = File.ReadAllText(path);
        if (expectedSha256 is not null && !string.Equals(Sha256(json), expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La sesión guardada de {jobId} ha cambiado desde que se aparcó: no se reanuda.");
        }

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private string PathFor(string jobId)
    {
        if (jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || jobId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Id de trabajo no válido: {jobId}");
        }

        return Path.Combine(this._dir, jobId + ".json");
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
