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
    /// <summary>La petición, tal como la recibe el agente, en el idioma activo (UPGRADE_CAPTAIN_LANG).</summary>
    public string ToPrompt() => this.ToPrompt(DemoLanguages.Current);

    public string ToPrompt(DemoLanguage language) => language == DemoLanguage.En
        ? $"""
          Job {this.JobId}, requested by {this.RequestedBy}: upgrade {this.Package} from {this.From} to {this.To} in Canarias.Tours{(this.Advisory is null ? "" : $" because of security advisory {this.Advisory}")}.
          Follow the playbook of the nuget-upgrade skill and the matching migration guide. Change the version,
          fix every breaking point and leave the repository building with all the tests green.
          When you finish, write UPGRADE_REPORT.md at the repository root with the playbook's template.
          """
        : $"""
          Trabajo {this.JobId}, pedido por {this.RequestedBy}: actualiza {this.Package} de {this.From} a {this.To} en Canarias.Tours{(this.Advisory is null ? "" : $" por el aviso de seguridad {this.Advisory}")}.
          Sigue el playbook de la skill nuget-upgrade y la guía de migración que corresponda. Haz el cambio de versión,
          arregla todos los puntos de rotura y deja el repositorio compilando con todos los tests en verde.
          Al terminar escribe UPGRADE_REPORT.md en la raíz del repositorio con la plantilla del playbook.
          """;
}
