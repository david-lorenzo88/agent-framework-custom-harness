using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace UpgradeCaptain.Harness;

/// <summary>
/// Spans y métricas de las piezas a medida. Misma fuente que HarnessAgentOptions.OpenTelemetrySourceName
/// ("UpgradeCaptain"), así que upgrade_captain.verify aparece en la misma traza que invoke_agent y chat.
/// </summary>
public static class HarnessDiagnostics
{
    public const string SourceName = "UpgradeCaptain";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    /// <summary>Tokens consumidos (BudgetGuard). Etiqueta token.type = input | output | total.</summary>
    public static readonly Counter<long> Tokens = Meter.CreateCounter<long>("upgrade_captain.tokens", unit: "{token}",
        description: "Tokens consumidos por el modelo, contados por BudgetGuard.");

    /// <summary>Verificaciones del repo (RepoVerifier). Etiqueta result = green | red.</summary>
    public static readonly Counter<long> Verifications = Meter.CreateCounter<long>("upgrade_captain.verifications",
        description: "Ejecuciones de dotnet build + dotnet test hechas por el harness.");
}
