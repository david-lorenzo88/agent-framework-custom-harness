using System.Text.Json;
using Azure.AI.AgentServer.Invocations;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Hosted;

/// <summary>
/// Webhook de Invocations. Tres eventos JSON y siempre la misma respuesta (JobStatus):
///   { "type": "upgrade.requested", "jobId", "package", "from", "to", "advisory", "requestedBy" }
///   { "type": "approval.decided", "jobId", "approvalId", "decision": "approve"|"reject", "reason" }
///   { "type": "job.status", "jobId" }
/// Por defecto responde enseguida (202) y el trabajo sigue en la sesión; "wait": true espera al final (Q6).
/// </summary>
public sealed class UpgradeInvocationHandler(UpgradeJobs jobs, IdentityMap identities, ILogger<UpgradeInvocationHandler> logger) : InvocationHandler
{
    public override async Task HandleAsync(HttpRequest request, HttpResponse response, InvocationContext context, CancellationToken cancellationToken)
    {
        JsonElement body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            await WriteAsync(response, 400, new { error = "El cuerpo debe ser JSON." }, cancellationToken);
            return;
        }

        string? type = Get(body, "type");
        string? jobId = Get(body, "jobId");
        // "wait": true en el cuerpo o ?wait=true en la URL (así hosted.sh envía el fichero del trabajo tal cual).
        bool wait = (body.TryGetProperty("wait", out JsonElement w) && w.ValueKind == JsonValueKind.True)
            || (context.QueryParameters.TryGetValue("wait", out var waitQuery) && waitQuery.ToString() == "true");
        logger.LogInformation(
            "Evento {Type} para {JobId} (sesión {SessionId}, invocación {InvocationId}, x-agent-user-id presente: {HasUserId})",
            type, jobId, context.SessionId, context.InvocationId, !string.IsNullOrEmpty(context.PlatformContext.UserIdKey));

        if (jobId is null && type != "upgrade.requested")
        {
            await WriteAsync(response, 400, new { error = "Falta jobId." }, cancellationToken);
            return;
        }

        switch (type)
        {
            case "upgrade.requested":
                UpgradeRequest? upgrade = body.Deserialize<UpgradeRequest>();
                if (upgrade is null || string.IsNullOrWhiteSpace(upgrade.JobId))
                {
                    await WriteAsync(response, 400, new { error = "upgrade.requested necesita jobId, package, from, to y requestedBy." }, cancellationToken);
                    return;
                }

                JobStatus started = await jobs.RequestAsync(upgrade, wait);
                await WriteAsync(response, wait ? 200 : 202, started, cancellationToken);
                return;

            case "approval.decided":
                // Quién aprueba: x-agent-user-id (Foundry, protocolo 2.0.0) → identities.json. El payload, solo en modo desarrollo.
                (string? approver, string? identityError) = identities.Resolve(context.PlatformContext.UserIdKey, Get(body, "approver"));
                if (approver is null)
                {
                    await WriteAsync(response, 403, jobs.Status(jobId!, identityError), cancellationToken);
                    return;
                }

                string decision = Get(body, "decision") ?? "approve";
                (JobStatus decided, bool accepted) = await jobs.DecideAsync(jobId!, Get(body, "approvalId") ?? string.Empty, approver, decision == "approve", Get(body, "reason"), wait);
                await WriteAsync(response, !accepted ? 409 : (wait ? 200 : 202), decided, cancellationToken); // 409: rechazada por las reglas
                return;

            case "job.status":
                JobStatus current = jobs.Status(jobId!);
                await WriteAsync(response, current.Status == JobStates.Unknown ? 404 : 200, current, cancellationToken);
                return;

            default:
                await WriteAsync(response, 400, new { error = $"Tipo de evento desconocido: {type}. Usa upgrade.requested, approval.decided o job.status." }, cancellationToken);
                return;
        }
    }

    private static string? Get(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task WriteAsync(HttpResponse response, int statusCode, object payload, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(response.Body, payload, payload.GetType(), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }, cancellationToken);
    }
}
