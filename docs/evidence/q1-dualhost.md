# Q1: Responses host + Invocations host in one ASP.NET Core app

**Answer: YES.** One `WebApplication` can serve both `AddFoundryResponses`/`MapFoundryResponses` and `AddInvocationsServer`/`MapInvocationsServer`. The project restores and builds with 0 warnings and 0 errors. All three endpoints respond (`/readiness`, `/invocations`, `/responses`). No route collides, and no Core version conflict appears.

Experiment date: 2026-10-13. Machine: macOS (Apple Silicon), .NET SDK 10.0.107, packages from nuget.org only. No Azure calls were made.
Scratch project: `<scratchpad>/q1-dualhost/` (`app/` is the beta.7 version, `app-beta6/` is the beta.6 version, and `reflect/` holds the reflection dump).

## Versions

| Package | Requested | Resolved | Depends on Core |
|---|---|---|---|
| Microsoft.Agents.AI.Foundry.Hosting | 1.24.0-preview.261006.1 | same | Core >= 1.0.0-beta.29, Responses >= 1.0.0-beta.8 |
| Azure.AI.AgentServer.Invocations | 1.0.0-beta.7 | same | Core >= 1.0.0-beta.29 |
| Azure.AI.AgentServer.Responses | (transitive) | 1.0.0-beta.8 | Core >= 1.0.0-beta.28 |
| Azure.AI.AgentServer.Core | (transitive) | **1.0.0-beta.29** | (single copy) |
| Microsoft.Agents.AI / .Abstractions / .Workflows | (transitive) | 1.24.0 | |
| Microsoft.Extensions.AI | (transitive) | 10.10.0 (Abstractions 10.10.1) | |

These match the AF repo's `dotnet/Directory.Packages.props` at this tag: Core beta.29, Responses beta.8, Invocations **beta.6**.

**Build result:** `Compilación correcta. 0 Advertencia(s) 0 Errores` (Spanish SDK output for "Build succeeded, 0 warnings, 0 errors"). There were no NU1605/NU1608 warnings. Invocations **beta.6** (the version the AF repo pins) was also tested. It depends on Core >= beta.28, resolves to Core beta.29, builds clean, and all three endpoints work. The fallback is not needed, but it works too.

## Minimal Program.cs that worked

csproj: `Microsoft.NET.Sdk.Web`, `net10.0`, and only two PackageReferences: `Microsoft.Agents.AI.Foundry.Hosting 1.24.0-preview.261006.1` and `Azure.AI.AgentServer.Invocations 1.0.0-beta.7`. `NoWarn` lists `OPENAI001;MEAI001;MAAI001`, though none of these were actually triggered.

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.AI.AgentServer.Invocations;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Responses host: ChatClientAgent over a fake echo IChatClient (no Azure)
AIAgent agent = new ChatClientAgent(new EchoChatClient(), instructions: "echo", name: "dual-agent");
builder.Services.AddFoundryResponses(agent, new InMemoryAgentSessionStore());

// Invocations host
builder.Services.AddInvocationsServer();
builder.Services.AddScoped<InvocationHandler, DualInvocationHandler>();

var app = builder.Build();
app.MapInvocationsServer();   // /invocations*, /invocations_ws
app.MapFoundryResponses();    // /responses* + GET /readiness (only if not already mapped)
app.MapGet("/_routes", (IEnumerable<EndpointDataSource> s) =>   // debug only
    s.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
     .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} {e.RoutePattern.RawText}"));
app.Run();

internal sealed class DualInvocationHandler(AIAgent agent) : InvocationHandler
{
    public override async Task HandleAsync(HttpRequest request, HttpResponse response,
        InvocationContext context, CancellationToken ct)
    {
        var body = await new StreamReader(request.Body).ReadToEndAsync(ct);
        var result = await agent.RunAsync(body, cancellationToken: ct);   // same AIAgent instance as /responses
        response.ContentType = "application/json";
        await response.WriteAsync(JsonSerializer.Serialize(new {
            reply = result.Text, context.InvocationId, context.SessionId,
            userIdKey = context.PlatformContext.UserIdKey, callId = context.PlatformContext.CallId,
            clientHeaders = context.ClientHeaders,
            query = context.QueryParameters.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) }), ct);
    }
}

internal sealed class EchoChatClient : IChatClient
{
    static string Last(IEnumerable<ChatMessage> m) => m.LastOrDefault(x => x.Role == ChatRole.User)?.Text ?? "";
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Echo: {Last(m)}")) { ModelId = "echo" });
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    { yield return new ChatResponseUpdate(ChatRole.Assistant, $"Echo: {Last(m)}") { ModelId = "echo", MessageId = Guid.NewGuid().ToString("N") }; await Task.CompletedTask; }
    public object? GetService(Type t, object? k = null) => t.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}
