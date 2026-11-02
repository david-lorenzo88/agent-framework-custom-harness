using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UpgradeCaptain.Core;

/// <summary>
/// Telemetría de Upgrade Captain. Todo (spans del agente, del chat, de las tools y de los componentes
/// propios del step-3) sale por la misma fuente, <see cref="SourceName"/>, así que acaba en UNA traza.
/// </summary>
public static class Telemetry
{
    /// <summary>Nombre de la ActivitySource y del Meter. Es el mismo que HarnessAgentOptions.OpenTelemetrySourceName.</summary>
    public const string SourceName = "UpgradeCaptain";

    /// <summary>Fuente por defecto de Agent Framework (la usa, por ejemplo, la compactación).</summary>
    private const string AgentFrameworkSourceName = "Experimental.Microsoft.Agents.AI";

    /// <summary>Fuente de spans propios (verify, park, checkpoint...).</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>Métricas propias (tokens consumidos, verificaciones...).</summary>
    public static readonly Meter Meter = new(SourceName);

    /// <summary>
    /// Arranca el exportador OTLP SOLO si existe OTEL_EXPORTER_OTLP_ENDPOINT (p. ej. Aspire Dashboard en :4317).
    /// Nunca exporta a consola: la consola es para la demo. Devuelve null si no hay nada que arrancar.
    /// </summary>
    public static IDisposable? StartIfConfigured(string serviceName = "upgrade-captain")
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            return null;
        }

        ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        TracerProvider tracing = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(SourceName, AgentFrameworkSourceName)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter() // lee OTEL_EXPORTER_OTLP_ENDPOINT / _PROTOCOL del entorno
            .Build();

        MeterProvider metrics = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(SourceName, AgentFrameworkSourceName)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter()
            .Build();

        return new Providers(tracing, metrics);
    }

    private sealed class Providers(TracerProvider tracing, MeterProvider metrics) : IDisposable
    {
        public void Dispose()
        {
            tracing.Dispose(); // Dispose hace flush de lo pendiente
            metrics.Dispose();
        }
    }
}
