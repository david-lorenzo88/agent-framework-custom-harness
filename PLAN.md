# Upgrade Captain · Demo build plan for Claude Code

Session: **"Plan, execute, approve: el Agent Harness de Microsoft Agent Framework a fondo"**
Event: **netcoreconf Madrid 2026**, 23 October 2026. Stage language: Spanish.
Speaker: David Lorenzo López. Host machine: **macOS (Apple Silicon)**. Editor on stage: VS Code.
Target: everything built and rehearsed by **21 October**.

## How to use this plan

Put this file at the root of an empty git repo named `upgrade-captain` and start Claude Code with:

> Read PLAN.md. Execute Phase 0 only, then stop and report.

Rules for every phase:

- **Stop at the end of each phase.** Report what was done, what was verified and what is still uncertain, then wait for explicit confirmation.
- **Never touch Azure** (provision, deploy, role assignments) without asking first.
- **Never invent APIs.** Section 6 lists what was verified against the Agent Framework source. Anything else must be checked in `.ref/` before use.
- **If reality disagrees with this plan, reality wins.** Record the difference in `docs/verification-log.md` and propose the smallest change. Do not silently work around it.

---

## 1. The story

### The real-world problem

A security advisory flags `Teide.Geo` below 2.0.0. The fix is a major version with breaking changes. Dependabot or Renovate opens the bump PR, the build goes red, and the PR sits for weeks because nobody owns the migration.

**Upgrade Captain** takes that bump to green. It inventories the breaking call sites, fixes them, builds, tests, gets the code owner's approval for protected code, and writes a report.

### Why it needs a custom harness

The built-in `HarnessAgent` covers most of the work (steps 0 to 5). Running it unattended against a real repo hits four walls that the built-in harness does not cover. These were checked against the `dotnet-1.24.0` source:

| Requirement | Built-in behaviour at 1.24.0 | Custom piece |
|---|---|---|
| "Done" means the repo builds and tests pass, whatever the model or the user said | `TodoCompletionLoopEvaluator`, `CompletionMarkerLoopEvaluator` and `AIJudgeLoopEvaluator` judge the conversation. Nothing runs the build. `LoopEvaluator` is the extension point | `VerifiedCompletionEvaluator` |
| The approver is the code owner of the touched path, and not the requester | The approval request returns to the caller of that session. `LoopAgent` stops on a pending approval. When hosted over Responses, pending approvals are stored per agent and per user, so another user cannot resolve them (`ToolApprovalIdMap.cs`) | `CodeOwnerApprovalAgent` with `CodeOwnersPolicy`, `ApprovalStore` and an audit log |
| Undo a bad change | No rollback for shell or file changes | `GitCheckpoints` |
| Ground truth after compaction, plus a cost cap | Compaction can summarise away exact compiler errors. Only iteration caps exist (`MaxIterations`, `MaximumIterationsPerRequest`) | `RepoStateProvider` and `BudgetGuard` |

"Custom harness" in this talk means **`HarnessAgent` plus these components, plugged into its documented extension points**: `LoopEvaluators`, `AIContextProviders`, an outer `DelegatingAIAgent` and an inner `DelegatingChatClient`. It is not a from-scratch loop. The abstract promises the built-in pieces; the closing slide covers when a from-scratch loop is warranted.

---

## 2. Architecture

```
Trigger: console (attended) | headless job | Invocations webhook (hosted)
  │
  ▼
CodeOwnerApprovalAgent                      custom · DelegatingAIAgent, outermost
  │  parks approvals for protected paths, resumes saved sessions, writes the audit log
  ▼
HarnessAgent                                built-in
  ├─ decorators: LoopAgent (outermost), ToolApprovalAgent, OpenTelemetryAgent
  │    LoopEvaluators, in order:
  │      1. TodoCompletionLoopEvaluator     built-in, mode "execute"
  │      2. VerifiedCompletionEvaluator     custom, mode "execute"
  ├─ context providers
  │    Todo, AgentMode, FileMemory, AgentSkills, FileAccess    built-in
  │    ShellEnvironmentProvider, LocalCodeActProvider          preview packages
  │    RepoStateProvider                                       custom
  └─ chat pipeline: approval binding → function invocation → per-call persistence → compaction
        ▼
     BudgetGuard                            custom · DelegatingChatClient
        ▼
     IChatClient                            Foundry project, Responses API
```

Shared services used by the custom pieces: `RepoVerifier`, `RepoStateCache`, `GitCheckpoints`, `SessionVault`, `ApprovalStore`, `AuditLog`. All of them emit spans and metrics on the source name `UpgradeCaptain`, the same name passed to `OpenTelemetrySourceName`, so everything lands in one trace.

---

## 3. Steps

One git tag per step. The deck mirrors them one to one.

| Tag | Slide name | What it adds | Built-in or custom | Abstract bullet |
|---|---|---|---|---|
| `step-0` | Chat básico | Plain `ChatClientAgent`, no tools, no session | built-in | Agente que solo conversa |
| `step-1` | Harness | `AsHarnessAgent`, `UpgradeTools.All`, persisted history, compaction, telemetry plumbing | built-in | Loop, historial, compactación |
| `step-2` | Plan / execute | Plan mode by default, `TodoCompletionLoopEvaluator` | built-in | Plan y ejecución |
| `step-3` | Approve | Confined shell, file access, in-console approvals, deny list | built-in + preview package | Shell y ficheros con aprobaciones |
| `step-4` | CodeAct | `LocalCodeActProvider`, `--surface direct\|codeact` | preview package | CodeAct |
| `step-5` | Skills y OTel | File-based skills with a script, Aspire Dashboard | built-in | Skills y telemetría |
| `step-6` | Tu harness | Factory refactor and the five custom components | **custom** | Cuándo compensa montártelo |
| `step-7` | Hosted Agent | Same factory behind an Invocations host in Foundry | built-in hosting + custom state | Hosted Agent, escala a cero, estado |

---

## 4. Slide contract

The deck will be rebuilt from these identifiers. The code must use exactly these names.