```

`AddFoundryResponses(AIAgent, AgentSessionStore?, Action<FoundryResponsesOptions>?)` accepts any `AIAgent`, so no Azure credential is needed. Locally, `FoundryEnvironment.IsHosted == false`, so the default store would not create a credential either. `InMemoryAgentSessionStore` was passed only to avoid writing session state to disk.

## Routes (from `GET /_routes`)

```
POST   /invocations
GET    /invocations/{invocationId}
POST   /invocations/{invocationId}/cancel
GET    /invocations/docs/openapi.json
GET    /invocations/docs/asyncapi.json
GET    /invocations/docs/asyncapi.yaml
*      /invocations_ws
POST   /responses
GET    /responses/{responseId}
POST   /responses/{responseId}/cancel
DELETE /responses/{responseId}
GET    /responses/{responseId}/input_items
*      /readiness            [Health checks]   <- mapped by MapFoundryResponses
GET    /_routes              (debug)
```

**Collisions: none.** In Tier 3 (plain `WebApplication`), `MapInvocationsServer` does **not** map `/readiness`. The AF Invocations sample maps it by hand for that reason. `MapFoundryResponses` maps `/readiness` with `MapHealthChecks` only when no route with that pattern exists yet.
- If you map your own `/readiness` **before** `MapFoundryResponses`, the AF package detects it and skips its own mapping. Only your route exists.
- If you map `GET /readiness` **after** `MapFoundryResponses`, both endpoints exist. This causes no AmbiguousMatch error because the GET-specific endpoint wins over the method-less health-check endpoint. However, your handler then **bypasses** the Foundry readiness health checks (toolbox, stored-output, checkpointing). **Recommendation:** in a dual-host app, do not map `/readiness` yourself. `MapFoundryResponses` covers both protocols.
- No middleware is duplicated. The two Add* calls share one Core beta.29 DI graph.

Startup log (excerpt):
```
Azure.AI.AgentServer.Tasks: TaskManager starting (owner=unknown-agent|session:local, ..., hosted=False).
Responses protocol configuration: StorageProvider=FileResponsesProvider DefaultModel=(not set) ...
Invocations protocol registered (route: /invocations, /invocations_ws); ws_keepalive_interval=disabled
Now listening on: http://127.0.0.1:8088
```
The app was run with `ASPNETCORE_URLS=http://127.0.0.1:8088`. The Foundry listen-port startup filter only rewrites the URL when `FOUNDRY_HOSTING_ENVIRONMENT` is set. Note that the Responses SDK's local `FileResponsesProvider` and the TaskManager write under `$HOME/.agentserver/{responses,tasks}`. For the experiment, HOME was pointed at the scratchpad.

## curl outputs (trimmed)

```
$ curl -i http://127.0.0.1:8088/readiness
HTTP/1.1 200 OK
Content-Type: text/plain
Healthy

$ curl -i -X POST "http://127.0.0.1:8088/invocations?agent_session_id=sess-abc&foo=1" \
    -H 'Content-Type: application/json' -H 'x-agent-user-id: user-123' \
    -H 'x-agent-foundry-call-id: call-456' -H 'x-agent-session-id: hdr-sess-999' \
    -H 'x-client-trace: t1' -d '{"message":"hola invocations"}'
HTTP/1.1 200 OK
x-agent-invocation-id: f47907b8-070f-4930-9f73-f831fc43f51d
x-agent-session-id: sess-abc
{"reply":"Echo: {\"message\":\"hola invocations\"}","InvocationId":"f47907b8-...","SessionId":"sess-abc",
 "userIdKey":"user-123","callId":"call-456","clientHeaders":{"x-client-trace":"t1"},
 "query":{"agent_session_id":"sess-abc","foo":"1"}}

$ curl -i -X POST http://127.0.0.1:8088/invocations -H 'x-agent-session-id: hdr-sess-999' \
    -H 'x-agent-invocation-id: my-inv-1' -d 'hdr test'
HTTP/1.1 200 OK
x-agent-invocation-id: my-inv-1                      <- request header honoured
x-agent-session-id: 59225f34-a121-48ba-a6e6-...      <- request x-agent-session-id IGNORED, UUID generated
{"reply":"Echo: hdr test","InvocationId":"my-inv-1","SessionId":"59225f34-...","userIdKey":null,"callId":null,...}

$ curl -i -X POST http://127.0.0.1:8088/responses -H 'Content-Type: application/json' \
    -d '{"input":"hola","model":"x"}'
HTTP/1.1 200 OK
x-agent-session-id: 79b518e3f4451615c720c9a17b...
{"model":"x","id":"caresp_cb96...","object":"response","status":"completed",
 "output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Echo: hola",...}]}],
 "agent_session_id":"79b518e3f4451615c720c9a17b...","agent_reference":null}

$ curl -N -X POST .../responses -d '{"input":"hola stream","model":"x","stream":true}'
event: response.created / response.in_progress / response.output_item.added / response.content_part.added ...  (SSE works)

GET /responses/{id}   -> 200
GET /invocations/x    -> 404 (default InvocationHandler.GetAsync)
```
Log note: with `"model":"x"` the AF handler logs `Agent 'x' not found in keyed services. Attempting default resolution.` and then uses the default agent. This is harmless.

## InvocationContext public members (reflection, Invocations beta.7)

`Azure.AI.AgentServer.Invocations.InvocationContext` (sealed, every property is get-only):

