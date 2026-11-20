using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Web.Cockpit;

public sealed record HostedApproval(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths,
    [property: JsonPropertyName("owners")] IReadOnlyList<string> Owners,
    [property: JsonPropertyName("requestedBy")] string RequestedBy);

/// <summary>El JobStatus del Hosted Agent (src/UpgradeCaptain.Hosted/JobStatus.cs), tal como llega por HTTP.</summary>
public sealed record HostedJobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingApprovals")] IReadOnlyList<HostedApproval>? PendingApprovals,
    [property: JsonPropertyName("lastVerification")] string? LastVerification,
    [property: JsonPropertyName("reportPath")] string? ReportPath,
    [property: JsonPropertyName("message")] string? Message);

public sealed record HostedCall(DateTimeOffset At, string Event, int? Http, string Body, bool Ok);

/// <summary>
/// scripts/hosted.sh en C#: habla con el Hosted Agent (step-4) por el protocolo Invocations.
///   · Foundry: &lt;FOUNDRY_PROJECT_ENDPOINT&gt;/agents/upgrade-captain/endpoint/protocols, token de Entra para
///     https://ai.azure.com, api-version=v1 y agent_session_id (un UUID guardado en out/hosted/&lt;jobId&gt;.session,
///     el mismo fichero que usa hosted.sh)
///   · Local: http://localhost:8088 (scripts/hosted.sh serve --local), sesión = jobId e identidad simulada con
///     x-agent-user-id (demo/identities.json)
/// Solo llama cuando la persona pulsa un botón: nunca despliega ni cambia nada en Azure.
/// </summary>
public sealed class HostedClient(Action notify)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly List<HostedCall> _calls = [];
    private CancellationTokenSource? _watch;

    public bool Local { get; set; }

    public bool Busy { get; private set; }

    public bool Watching => this._watch is not null;

    public HostedJobStatus? Last { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<HostedCall> Calls => [.. this._calls];

    private static string Root => DemoPaths.Current.ProjectRoot;

    public string JobFile => Path.Combine(Root, "demo", "jobs", "upgrade-teide-geo.json");

    public string JobId => JsonNode.Parse(File.ReadAllText(this.JobFile))?["jobId"]?.GetValue<string>() ?? "job-001";

    public string SessionFile => Path.Combine(Root, "out", "hosted", $"{this.JobId}.session");

    public string? SessionId => this.Local
        ? this.JobId
        : Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_SESSION_ID") is { Length: > 0 } fixedId ? fixedId
        : File.Exists(this.SessionFile) ? File.ReadAllText(this.SessionFile).Trim() : null;

    public string? Endpoint => this.Local
        ? "http://localhost:8088"
        : Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_HOSTED_ENDPOINT") is { Length: > 0 } custom ? custom.TrimEnd('/')
        : Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT") is { Length: > 0 } project
            ? $"{project.TrimEnd('/')}/agents/{Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_AGENT_NAME") ?? "upgrade-captain"}/endpoint/protocols"
            : null;

    /// <summary>upgrade.requested con demo/jobs/upgrade-teide-geo.json. En Foundry abre una sesión nueva (UUID).</summary>
    public Task TriggerAsync()
    {
        if (!this.Local)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SessionFile)!);
            File.WriteAllText(this.SessionFile, Guid.NewGuid().ToString() + "\n");
        }

        return this.PostAsync("upgrade.requested", File.ReadAllText(this.JobFile), approver: null);
    }

    public Task StatusAsync() =>
        this.PostAsync("job.status", JsonSerializer.Serialize(new { type = "job.status", jobId = this.JobId }), approver: null);

    public Task DecideAsync(string approvalId, string approver, bool approve, string? reason) =>
        this.PostAsync("approval.decided", JsonSerializer.Serialize(new
        {
            type = "approval.decided",
            jobId = this.JobId,
            approvalId,
            decision = approve ? "approve" : "reject",
            reason,
            approver, // en Foundry solo vale con UPGRADE_CAPTAIN_DEV_IDENTITY=1 (D-39); en local manda x-agent-user-id
        }), approver);

    /// <summary>job.status cada 5 s hasta que el trabajo termina (green, blocked, budget_exceeded) o se para.</summary>
    public void ToggleWatch()
    {
        if (this._watch is not null)
        {
            this._watch.Cancel();
            this._watch = null;
            notify();
            return;
        }

        var cts = new CancellationTokenSource();
        this._watch = cts;
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await this.StatusAsync();
                if (this.Last?.Status is "green" or "blocked" or "budget_exceeded")
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (this._watch == cts)
            {
                this._watch = null;
            }

            notify();
        });
        notify();
    }

    private async Task PostAsync(string eventName, string body, string? approver)
    {
        if (this.Busy && eventName != "job.status")
        {
            return;
        }

        this.Busy = true;
        this.Error = null;
        notify();
        try
        {
            string endpoint = this.Endpoint ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT / UPGRADE_CAPTAIN_HOSTED_ENDPOINT is not set.");
            string session = this.SessionId ?? throw new InvalidOperationException($"No session for {this.JobId}: trigger the job first.");
            bool wait = false;
            string query = this.Local
                ? $"agent_session_id={Uri.EscapeDataString(session)}&wait={(wait ? "true" : "false")}"
                : $"api-version=v1&agent_session_id={Uri.EscapeDataString(session)}&wait={(wait ? "true" : "false")}"; // agent_session_id va SIEMPRE en la query (Q2)

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/invocations?{query}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            if (!this.Local)
            {
                AccessToken token = await new DefaultAzureCredential().GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }
            else if (approver is not null && IdentityFor(approver) is { } userId)
            {
                request.Headers.Add("x-agent-user-id", userId);
            }

            using HttpResponseMessage response = await Http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            string shown = text;
            try
            {
                JsonNode? json = JsonNode.Parse(text);
                shown = json?.ToJsonString(Pretty) ?? text;
                if (json?["status"] is not null && json["jobId"] is not null)
                {
                    this.Last = json.Deserialize<HostedJobStatus>();
                }
            }
            catch (JsonException)
            {
                // no es JSON: se enseña tal cual
            }

            this._calls.Insert(0, new HostedCall(DateTimeOffset.Now, eventName, (int)response.StatusCode, shown, response.IsSuccessStatusCode));
        }
        catch (Exception ex)
        {
            this.Error = ex.Message;
            this._calls.Insert(0, new HostedCall(DateTimeOffset.Now, eventName, null, ex.Message, false));
        }
        finally
        {
            if (this._calls.Count > 20)
            {
                this._calls.RemoveRange(20, this._calls.Count - 20);
            }

            this.Busy = false;
            notify();
        }
    }

    private static string? IdentityFor(string handle)
    {
        string file = Path.Combine(Root, "demo", "identities.json");
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))?[handle]?.GetValue<string>() : null;
    }
}
