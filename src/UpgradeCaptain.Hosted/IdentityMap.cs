using System.Text.Json;

namespace UpgradeCaptain.Hosted;

/// <summary>
/// Quién aprueba (Q2). Con el protocolo de contenedor 2.0.0, Foundry pone en <c>x-agent-user-id</c> la identidad
/// de quien llama (el SDK la expone en <c>context.PlatformContext.UserIdKey</c>), y aquí se traduce a un handle de
/// CODEOWNERS con demo/identities.json.
///
/// Modo desarrollo (UPGRADE_CAPTAIN_DEV_IDENTITY=1): con una sola cuenta, el disparo y la aprobación llegan con
/// el MISMO x-agent-user-id (el tuyo), así que manda el campo <c>approver</c> del payload. Se dice en el escenario.
/// </summary>
public sealed class IdentityMap
{
    private readonly Dictionary<string, string> _handleByUserId = new(StringComparer.OrdinalIgnoreCase);

    public IdentityMap(string identitiesFile)
    {
        if (!File.Exists(identitiesFile))
        {
            return;
        }

        foreach (JsonProperty entry in JsonDocument.Parse(File.ReadAllText(identitiesFile)).RootElement.EnumerateObject())
        {
            if (entry.Name.StartsWith('@') && entry.Value.ValueKind == JsonValueKind.String)
            {
                this._handleByUserId[entry.Value.GetString()!] = entry.Name;
            }
        }
    }

    public static bool DevIdentityAllowed => Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_DEV_IDENTITY") == "1";

    /// <summary>Handle del que llama, o null (con el motivo) si no se puede establecer.</summary>
    public (string? Handle, string? Error) Resolve(string? platformUserId, string? payloadApprover)
    {
        if (DevIdentityAllowed && !string.IsNullOrEmpty(payloadApprover))
        {
            return (payloadApprover, null);
        }

        if (!string.IsNullOrEmpty(platformUserId))
        {
            return this._handleByUserId.TryGetValue(platformUserId, out string? handle)
                ? (handle, null)
                : (null, $"El usuario de x-agent-user-id no está en identities.json.");
        }

        return (null, "No hay x-agent-user-id. El campo 'approver' solo se acepta con UPGRADE_CAPTAIN_DEV_IDENTITY=1.");
    }

    /// <summary>Id de usuario de un handle (lo usa hosted.sh --local para simular la cabecera).</summary>
    public string? UserIdOf(string handle) => this._handleByUserId.FirstOrDefault(kv => kv.Value == handle).Key;
}