- Projects: `UpgradeCaptain.Core`, `UpgradeCaptain.Harness`, `UpgradeCaptain.Console`, `UpgradeCaptain.Hosted`.
- `Prompt` (agent instructions constant) and `UpgradeTools.All`, with tools `list_projects`, `list_source_files`, `read_source`, `get_advisories`, `get_migration_notes`.
- `UpgradeCaptainFactory.Create(UpgradeCaptainOptions)` returns `UpgradeCaptainBuild : IAsyncDisposable` with an `Agent` property.
- `UpgradeCaptainOptions.ForLocal(repoDir)` and `UpgradeCaptainOptions.ForHosted(home)`, with properties:
  - `EnableShell`, `EnableFileAccess`
  - `CodeAct` (enum `CodeActBackend { None, Local, Hyperlight }`)
  - `EnableCustomHarness`, `TokenBudget`, `MemoryRoot`, `StateRoot`
- Custom types: `RepoVerifier`, `VerificationResult`, `VerifiedCompletionEvaluator`, `RepoStateProvider`, `CodeOwnersPolicy`, `CodeOwnerApprovalAgent`, `ApprovalStore`, `SessionVault`, `AuditLog`, `GitCheckpoints`, `BudgetGuard`.
- Shell deny list, exactly: `\brm\s+-rf\b`, `\bsudo\b`, `\bgit\s+push\b`, `\bdotnet\s+nuget\s+push\b`.
- Console:
  - flags `--context normal|tiny`, `--surface direct|codeact`, `--as <handle>`, `--state clean|after-mechanical-fix`
  - subcommands `run --job <file>`, `approvals list`, `approvals approve <id> --as <handle>`, `approvals reject <id> --as <handle> --reason <text>`
- Handles: operator `@david-lorenzo`, payments owner `@maria-payments`, hosted requester `@dependabot`.
- Script `./scripts/aspire.sh`, dashboard at `http://localhost:18888`.
- Skills:
  - `skills/nuget-upgrade/{SKILL.md, scripts/bump_package.py}`
  - `skills/teide-geo-migration/{SKILL.md, references/breaking-changes.md, assets/fix-recipes.md}`
- Invocations event types: `upgrade.requested`, `approval.decided`, `job.status`.
- Deploy commands: `azd ai agent init -m deploy/agent.manifest.yaml --deploy-mode container` and `azd deploy`.

---

## 5. Hard constraints

1. **macOS host.** Scripts are bash, run from zsh. No PowerShell.
2. **.NET 10**, C#, nullable enabled. Pin the SDK in `global.json`.
3. **Central Package Management** with exact versions (section 6.1). All `Microsoft.Agents.AI.*` packages come from the same release.
4. **Reference source.** Sparse-clone `microsoft/agent-framework` into `.ref/` (gitignored) at tag **`dotnet-1.24.0`** (commit `245d4284a5018b7f446b5eac4d3add1fa5547229`). Read these samples before writing the matching code:
   - `dotnet/samples/02-agents/Harness/BuildYourOwnClaw/Claw_Step01..04` (closest to this demo)
   - `dotnet/samples/02-agents/Harness/Harness_Step01_Research` (compaction and loop)
   - `dotnet/samples/02-agents/Harness/Harness_Step05_Loop` (loop evaluators)
   - `dotnet/samples/02-agents/Harness/Harness_Shared_Console*` (console UX to vendor)
   - `dotnet/samples/04-hosting/FoundryHostedAgents/invocations/Hosted-Invocations-EchoAgent` (Invocations host)
   - `dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-LocalCodeAct`
   - `Claw_Step04_ProductionReady/ClawAgent.Hosted` (container deploy)
5. **Hyperlight does not run on macOS** (it needs KVM on Linux x64 or WHP on Windows). CodeAct uses `LocalCodeActProvider`. On stage David says plainly that it is not a sandbox. Hyperlight is optional (Phase 10).
6. **Security on the demo machine.**
   - Shell, file access and `read_source` are confined to `demo-workspace/canarias-tours`.
   - Never point them at this repo or at `$HOME`.
   - `.env` is gitignored; `.env.example` is committed.
   - Locally, export `AZURE_TOKEN_CREDENTIALS=dev` so `DefaultAzureCredential` skips the managed identity probe.
7. **Experimental APIs.** `LoopEvaluator`, `LoopAgent`, `LoopAgentOptions`, `TodoCompletionLoopEvaluator` and the compaction members of `HarnessAgentOptions` are `[Experimental]` under `MAAI001`. Suppress `MAAI001` (and `OPENAI001` where needed) with file-local `#pragma` lines, each with a one-line comment saying why.
8. **Web search off.** Set `DisableWebSearch = true` in every harness step.
9. **Stage readability.** `Prompt`, tool descriptions and user-visible strings are in Spanish; identifiers are in English. Test the console at a 20pt terminal font.
10. **No model calls in unit tests.** Custom components are tested with fakes.

---

## 6. Verified baseline (dotnet-1.24.0, 2026-10-07)

### 6.1 Packages

Versions computed from `dotnet/nuget/nuget-package.props` at the tag. Confirm each on nuget.org in Phase 0 with `dotnet package search <id> --exact-match --prerelease --format json`.

| Package | Version | Status |
|---|---|---|
| `Microsoft.Agents.AI.Harness` | `1.24.0` | released |
| `Microsoft.Agents.AI` | `1.24.0` | released |
| `Microsoft.Agents.AI.Foundry` | `1.24.0-preview.261006.1` | preview (depends on `Azure.AI.Projects` 3.0.0-beta.3) |
| `Microsoft.Agents.AI.Tools.Shell` | `1.24.0-preview.261006.1` | preview |
| `Microsoft.Agents.AI.LocalCodeAct` | `1.24.0-preview.261006.1` | preview |
| `Microsoft.Agents.AI.Foundry.Hosting` | `1.24.0-preview.261006.1` | preview |
| `Azure.AI.AgentServer.Invocations` | `1.0.0-beta.7` or newer | beta; used by the Invocations sample |
| `Azure.Identity`, `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.Http` | match `dotnet/Directory.Packages.props` at the tag | |

### 6.2 APIs confirmed in source

