using System.Text.Json.Serialization;

namespace UpgradeCaptain.Core;

/// <summary>
/// Un trabajo de actualización: el evento <c>upgrade.requested</c> del Hosted Agent y el fichero de
/// <c>run --job</c> (demo/jobs/upgrade-teide-geo.json) tienen la misma forma.
/// </summary>
public sealed record UpgradeRequest(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("advisory")] string? Advisory,
    [property: JsonPropertyName("requestedBy")] string RequestedBy)
{
    /// <summary>La petición, en español, tal como la recibe el agente.</summary>
    public string ToPrompt() =>
        $"""
        Trabajo {this.JobId}, pedido por {this.RequestedBy}: actualiza {this.Package} de {this.From} a {this.To} en Canarias.Tours{(this.Advisory is null ? "" : $" por el aviso de seguridad {this.Advisory}")}.
        Sigue el playbook de la skill nuget-upgrade y la guía de migración que corresponda. Haz el cambio de versión,
        arregla todos los puntos de rotura y deja el repositorio compilando con todos los tests en verde.
        Al terminar escribe UPGRADE_REPORT.md en la raíz del repositorio con la plantilla del playbook.
        """;
}