| Member | Type | Source (observed) |
|---|---|---|
| `ctor(string invocationId, string sessionId, IReadOnlyDictionary<string,string> clientHeaders, IReadOnlyDictionary<string,StringValues> queryParameters, PlatformContext platformContext)` | | |
| `InvocationId` | `string` | `x-agent-invocation-id` request header if present, otherwise a generated UUID. It is echoed back in the `x-agent-invocation-id` response header. |
| `SessionId` | `string` | The `agent_session_id` query parameter, otherwise the `FOUNDRY_AGENT_SESSION_ID` env var, otherwise a generated UUID. The `x-agent-session-id` request header is **not** used. The value is echoed in the `x-agent-session-id` response header. |
| `ClientHeaders` | `IReadOnlyDictionary<string,string>` | Every `x-client-*` request header |
| `QueryParameters` | `IReadOnlyDictionary<string,StringValues>` | All query parameters |
| `PlatformContext` | `Azure.AI.AgentServer.Core.PlatformContext` | See below |

`Azure.AI.AgentServer.Core.PlatformContext`: `ctor(string userIdKey, string callId)`, `string UserIdKey` (from **`x-agent-user-id`**), `string CallId` (from **`x-agent-foundry-call-id`**), `static PlatformContext Empty`, `static PlatformContext FromRequest(HttpRequest)`. Both properties are null when the headers are absent.

`InvocationHandler` (abstract): `abstract HandleAsync(HttpRequest, HttpResponse, InvocationContext, CancellationToken)`. The virtual methods `GetAsync(string invocationId, ...)`, `CancelAsync(string invocationId, ...)`, `GetOpenApiAsync`, `GetAsyncApiJsonAsync` and `GetAsyncApiYamlAsync` return 404 by default. `InvocationsServerOptions` has no public members in beta.7.

Related Core types:
- `FoundryAgentRequestContext` has `CallId`, `UserId` and `SessionId` (init), `static Current`, `static Empty`, and `PlatformHeaders()`. It is an ambient per-request context.
- `FoundryEnvironment` exposes static `AgentName`, `AgentId`, `AgentVersion`, `ProjectEndpoint`, `SessionId`, `Port`, `IsHosted`, `OtlpEndpoint`, and similar settings.
- `FoundryCallIdHandler` is a `DelegatingHandler` that echoes the call id on outbound calls.

## Header names found

`Azure.AI.AgentServer.Core.PlatformHeaders` constants (reflection):
```
RequestId        = x-request-id
ServerVersion    = x-platform-server
SessionId        = x-agent-session-id
UserId           = x-agent-user-id
FoundryCallId    = x-agent-foundry-call-id
ClientHeaderPrefix = x-client-
TraceParent      = traceparent
ClientRequestId  = x-ms-client-request-id
ErrorSource      = x-platform-error-source   (values: user | platform | upstream)
ErrorDetail      = x-platform-error-detail
```
DLL string scan:
- `strings Azure.AI.AgentServer.Invocations.dll | grep -i -E 'x-agent|session|user'` finds only identifiers: `SessionIdResolver`, `InjectSessionIdHeader`, `InjectSessionIdHeaderFromEnv`, `SessionIdResponseHeader`, `get_UserIdKey`, `AttrSpanSessionId`, and Voice `OnUser*`/`VoiceSession*`.
- macOS `strings` cannot read .NET UTF-16 literals, so a small Python scan of those was also run. The Invocations DLL's literals include `agent_session_id`, the routes above, the log template `Handling invocation {InvocationId}: HasUserId={HasUserId} HasCallId={HasCallId}`, and the span attributes `azure.ai.agentserver.session_id`, `azure.ai.agentserver.invocation_id` and `microsoft.session.id`.
- **No `x-agent-*` literal exists in the Invocations DLL itself.** Header parsing goes through Core's `PlatformContext.FromRequest`.
- Core DLL literals include `x-agent-user-id`, `x-agent-foundry-call-id`, `x-agent-session-id`, `x-client-`, **`x-ms-user-id`** (`DelegatedUserIdHeader`), `FOUNDRY_AGENT_SESSION_ID`, `/readiness`, and `The local state store fallback does not support user isolation.`
- At runtime, the Invocations host emits the **`x-agent-invocation-id`** and `x-agent-session-id` response headers. `x-agent-invocation-id` is also read from the request.

## Takeaways for the harness

1. A dual-protocol container works with the stock packages. Register both, call `MapInvocationsServer()` and `MapFoundryResponses()`, and let `MapFoundryResponses` own `/readiness`.
2. The same `AIAgent` singleton can back both protocols. `AddFoundryResponses(agent)` registers it as the default `AIAgent`, so the `InvocationHandler` can take it by constructor injection.
3. For per-user partitioning on Invocations, use `context.PlatformContext.UserIdKey` (`x-agent-user-id`). For the session, use `context.SessionId` (the `agent_session_id` query parameter or `FOUNDRY_AGENT_SESSION_ID`), not the `x-agent-session-id` request header.
4. Whether the Foundry platform accepts **two protocols declared for one hosted agent** in `agent.yaml`/`azure.yaml` (`protocols: [responses, invocations]`) was **not** tested. This experiment is local only.