Loop:
- `abstract class LoopEvaluator { ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken ct) }`, marked `[Experimental]`.
- `LoopContext` exposes `Agent`, `Session`, `Iteration`, `LastResponse`, `Feedback` and `AdditionalProperties`.
- `LoopEvaluation.Stop()`, `LoopEvaluation.Continue(string? feedback)` and `LoopEvaluation.ContinueWithMessages(...)`.
- With several evaluators, they run in order and the first that asks to re-invoke wins.
- `LoopAgent` stops and returns when a response contains a pending `ToolApprovalRequestContent`.
- `DelegateLoopEvaluator(Func<LoopContext, CancellationToken, ValueTask<LoopEvaluation>>)` also exists. We subclass instead, so the evaluator is a named, testable type.

Context providers:
- `AIContextProvider.ProvideAIContextAsync(InvokingContext, CancellationToken)` returns `AIContext { Instructions, Messages, Tools }`.
- `StateKeys` is overridable.

Approvals:
- `ToolApprovalAgentOptions.AutoApprovalRules` is `IEnumerable<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>>`.
- `ToolAutoApprovalRuleContext` exposes `FunctionCallContent`, `Agent`, `Session` and `RequestMessages`.
- Built-in rules: `FileAccessProvider.ReadOnlyToolsAutoApprovalRule`, `AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule` and `AgentSkillsProvider.AllToolsAutoApprovalRule`.
- Response helpers: `CreateResponse(approved, reason)`, `CreateAlwaysApproveToolResponse(...)` and `CreateAlwaysApproveToolWithArgumentsResponse(...)`.

File access tools:
- Read: `file_access_ls`, `file_access_read`, `file_access_read_lines`, `file_access_grep`.
- Write: `file_access_write`, `file_access_replace`, `file_access_replace_lines`, `file_access_delete`.
- The path argument is `fileName` (checked on `WriteAsync` and the delete handler); confirm it on the replace tools.

Sessions and modes:
- `AIAgent.SerializeSessionAsync(session)` returns `JsonElement`; `AIAgent.DeserializeSessionAsync(JsonElement)` restores it.
- `AIAgent.CurrentRunContext` gives the active run context.
- `AgentModeProvider.GetModeAsync(session)` and `SetModeAsync(session, mode)`, resolved via `agent.GetService<AgentModeProvider>()`.

Shell:
- `LocalShellExecutorOptions`: `Mode`, `Shell`, `ShellArgv`, `WorkingDirectory`, `ConfineWorkingDirectory`, `CleanEnvironment`, `Policy`, `Timeout`, `MaxOutputBytes`, `AcknowledgeUnsafe`.
- `ShellPolicy(denyList, allowList, custom)`.
- `LocalShellExecutor.DefaultTimeout` is 30 s. `AsAIFunction(string name = "run_shell", string? description = null, bool requireApproval = true)`.

CodeAct:
- `LocalCodeActProvider(string pythonExecutablePath, LocalCodeActProviderOptions? options)`.

Hosting over Responses:
- `AddFoundryResponses(agent, agentSessionStore?, configure?)` and `MapFoundryResponses()`.
- Tool approvals map to `mcp_approval_request` and `mcp_approval_response`.
- Sessions are persisted by `FoundryAgentSessionStore` (the Foundry durable state store when hosted), partitioned per agent and per user.
- The platform injects the `x-agent-user-id` header.

Hosting over Invocations:
- `services.AddInvocationsServer()`, an `InvocationHandler` subclass overriding `HandleAsync(HttpRequest, HttpResponse, InvocationContext, CancellationToken)`, and `app.MapInvocationsServer()`.
- Map `/readiness` yourself.
- Protocol `invocations`, version `1.0.0`. Listen on port 8088.

### 6.3 Open questions (Phase 0, each with a fallback)

| # | Question | Fallback |
|---|---|---|
| Q1 | Can one container serve both AF Responses (`AddFoundryResponses`) and Invocations (`AddInvocationsServer`)? | Invocations only. The hosted demo is webhook-driven anyway |
| Q2 | How does an Invocations call carry the session id, and does `x-agent-user-id` arrive on Invocations calls? | Job id in the payload and state in the Foundry state store (`FoundryJsonCheckpointStore` / `FoundryStateStoreBinding`) instead of `$HOME`. Approver identity from the payload, allowed only in a dev mode, and stated on stage |
| Q3 | After `DeserializeSessionAsync`, how do we produce an approval response that approval-response binding accepts for a parked request? Either find the original `ToolApprovalRequestContent` in the restored history and call `CreateResponse`, or rebuild it from stored `RequestId` and `FunctionCallContent` | Keep the session alive in memory for the local demo and record the limitation |
| Q4 | Does `dotnet build` and `dotnet test` work under `LocalShellExecutor` with the default `CleanEnvironment` (PATH, DOTNET_ROOT, HOME, NUGET_PACKAGES)? | Set the environment explicitly in the executor options |
| Q5 | Does compaction engage with the Foundry Responses client, or is history stored server-side (a real `resp_…` conversation id)? | Disable stored output, or use framework-managed history, so the compaction demo is real |
| Q6 | How long can an Invocations request run, and does the platform keep compute alive while it streams? | Return 202 quickly, run the job in the session, and poll `job.status` |

---

## 7. Repository layout

```
upgrade-captain/
  PLAN.md  README.md  global.json  Directory.Build.props  Directory.Packages.props
  nuget.config  UpgradeCaptain.slnx  .env.example  .gitignore
  src/
    UpgradeCaptain.Core/          # prompt, tools, options; factory from step-6
    UpgradeCaptain.Harness/       # step-6 custom components
    UpgradeCaptain.Console/       # Program.cs is live-coded in steps 0-5
    UpgradeCaptain.Hosted/        # step-7 Invocations host
  tests/
    UpgradeCaptain.Harness.Tests/ # xUnit, fakes only
  vendor/                         # AF harness console samples at dotnet-1.24.0 (MIT)
    README.md                     # source, tag, commit, licence, local changes
    Harness_Shared_Console/  Harness_Shared_Console_OpenAI/
    ConsoleReactiveFramework/  ConsoleReactiveComponents/
  skills/
    nuget-upgrade/  teide-geo-migration/
  demo/
    teide-geo/v1.8.0/  teide-geo/v2.0.0/      # library sources
    feed/                                     # generated .nupkg (gitignored)
    canarias-tours-template/                  # the repo the agent fixes
    reference-fix/                            # mechanical patch, never visible to the agent
    security/advisories.json
    identities.json                           # handle → Entra object id (hosted)
    jobs/upgrade-teide-geo.json
  deploy/
    Dockerfile  agent.manifest.yaml
  scripts/
    check-env.sh  pack-feed.sh  reset-demo.sh  verify-trap.sh  goto-step.sh
    prewarm.sh  aspire.sh  measure-surfaces.sh  approve.sh  hosted.sh
  .vscode/
    upgrade-captain.code-snippets  settings.json
  docs/
    architecture.md  run-of-show.md  fallbacks.md  verification-log.md
```

`.gitignore`: `bin/ obj/ .env .ref/ demo/feed/*.nupkg demo-workspace/ state/ agent-file-memory/ out/ .venv/`

---

## 8. Demo data

### 8.1 Teide.Geo, the dependency

A fake library with two versions. `scripts/pack-feed.sh` packs both into `demo/feed/`, a local folder feed. Each package carries a `RELEASE-NOTES.md`, also copied to `demo/feed/notes/<version>.md` for `get_migration_notes`.

**v1.8.0** (namespace `Teide.Geo`):

```csharp
public readonly record struct GeoPoint(double Latitude, double Longitude);
public static class GeoCalculator
{
    public static double Distance(GeoPoint a, GeoPoint b);   // kilometres, haversine
    public static double Bearing(GeoPoint a, GeoPoint b);
}
public sealed class ElevationClient
{
    public int GetElevation(GeoPoint p);                     // metres, offline lookup table
}
public static class GeoFormat
{
    public static string ToDms(GeoPoint p);
    public static GeoPoint Parse(string text);               // the "vulnerable" API
}
```

**v2.0.0** has exactly four breaking changes, documented in its release notes and in the skill's `breaking-changes.md`:

1. `GeoPoint` moves to `Teide.Geo.Primitives`. Compile error CS0246.
2. `GetElevation` is replaced by `Task<int> GetElevationAsync(GeoPoint, CancellationToken = default)`. Compile errors, and an async cascade in the API project.
3. `GeoFormat.ToDms` is removed; use `GeoPoint.ToString("dms")`. Compile errors.
4. **Behavioural, compiles fine:** `GeoCalculator.Distance` now returns **metres**. A new overload `Distance(a, b, DistanceUnit unit)` exists. Only tests catch this.

**Advisory** in `demo/security/advisories.json`: id `TEIDE-2026-0042`, package `Teide.Geo`, affected `< 2.0.0`, fixed `2.0.0`, severity high, a ReDoS in `GeoFormat.Parse`.

### 8.2 Canarias.Tours, the repo the agent fixes

A solution with CPM pinning `Teide.Geo 1.8.0`. `reset-demo.sh` writes `nuget.config` with the absolute path of the local feed plus nuget.org.

- `src/Canarias.Tours.Domain`
  - `RoutePlanner`, `Itinerary` and `PointsOfInterest` (Teide, Masca, Los Gigantes, Garachico, La Laguna, Puerto de la Cruz, Los Cristianos, Anaga)
  - uses `GeoPoint`, `Distance`, `Bearing` and `ToDms`, about 14 call sites
- `src/Canarias.Tours.Payments`
  - `FareCalculator`: `BaseFare + PerKmRate * GeoCalculator.Distance(a, b)`, in euros
  - about 5 call sites
- `src/Canarias.Tours.Api`
  - minimal API: `/routes`, `/fare`, `/elevation/{poi}`
  - uses `ElevationClient`, which forces the async cascade; about 8 call sites
- `tests/Canarias.Tours.Tests`
  - xUnit, about 20 tests, under 10 s once warm
  - fare tests assert exact euro amounts; distance tests assert kilometres
- `.github/CODEOWNERS`

  ```
  *                              @david-lorenzo
  /src/Canarias.Tours.Payments/  @maria-payments
  ```

- About 30 breaking call sites in total, plus two mentions inside comments that must not be counted as call sites.

**Acceptance** (proved by `scripts/verify-trap.sh`, which applies `demo/reference-fix/` to a scratch copy):

- On 1.8.0 the build and all tests are green.
- After bumping to 2.0.0 with only the mechanical fixes (namespace, async, `ToDms`), the build is green and **exactly the fare tests and one or two distance tests fail**, for example with a fare of 33,000 € instead of 33 €.

This is the trap the step-6 story depends on, so it must be deterministic.

### 8.3 `reset-demo.sh`

1. Rebuilds `demo-workspace/canarias-tours` from the template.
2. Runs `git init`, sets a local identity (never the global one), and creates believable history: about 8 commits and tag `v3.2.0`.
3. Adds a dummy `origin` remote.
4. Clears `state/`, `agent-file-memory/` and exported sessions.
5. Accepts `--state after-mechanical-fix`, which applies the reference mechanical patch and the 2.0.0 bump. This is the entry point for the step-6 demo.

It is idempotent and runs in under 5 s.

### 8.4 Tools: `UpgradeTools.All`

All read-only, no approval, Spanish `[Description]`s, no artificial latency.

| Tool | Returns |
|---|---|
| `list_projects()` | `[{ name, path, packageReferences[] }]`, from the csproj files and `Directory.Packages.props` |
| `list_source_files(project)` | Relative `*.cs` paths |
| `read_source(path)` | Full file content. Confined to the repo, 64 KB cap. Returning whole files is deliberate: it makes the direct-tools inventory honestly token-heavy |
| `get_advisories(package)` | Matching advisories |
| `get_migration_notes(package, fromVersion, toVersion)` | Release notes text |

---

## 9. Custom harness components (`UpgradeCaptain.Harness`)

### 9.1 `RepoVerifier` and `VerificationResult`

- **Runs, as direct processes in the repo directory** (the harness runs them, not the model's shell tool):
  - `dotnet build --nologo -clp:ErrorsOnly`
  - `dotnet test --nologo --logger "trx;LogFileName=verify.trx"`, parsing the TRX file for failing tests
  - a dependency check: resolved `Teide.Geo` version from `dotnet list package --format json`, compared with `advisories.json`
- **Limits:** 120 s timeout per command.
- **Returns** `VerificationResult`:
  - `BuildSucceeded`, `Errors` (top 10, as `file:line code message`)
  - `TestsPassed`, `FailedTests` (top 10, name and message)
  - `VulnerablePackages`, `Duration`
- **Telemetry:** span `upgrade_captain.verify` with tags `build.ok`, `tests.failed`, `vulnerable.count`.

### 9.2 `VerifiedCompletionEvaluator : LoopEvaluator`

1. Acts only in `execute` mode, read with `AgentModeProvider.GetModeAsync(context.Session)`.
2. Registered **after** `TodoCompletionLoopEvaluator`, so it runs once the todos are done.
3. Calls `RepoVerifier` and stores the result in `RepoStateCache`.
4. **Green:** calls `GitCheckpoints.CommitGreen(...)` and returns `LoopEvaluation.Stop()`.
5. **Red:** returns `LoopEvaluation.Continue(feedback)`. The feedback is Spanish and actionable, at most 2,000 characters, listing the failing tests and errors with file and line.
6. **Stuck guard:** if the same failure set repeats on 3 consecutive iterations, it stops with a "bloqueado" summary and calls `GitCheckpoints.RestoreLastGreen()`.

This is the class David live-codes on stage (about 30 lines), so keep it short and readable.

### 9.3 `RepoStateProvider : AIContextProvider`

- **Hook:** `ProvideAIContextAsync`.
- **Returns** an `Instructions` block titled `## Estado verificado del repositorio`, at most 1,500 characters, containing:
  - the resolved `Teide.Geo` version
  - the last verification status and time
  - the top 5 errors or failures
  - pending approvals
- **Why:** it is re-injected on every run, so it survives compaction.

### 9.4 `CodeOwnersPolicy`

- Parses `.github/CODEOWNERS` with GitHub semantics: the last matching pattern wins; supports `*`, `**` and directory patterns.
- `OwnersFor(paths)` returns the owners.
- `IsProtected(path)` is true when the match is not the `*` default rule.
- **Path extraction** from `FunctionCallContent`:
  - `file_access_write`, `file_access_replace`, `file_access_replace_lines`, `file_access_delete`: the `fileName` argument
  - `run_skill_script` for `bump_package.py`: `Directory.Packages.props`
  - `run_shell`: no reliable paths, so treat as non-protected and leave it to the built-in in-console approval

### 9.5 `CodeOwnerApprovalAgent : DelegatingAIAgent`

Outermost, wrapping the `HarnessAgent`.

1. **Requester per job:**
   - console: `--as` (default `@david-lorenzo`)
   - headless and hosted: `requestedBy` from the job (`@dependabot`)
2. **After every run** (`RunAsync` and `RunStreamingAsync`), collect each `ToolApprovalRequestContent`:
   - **No protected path, attended run:** pass it through. The built-in console prompt handles it.
   - **Any protected path, or unattended run:** park it.
3. **Parking:**
   - save `PendingApproval { Id, JobId, RequestId, ToolName, ArgumentsJson, Paths, Owners, RequestedBy, CreatedAt }` to `ApprovalStore`
   - save the session via `SerializeSessionAsync` to `SessionVault`
   - notify (console line locally; status and log when hosted)
   - remove the parked content from the returned response and append the text `⏸ Pendiente de aprobación de @maria-payments (APR-xxxx)`
4. **`DecideAsync(approvalId, approverId, approved, reason)`:**
   - the approver must be an owner of every protected path, **and** differ from the requester
   - otherwise refuse with a clear Spanish message and audit the refusal
   - on success: load the session from `SessionVault`, build the approval response (Q3), and continue with `RunAsync(new ChatMessage(ChatRole.User, [response]), session)`; the loop resumes and may park again
   - on rejection: send a rejected response and call `GitCheckpoints.RestoreLastGreen()`
5. **`AuditLog`:** append-only JSONL in `StateRoot`, with a SHA-256 hash chain (each entry stores the previous entry's hash).
   - events: `requested`, `parked`, `refused`, `decided`, `resumed`, `verified`, `restored`

### 9.6 `GitCheckpoints`

- Works on branch `upgrade/teide-geo-2.0`, created if missing.
- `CommitGreen(message)` runs `git add -A` and `git commit`.
- `RestoreLastGreen()` runs `git reset --hard <sha>` and `git clean -fd`, inside the repo only.
- Stores checkpoint SHAs in `StateRoot/checkpoints.json`.
- Never pushes.

### 9.7 `BudgetGuard : DelegatingChatClient`

- **Placement:** wraps the leaf `IChatClient` before `AsHarnessAgent`.
- **Counting:** adds up `Usage.TotalTokenCount` (non-streaming) and `UsageContent` (streaming), scoped by `AIAgent.CurrentRunContext?.Session`.
- **Limit:** above `TokenBudget` it throws `BudgetExceededException`. The host catches it, sets the job status to `budget_exceeded` and restores the last green checkpoint.
- **Telemetry:** counter `upgrade_captain.tokens`.

### 9.8 Tests (`UpgradeCaptain.Harness.Tests`)

Fakes only, no model calls.

- `CodeOwnersPolicy`: pattern matching and the protected-path rule.
- `VerifiedCompletionEvaluator`: green leads to `Stop`, red to `Continue`, stuck to `Stop` plus restore.
- Broker decision rules: owner check, separation of duties, rejection restore.
- `AuditLog`: hash-chain verification.
- `BudgetGuard`: a fake chat client that reports usage.

---

## 10. Phases

### Phase 0 · Environment, pins and API check (stop after)

1. **`scripts/check-env.sh`** prints and checks:
   - .NET 10 SDK, git, python3 3.11+, Docker running
   - `az` logged in, `azd` with the `azure.ai.agents` extension
   - the CPU architecture
2. **Model smoke test:** read `FOUNDRY_PROJECT_ENDPOINT` and `FOUNDRY_MODEL` from `.env` (default `gpt-5.4`, fallback `gpt-5.4-mini`) and make a 1-token call.
3. **Pins:** confirm every version in 6.1 on nuget.org and record them in `docs/verification-log.md`.
4. **Reference source:** clone `.ref/` at `dotnet-1.24.0` and record the commit.
5. **API check:** re-confirm every API in 6.2 and record any drift.
6. **Open questions:** resolve what can be resolved locally:
   - Q1: a throwaway web app registering both hosts, run locally
   - Q3: read the approval binding code
   - Q4: run `dotnet build` through `LocalShellExecutor`

   Leave Q2, Q5 and Q6 for the phases that can test them.

**Report:** versions, drift, answers to Q1, Q3 and Q4, and blockers.

### Phase 1 · Scaffold and demo data (stop after)

1. Create the layout from section 7: solution, props files, `nuget.config`, `.env.example`, and the Core and Console projects.
2. Vendor the harness console projects from `.ref/`. Switch their `ProjectReference`s into `PackageReference`s at the pinned version. Keep the headers, record the source in `vendor/README.md`, and change nothing else unless the build requires it.
3. Build `Teide.Geo` 1.8.0 and 2.0.0, `pack-feed.sh`, the `Canarias.Tours` template, the advisory, CODEOWNERS, `reference-fix/`, `reset-demo.sh` and `verify-trap.sh`.
4. **Acceptance:**
   - `dotnet build` is clean.
   - `reset-demo.sh` produces a green repo on 1.8.0.
   - `verify-trap.sh` shows a green build with exactly the expected red tests after the mechanical fix.

### Phase 2 · `step-0` and `step-1` (stop after)

**`step-0`.** Build the chat client like the Claw samples:

```
AIProjectClient → GetProjectOpenAIClient() → GetResponsesClient() → AsIChatClient(FOUNDRY_MODEL)
```

Wrap it with `chatClient.AsAIAgent(new ChatClientAgentOptions { ChatOptions = new() { Instructions = Prompt } })` and add a plain `Console.ReadLine` loop that calls `RunAsync(input)` with **no session**. Commit and tag `step-0`.

**`step-1`.** `chatClient.AsHarnessAgent(new HarnessAgentOptions { ... })` with:

- `ChatOptions.Instructions = Prompt`, `ChatOptions.Tools = UpgradeTools.All`
- `DisableWebSearch = true`
- `AgentModeProviderOptions.DefaultMode = "execute"` (planning arrives in step 2)
- `FileMemoryStore = new FileSystemAgentFileStore(<repo>/agent-file-memory)`
- `OpenTelemetrySourceName = "UpgradeCaptain"`

Run it through the vendored `HarnessConsole.RunAgentAsync`, with `BuildObserversWithPlanning` (passing the context window values) and `BuildDefaultCommandHandlers`.

Add telemetry plumbing: tracer and meter providers on `UpgradeCaptain`, HttpClient instrumentation and an OTLP exporter. Turn them on **only** when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Never add a console exporter.

Add `--context normal|tiny`. Calibrate `tiny` so that reading about 10 source files visibly compacts, and record the numbers.

**Verify and log:**

- "Haz inventario de dónde usamos Teide.Geo" produces several tool calls in one turn.
- `/session-export`, `/exit`, relaunch, `/session-import` restores history.
- Compaction really happens (Q5).

Commit and tag `step-1`.

### Phase 3 · `step-2` (stop after)

1. Set `DefaultMode = "plan"`.
2. Add:
   - `LoopEvaluators = [new TodoCompletionLoopEvaluator(new() { Modes = ["execute"] })]`
   - `LoopAgentOptions = new() { MaxIterations = 10 }`
3. **Verify:**
   - "Prepara la actualización de Teide.Geo a 2.0" produces todos and a question.
   - The mode does not change until told.
   - `/todos` and `/mode` work.
   - After "adelante, pasa a execute" the loop runs until the todos are done or the cap is hit.

Commit and tag `step-2`.

### Phase 4 · `step-3` (stop after)

1. **Shell:** `LocalShellExecutor` with:
   - `WorkingDirectory = repoDir`, `ConfineWorkingDirectory = true`, `Timeout = 120 s`
   - the deny list from section 4
   - registered as `shell.AsAIFunction(requireApproval: true)` plus `new ShellEnvironmentProvider(shell)`
   - disposed with `await using`
   - environment fixed if Q4 required it
2. **File access:** `FileAccessStore = new FileSystemAgentFileStore(repoDir)`.
3. **Approvals:** `ToolApprovalAgentOptions.AutoApprovalRules = [FileAccessProvider.ReadOnlyToolsAutoApprovalRule]`.
4. **Verify the stage script:**
   - `dotnet build` prompts; choose "Always approve this tool with these arguments", never "any arguments" on `run_shell`.
   - A `file_access_replace` prompts.
   - Denying an edit makes the agent adapt.
   - `git push` and `dotnet nuget push` are blocked by the policy. Capture the exact console output.

Commit and tag `step-3`.

### Phase 5 · `step-4` CodeAct (stop after)

1. **`--surface direct|codeact`**, default `direct`. `codeact` moves `UpgradeTools.All` into `LocalCodeActProviderOptions.Tools` and adds `new LocalCodeActProvider(venvPython, options)`.
   - `ExecutionLimits.TimeoutSeconds = 20`
   - a dedicated `.venv/` with its python path passed explicitly
   - an extra instruction: prefer one `execute_code` block using `await call_tool(...)`
2. **`scripts/measure-surfaces.sh`** runs this prompt 3 times per surface through a headless entry point:

   > Haz inventario de las llamadas a APIs de Teide.Geo que rompen en 2.0: proyecto, fichero, línea y cambio necesario

   For each run it records model calls (`chat` spans), input and output tokens, and wall time, in the verification log.
3. **Parallel calls:** record whether the model parallelises `read_source` on the direct surface. Never disable parallel calls to inflate the gap.
4. **Acceptance:**
   - Both surfaces find the same call sites and ignore the two comment mentions.
   - The CodeAct run uses one or two `execute_code` calls.
   - Numbers are rehearsal reference only and never go on slides.

Commit and tag `step-4`.

### Phase 6 · `step-5` skills and telemetry (stop after)

1. **Skills**, each with valid frontmatter (the name matches the folder; the description says when to use it):
   - `nuget-upgrade`
     - covers the company playbook: CPM only, never prereleases, branch `upgrade/<package>-<version>`, commit format, report template
     - `scripts/bump_package.py <propsPath> <package> <version>` edits `Directory.Packages.props` deterministically; `SKILL.md` documents the JSON-array argument format
   - `teide-geo-migration`
     - `references/breaking-changes.md` lists the four changes, stating the units change explicitly
     - `assets/fix-recipes.md` gives before and after snippets
2. **Wiring:**
   - `AgentSkillsSource = new AgentFileSkillsSource([skillsDir], runner)`, using the vendored `SubprocessScriptRunner`
   - add `AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule`, so `run_skill_script` still prompts
   - copy `skills/` to the output directory
3. **`scripts/aspire.sh`:** runs `mcr.microsoft.com/dotnet/aspire-dashboard` in Docker, with the UI on 18888, OTLP gRPC on 4317 and anonymous access. Idempotent.
4. **Verify:**
   - "¿Qué cambia en Teide.Geo 2.0 y cómo lo arreglamos?" loads `teide-geo-migration` and reads `breaking-changes.md`.
   - "Sube Teide.Geo a 2.0.0" loads `nuget-upgrade` and prompts for `bump_package.py`.
   - Traces show `invoke_agent`, `chat` and `execute_tool` spans with token attributes.
   - Prompt content appears only with `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true`.

Commit and tag `step-5`.

### Phase 7 · `step-6` the custom harness (stop after 7a, and again after 7b)

**7a. Components and tests**

1. **Factory refactor:** move agent construction into `UpgradeCaptain.Core`, so `UpgradeCaptainFactory.Create(UpgradeCaptainOptions)` returns `UpgradeCaptainBuild`, which owns the shell executor, CodeAct provider and custom services.
   - `ForLocal(repoDir)` has everything on and `EnableCustomHarness = true`.
   - With `EnableCustomHarness = false`, behaviour is identical to `step-5`; a test proves it by comparing the options produced.
2. **Components:** implement section 9 in `UpgradeCaptain.Harness`, with its tests. Resolve Q3 here.
3. **Wiring in the factory:**
   - `BudgetGuard` around the chat client
   - `RepoStateProvider` in `AIContextProviders`
   - `VerifiedCompletionEvaluator` second in `LoopEvaluators`
   - `CodeOwnerApprovalAgent` wrapping the result

**Report** after 7a: components, test results, the Q3 answer.

**7b. Console UX and the stage beat**

1. Add `--as`, `--state` and the `approvals` subcommands. `scripts/approve.sh` wraps `approvals approve|reject` for "María's laptop", a second terminal.
2. Add `run --job demo/jobs/upgrade-teide-geo.json` for headless, unattended runs, which always park approvals.
3. **Rehearse the wall and the fix, and make it reliable:**
   1. `goto-step.sh 5`, then `reset-demo.sh --state after-mechanical-fix`. Prompt: "Termina la actualización y ciérrala. No ejecutes los tests, ya los pasará CI." The agent declares it done. In a plain terminal, `dotnet test` shows the fare tests red, with 33,000 € instead of 33 €.
   2. `goto-step.sh 6`, same state, same prompt:
      - the evaluator runs the tests anyway and feeds the failures back
      - the agent edits `FareCalculator` in Payments, which parks for `@maria-payments`
      - `approve.sh APR-x --as @david-lorenzo` is refused (not an owner)
      - `approve.sh APR-x --as @maria-payments` resumes the run
      - the evaluator goes green and a checkpoint commit appears
   3. Show the audit log with the hash chain verified, and the `upgrade_captain.verify` span in Aspire.
   4. Rehearse at least 5 times. Tune the `Prompt` and evaluator feedback until the beat succeeds 5 times out of 5, and log every run.
4. **Snippet** `uc6-evaluator` types the evaluator; the other components are revealed, not typed.

Commit and tag `step-6`.

### Phase 8 · `step-7` Hosted Agent (ask before Azure; stop after)

1. **`UpgradeCaptain.Hosted`:** an ASP.NET app with `AddInvocationsServer()`, an `UpgradeInvocationHandler : InvocationHandler`, `/readiness` and `MapInvocationsServer()`, on port 8088. Add Responses alongside only if Q1 says yes.
2. **Events.** Every response is JSON: `{ jobId, status, pendingApprovals[], lastVerification, reportPath }`, where `status` is one of `running`, `waiting_approval`, `green`, `blocked`, `budget_exceeded`.

   ```json
   { "type": "upgrade.requested", "jobId": "job-001", "package": "Teide.Geo", "from": "1.8.0", "to": "2.0.0", "advisory": "TEIDE-2026-0042", "requestedBy": "@dependabot" }
   { "type": "approval.decided", "jobId": "job-001", "approvalId": "APR-…", "decision": "approve", "reason": "Revisado: tarifa en km" }
   { "type": "job.status", "jobId": "job-001" }
   ```

3. **Approver identity:** taken from `x-agent-user-id`, mapped through `demo/identities.json`. A payload `approver` field is accepted only when `UPGRADE_CAPTAIN_DEV_IDENTITY=1`; say so on stage if the fallback is used (Q2).
4. **`ForHosted(home)`:**
   - shell and file access stay **on** but are confined to `$HOME/work/canarias-tours`; the shell gets an allow list (`dotnet`, `git status|diff|log|add|commit`)
   - `CodeAct = Local` (the per-session VM sandbox is the boundary)
   - `MemoryRoot = $HOME/memory`, `StateRoot = $HOME/state`
   - skills use `AllToolsAutoApprovalRule` (trusted, packaged)

   This deliberately diverges from Microsoft's Claw hosted sample, which turns shell off. Justify it in `docs/architecture.md`: per-session VM isolation, an allow list, confinement, the approval gate and the audit log.
5. **State layout:** on the first `upgrade.requested`, copy `/opt/demo/canarias-tours` to `$HOME/work/canarias-tours`. Sessions, approvals, checkpoints and the audit log live under `$HOME/state` (or the Q2 fallback).
6. **Image (`deploy/Dockerfile`, built from the repo root):**
   - base `mcr.microsoft.com/dotnet/sdk:10.0` (the sandbox must build and test), plus python3 and git
   - the feed and template under `/opt/demo`
   - `NUGET_PACKAGES=/opt/nuget`, pre-restored for both 1.8.0 and 2.0.0 so the sandbox works offline
   - `ASPNETCORE_URLS=http://+:8088`
7. **`deploy/agent.manifest.yaml`:**
   - kind `hosted`, protocol `invocations` `1.0.0`
   - `cpu "2.0"`, `memory 4Gi` (build and test need it)
   - env `AZURE_AI_MODEL_DEPLOYMENT_NAME`
8. **Deploy:**
   - ask David for the subscription, resource group, project and region (Spain Central and Sweden Central support hosted agents)
   - `azd ai agent init -m deploy/agent.manifest.yaml --deploy-mode container`, with a remote ACR build
   - the project's managed identity needs `AcrPull` on the registry
   - give the agent identity no Azure roles beyond the defaults
   - set the session idle timeout to the minimum, 2 minutes
9. **`scripts/hosted.sh trigger|approve|status`** wraps the calls with a token from `az account get-access-token`. Resolve the endpoint path and session-id mechanism in this phase (Q2, Q6).
10. **Verify:**
    - the image runs locally with `curl -i localhost:8088/readiness`
    - the full flow runs in Foundry: trigger, park, idle past the timeout, approve, resume on new compute with `$HOME` restored, green, `UPGRADE_REPORT.md` written
    - traces appear in Application Insights with no exporter code

Commit and tag `step-7`.

### Phase 9 · Rehearsal tooling (stop after)

1. **`goto-step.sh N`:** refuses to run with uncommitted changes unless `--force`; checks out `step-N`, resets the demo and builds.
2. **Live branch:** `live` starts at `step-0`; on stage David types the diffs and falls back to `goto-step.sh N` if anything breaks.
3. **Snippets** in `.vscode/upgrade-captain.code-snippets`; each produces code identical to its tag:

   | Prefix | Diff |
   |---|---|
   | `uc1-harness` | step-0 → step-1 |
   | `uc2-plan` | mode + loop |
   | `uc3-shell` | executor + file access + rules |
   | `uc4-codeact` | CodeAct provider |
   | `uc5-skills` | skills source + rule |
   | `uc6-evaluator` | `VerifiedCompletionEvaluator` + one wiring line |

4. **`prewarm.sh`:**
   - builds every tag
   - restores packages and warms `dotnet test` in the workspace
   - makes a 1-token model call
   - starts Aspire
   - checks hosted readiness and that a parked job exists for the hosted beat
   - prints a green/red checklist
5. **Docs:** write `docs/run-of-show.md` (section 11) and `docs/fallbacks.md` (section 12).

### Phase 10 · Optional Hyperlight backup

On a Linux x64 VM with KVM, run a variant with `CodeAct = Hyperlight`, using `HyperlightCodeActProvider(HyperlightCodeActProviderOptions.CreateForWasm(PythonGuestModule.GetModulePath()))`. Record a 60-second capture for the CodeAct slide. Not part of the live demo.

---

## 11. Run of show (50-minute slot, about 47 minutes of content)

| Time | Slides | Demo |
|---|---|---|
| 0:00 | Opening → map of steps | About 8 min: the problem, harness anatomy, built-in vs opt-in, the Upgrade Captain scenario |
| 8:00 | Steps 0–1 | 6 min. "Actualiza Teide.Geo a 2.0 en Canarias.Tours" (plain agent can only explain). After the diff, "Haz inventario de dónde usamos Teide.Geo". Session export and import. `--context tiny` while reading files |
| 14:00 | Step 2 | 5 min. "Prepara la actualización de Teide.Geo a 2.0". Correct the plan out loud ("el informe lo escribo yo, quítalo"), `/todos`, then "adelante, pasa a execute" |
| 19:00 | Step 3 | 6 min. `dotnet build` (always approve with these arguments), an approved edit, a denied edit, `git push` blocked by the policy |
| 25:00 | Steps 4–5 | 7 min. Aspire up. The inventory prompt on `direct`, then `codeact`, compared in the dashboard. The migration skill loads on demand; `bump_package.py` prompts |
| 32:00 | Step 6 | 9 min. Slide "El muro". The wall beat on `step-5`, live-code the evaluator, reveal the rest, then the fix beat: park for María, David refused, María approves, green, checkpoint, audit |
| 41:00 | Step 7 | 5 min. Job triggered before the talk is parked and its session idled. `hosted.sh status` shows `waiting_approval`. `hosted.sh approve` resumes on new compute with `$HOME` intact, then green and the report. Trace in App Insights |
| 46:00 | Close | Harness vs your own loop, takeaways, resources, questions |

---

## 12. Fallbacks (write into `docs/fallbacks.md`)

- **Video per block.** Record every block at the final rehearsal (1080p, 20pt terminal). Keep copies on the desktop and on a USB stick.
- **Network.** Phone hotspot first. Keep a second model deployment in another region, switchable with `.env.alt`.
- **Hosted.** Deploy at least 24 hours before the talk. Trigger the stage job at least 30 minutes before the session. If Foundry fails, run `UpgradeCaptain.Hosted` locally against `localhost:8088` and use `hosted.sh --local`.
- **Step 6.** If the model goes off script, run `reset-demo.sh --state after-mechanical-fix` and retry once, then switch to the video.
- **CodeAct.** Show the measurement log and the video.

---

## 13. Definition of done

- [ ] All 8 tags build and run from a clean clone after `pack-feed.sh` and `reset-demo.sh`.
- [ ] `verify-trap.sh` passes. The custom harness tests pass.
- [ ] Every identifier in the slide contract matches the code.
- [ ] `docs/verification-log.md` records:
  - versions, the reference tag and commit
  - answers to Q1–Q6
  - compaction proof
  - surface measurements
  - the policy block output
  - 5/5 runs of the step-6 beat
  - the hosted resume result
  - all API drift
- [ ] `prewarm.sh` is green on the stage laptop.
- [ ] The run of show has been rehearsed end to end at least twice within 47 minutes.

---

## 14. Deck impact (for the deck rebuild after the code is verified)

| Slide | Change |
|---|---|
| 8 | New scenario and an 8-step map |
| 9, 12, 15, 20 | Demo dividers with the new prompts and console mocks |
| 16 | CodeAct example becomes the call-site inventory |
| 18 | The two new skills |
| New, before step 6 | **"El muro"**: the four-row table from section 1 |
| New | **"Tu harness a medida"**: the architecture from section 2 |
| New | The evaluator code |
| 21–22 | Invocations host, `ForHosted(home)`, the parked-job flow |
| 23 | Updated with the custom harness argument |
