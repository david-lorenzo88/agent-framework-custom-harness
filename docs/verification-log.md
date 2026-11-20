# Verification log

What has been verified against reality, what differs from `PLAN.md`, and what is still open.
Rule from the plan: *if reality disagrees with the plan, reality wins*. Every difference is recorded here as **D-xx**, together with the smallest change that was made.

Last update: **2026-10-13** (initial build, Phases 0–9 without model or Azure access).

---

## 1. Environment (Phase 0)

| Item | Value | Status |
|---|---|---|
| Machine | macOS 26.6.2, arm64 (Apple Silicon) | ✔ |
| .NET SDK | 10.0.107 (`global.json` pins `10.0.107`, `rollForward: latestFeature`) | ✔ The AF repo itself pins 10.0.401. Consuming the 1.24.0 packages with 10.0.107 works. |
| git | 2.54.0 | ✔ |
| python3 | 3.13.14, **only for CodeAct** (step-4): `.venv/` is created by `scripts/setup-venv.sh`. No project code or script is Python (D-25) | ✔ |
| Docker | 29.5.2 | ✔ Aspire Dashboard runs; the hosted image builds and runs |
| az | logged in as `<work-account>`. The **default context is a tenant-level account with no subscription** | ⚠ Pick a subscription before Phase 8 (`az account set`) |
| azd | 1.27.0 (1.35.1 available). Extension `azure.ai.agents` **not installed** | ⚠ Phase 8: `azd extension install azure.ai.agents` |
| `.env` | **missing**: no `FOUNDRY_PROJECT_ENDPOINT` yet | ❌ Blocks every model-dependent check |

`scripts/check-env.sh` checks all of the above. `scripts/check-env.sh --model` adds the 1-token model call (`scripts/smoke-model.sh`).

## 2. Pins (Phase 0, confirmed on nuget.org on 2026-10-13)

| Package | Version | Notes |
|---|---|---|
| `Microsoft.Agents.AI.Harness` | `1.24.0` | released |
| `Microsoft.Agents.AI` | `1.24.0` | released |
| `Microsoft.Agents.AI.Foundry` | `1.24.0-preview.261006.1` | |
| `Microsoft.Agents.AI.Tools.Shell` | `1.24.0-preview.261006.1` | |
| `Microsoft.Agents.AI.LocalCodeAct` | `1.24.0-preview.261006.1` | |
| `Microsoft.Agents.AI.Foundry.Hosting` | `1.24.0-preview.261006.1` | used only in the Q1 experiment |
| `Azure.AI.AgentServer.Invocations` | `1.0.0-beta.7` | latest; the AF repo pins beta.6. Both resolve `Azure.AI.AgentServer.Core` beta.29 |
| `Azure.AI.Projects` / `Azure.AI.Extensions.OpenAI` | `3.0.0-beta.3` | `Extensions.OpenAI` is pinned explicitly, as AF does (NU1603) |
| `Azure.Identity` | `1.21.0` | |
| `OpenTelemetry`, `.Exporter.OpenTelemetryProtocol`, `.Instrumentation.Http` | `1.18.0` | |
| `Microsoft.Extensions.AI` | `10.10.0` | the vendored console needs it |
| `OpenAI` | `2.14.0` | the vendored console needs it |
| Test stack | `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.5`, `Microsoft.NET.Test.Sdk 18.10.0` | see D-24 |

## 3. Reference source

`.ref/af` is a sparse clone (`dotnet/` only) of `microsoft/agent-framework` at tag **`dotnet-1.24.0`**: annotated tag object `f74d6b56…`, commit **`245d4284a5018b7f446b5eac4d3add1fa5547229`**. It matches the plan.

Evidence reports produced while checking the source:
- [`evidence/api-check.md`](evidence/api-check.md): every API in PLAN §6.2, with file:line references.
- [`evidence/q3-approval-binding.md`](evidence/q3-approval-binding.md): how approval responses are bound, with probes.
- [`evidence/q1-dualhost.md`](evidence/q1-dualhost.md): Responses + Invocations in one app.

## 4. Open questions

| # | Status | Answer |
|---|---|---|
| **Q1** | ✔ **Resolved locally** | **Yes.** One app can register both `AddFoundryResponses` + `MapFoundryResponses` and `AddInvocationsServer` + `MapInvocationsServer`. No route collisions; `MapFoundryResponses` maps `/readiness` itself. Not yet tested: whether Foundry accepts one agent that declares both protocols. **Decision: Invocations only**, as the plan's fallback says (the hosted demo is webhook-driven). |
| **Q2** | ✔ **Resolved by docs and SDK (2026-10-13); live confirmation comes with the deploy** · Session: Foundry reads `agent_session_id` **only from the query string**. Identity: `x-agent-user-id` arrives on container protocol 2.0.0 (D-39), and sessions are isolated per caller identity. Earlier notes: | `InvocationContext.SessionId` comes from the `agent_session_id` **query parameter**, then the `FOUNDRY_AGENT_SESSION_ID` env var, then a new UUID. An `x-agent-session-id` *request* header is ignored. The user id is `InvocationContext.PlatformContext.UserIdKey`, read from `x-agent-user-id`. Implementation: the job id travels in the payload and all state lives in `$HOME/state`. `hosted.sh` sends `agent_session_id=<jobId>`. The approver comes from `x-agent-user-id`, mapped through `demo/identities.json`; the payload `approver` field is accepted only when `UPGRADE_CAPTAIN_DEV_IDENTITY=1`. **Still to check in Foundry:** that the platform injects `x-agent-user-id` on Invocations calls, and which session-id format it accepts. |
| **Q3** | ✔ **Resolved and tested** | Option (b): when parking, store the original `ToolApprovalRequestContent` (serialized with `AIJsonUtilities.DefaultOptions`) together with the session JSON. To resume, call `DeserializeSessionAsync` → `request.CreateResponse(approved, reason)` → `RunAsync(new ChatMessage(User, [response]), session)`. Option (a) does not work: with per-service-call persistence the request never reaches the history. The approval-binding record lives in `session.StateBag` and survives serialization. **Caveat:** any run on a parked session wipes that record, so `CodeOwnerApprovalAgent` blocks the session while a batch is open. Tested end to end with a *new* agent instance resuming from disk (`CodeOwnerApprovalAgentTests.OwnerApproves_ResumesFromSessionVault_InAFreshAgent`). |
| **Q4** | ✔ **Resolved** | `dotnet build` and `dotnet test` work through `LocalShellExecutor` with `CleanEnvironment = true`, which keeps only PATH, HOME and USER (Homebrew dotnet is on PATH). **Drift:** the default is `CleanEnvironment = false`, which inherits the whole parent environment, Azure and Foundry variables included (D-05). We set it to `true` and add `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO` and, in the container, `NUGET_PACKAGES`. |
| **Q5** | ✔ **Resolved, with live proof** (§10 "Compaction proof": 12001 → 4486 input tokens with `--context tiny`) | A plain `.GetResponsesClient().AsIChatClient(model)` leaves `store` on. The service then returns a conversation id, the harness drops its local history, and client-side compaction has no effect. Steps 1+ use `GetProjectResponsesClientForModel(model).AsIChatClientWithStoredOutputDisabled(model)` (D-08). Compaction itself has a bug in 1.24.0 (D-07). **Pending:** a live run with `--context tiny` that shows compaction in the usage line and in the `upgrade_captain.compaction` span. |
| **Q6** | ✔ **Resolved in Foundry (2026-10-13)**: 202 + `job.status` polling works; the session idles and resumes on the next call with `$HOME` intact (§9) · Original design: | Design follows the plan's fallback. `upgrade.requested` and `approval.decided` answer **202** right away and the run continues inside the session; `job.status` polls. `"wait": true` makes the call synchronous for local testing. Still to measure: request time limits and whether compute stays alive while a background run is in flight. |

## 5. API drift and design differences (D-xx)

| # | Plan said | Reality at 1.24.0 | Change made |
|---|---|---|---|
| D-01 | `LoopContext.Feedback` | `IReadOnlyList<string?>` (one entry per re-invocation). `Iteration` is 1 at the first evaluation. Also has `InitialMessages` and `RunOptions` | None needed; the evaluator does not read them |
| D-02 | Evaluator "stops with a *bloqueado* summary" | `LoopEvaluation.Stop()` has no message overload | The evaluator stops and restores. **`CodeOwnerApprovalAgent` adds the harness verdict line** (`✅ Verde verificado…`, `⛔ Bloqueado…`, `❌ …`) when a verification ran during the run |
| D-03 | `ProvideAIContextAsync(InvokingContext, ct)` | `protected virtual`; `InvokingContext` is nested in `AIContextProvider` | `RepoStateProvider` overrides it |
| D-04 | `CreateResponse(approved, reason)` | An **MEAI instance method** on `ToolApprovalRequestContent`. The call is in `ToolCall` (a `ToolCallContent`); there is no `FunctionCall` property | Used as is |
| D-05 | Shell defaults | `CleanEnvironment` defaults to **false** (inherits secrets). `Timeout` defaults to **null** (no timeout; `DefaultTimeout` is only a recommendation) | `CleanEnvironment = true`, `Timeout = 120 s` |
| D-06 | — | `execute_code` says it returns "the value of a top-level `result` variable", but `runner.py` returns the **value of the last expression** | `Prompts.CodeActHint` tells the model to end the block with a line that is just `result` |
| **D-07** | Built-in compaction via `MaxContextWindowTokens` + `MaxOutputTokens` | **Bug.** `CompactionProvider` keeps an index between calls. `CompactionMessageIndex.Update` returns early when the last message equals the last processed message. `TodoProvider` appends an identical `### Current todo list` message at the end of every run, so **new messages before it are silently dropped**: the 2nd user turn (when the 1st used no tools), LoopAgent feedback, and the result of a tool after it was approved. Reproduced in `CompactionTests.BuiltInCompaction_1_24_0_DropsTheSecondTurn_KnownBug` | **`CompactingChatClient`** (Core, ~40 lines) applies the *same built-in* `ContextWindowCompactionStrategy` on every model call through the static `CompactionProvider.CompactAsync`, with a fresh index each time. The window values are no longer set in `HarnessAgentOptions`. Upstream issue draft: [`upstream-issue-compaction.md`](upstream-issue-compaction.md). **When the known-bug test starts failing after an AF upgrade, remove the workaround.** |
| D-08 | `GetResponsesClient().AsIChatClient(model)` | Server-side storage on, so compaction has no effect (Q5) | Step 0 keeps the plan's chain; steps 1+ use `GetProjectResponsesClientForModel(model).AsIChatClientWithStoredOutputDisabled(model)` |
| D-09 | Types in `Microsoft.Agents.AI.Harness` | Loop, approval, todo, mode, file-access and skills types live in **`Microsoft.Agents.AI`**. Harness has only `HarnessAgent`, its options and `AsHarnessAgent` | `UpgradeCaptain.Harness` references `Microsoft.Agents.AI` |
| D-10 | Skills wiring | `AgentSkillsProviderBuilder` *requires* a runner. `AgentFileSkillsSource(paths, runner)` works as the plan says | As planned |
| D-11 | Batch parking | `ToolApprovalAgent` surfaces **one approval at a time** and queues the rest | If the model edits two Payments files in one turn, María sees **APR-0001 and then APR-0002** (rehearse this; see run of show) |
| D-12 | — | "Always approve this tool (any arguments)" creates a standing rule *inside* the harness that would auto-approve later protected edits | `CodeOwnerApprovalAgent` downgrades "always approve" responses for tools that touch files to a one-time approval, and says so in the console |
| D-13 | `git push` "blocked by the policy" | The policy runs **after** human approval. The console asks first; after "Approve" the tool returns `Command rejected by policy: matched deny pattern: \bgit\s+push\b` | Stage point: the deny list still applies when a human says yes |
| D-14 | Invocations session id | See Q2 | `hosted.sh` passes `agent_session_id` |
| D-15 | — | The Invocations SDK does not map `/readiness`; without it Foundry answers 424 | Mapped in `Program.cs` (as the plan says) |
| D-16 | Hosted: "unattended run → park" every approval | Parking every Domain edit and every `dotnet build` for a human makes the hosted beat need many approvals | `ForHosted` sets `AutoApproveUnprotected`: an auto-approval rule approves `run_shell` (already limited by the allow list) and writes to **non-protected** paths. Only protected paths reach `CodeOwnerApprovalAgent` and park for their owner. Justified in `architecture.md` |
| D-17 | "Approver must own every protected path" | — | Stricter: the approver must own **every touched path** (CODEOWNERS last match) and differ from the requester. Both reasons are reported together |
| D-18 | Resume after `approvals approve` | — | In the console, `CodeOwnerApprovalAgent` **waits inside the same run** and continues on David's screen when María decides from her terminal. `DecideAsync` resumes in its own process only when no live console is waiting (PID check), e.g. after `run --job` |
| D-19 | `--state` flag | — | `--state X` runs `reset-demo.sh --state X` before starting. It does not change the agent mode: type `/mode execute` (both beats behave the same) |
| D-20 | — | `LoopAgent` restarts its iteration count on each resume after an approval | Stuck detection lives in `RepoStateCache`, so it spans resumes |
| D-21 | Tags `step-0 … step-7` | — | Added **`step-6-pre`** (step-6 without the evaluator) so `uc6-evaluator` can be typed on stage on a tree that builds |
| D-22 | — | `FileMemoryProvider` uses a random working folder per session (`agent-file-memory/<timestamp>_<guid>`) | None; `/session-export` keeps the session id |
| D-23 | `.env` handling | — | `DotEnv.Load()` in Core, called before `DefaultAzureCredential`, which reads `AZURE_TOKEN_CREDENTIALS` |
| D-24 | — | — | xUnit 2.9.3 on VSTest for both the demo repo and the harness tests: `dotnet test --logger trx` works without Microsoft.Testing.Platform configuration |
| D-25 | `skills/nuget-upgrade/scripts/bump_package.py`, run by the Claw sample's `SubprocessScriptRunner` | Requirement from David (2026-10-13): **no Python** except Agent Framework's CodeAct, which only runs Python | The skill script is **`bump_package.cs`**, a .NET 10 file-based app. **`DotnetScriptRunner`** (Core) runs it with `dotnet run script.cs -- args`, using the repo as working directory and a clean environment (no `FOUNDRY_*`/`AZURE_*`). It takes about 1.3 s the first time and about 0.15 s once cached; `prewarm.sh` and the Dockerfile warm it. `AgentFileSkillsSource` discovers `.cs` scripts by default. The rehearsal helpers are .NET too (`scripts/trx-summary.cs`, `scripts/apply-unit-fix.cs`), and the bash scripts no longer call `python3`. Covered by `SkillScriptTests` (integration). Python remains **only** for CodeAct (step-4, and `CodeAct = Local` in the hosted container) |
| D-26 | `DefaultAzureCredential` with `AZURE_TOKEN_CREDENTIALS=dev` | The Foundry project `<foundry-resource>/<project>` (Sweden Central, `<resource-group>`, subscription `<subscription-id>`) belongs to **<personal-account>** (tenant `<tenant-id>`). The az CLI default was <work-account>, which got **HTTP 403** ("does not have permissions for …/agents/write"). No role assignment was needed | With David's OK: `az account set --subscription <subscription-id>` (local CLI setting only). `smoke-model.sh` now explains this case when it gets a 403. **Stage laptop: check `az account show` before the talk** |
| D-27 | `Prompt` said «Nunca publiques nada: ni git push ni dotnet nuget push» | With that line, gpt-5.4 refuses `git push` on its own, so the deny list is never shown | Line removed from `Prompts.Prompt`; the `nuget-upgrade` playbook still says it. The demo shows the **policy** stopping it after a human approves |
| D-28 | `--context tiny` = 24,000 / 12,000 | Compaction only kicks in on the 2nd turn, with no visible drop | `tiny` = **16,000 / 8,000**: the drop is visible in the 1st turn (12,001 → 4,486 input tokens) |
| D-29 | Stage prompts | «Prepara la actualización de Teide.Geo a 2.0» does not produce a report task (nothing to remove with «el informe lo escribo yo»). «Sube los cambios con git push» → "nothing to push" | Stage prompts: «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido» and «Ejecuta git push origin main» (+ «Sí, ejecútalo» if it asks) |
| D-30 | `git worktree` builds under `$TMPDIR` | On macOS `$TMPDIR` is under `/var/folders`, a symlink to `/private/var/folders`. NuGet restore then records `"projectReferences": {}` and transitive packages (OpenTelemetry) are not copied, which crashes at runtime. Builds in the repo itself (`goto-step.sh`) are not affected | `measure-surfaces.sh` and `prewarm.sh` resolve the worktree path with `pwd -P` |
| **D-31** | Switching to `execute` from code or from the console (`AgentModeProvider.SetModeAsync`) | **Azure OpenAI's content filter rejects the request** (HTTP 400 `content_filter`, "Parameter: prompt") because of the notification `AgentModeProvider` injects: «[Mode changed: … You must now adjust your behavior to match the "execute" mode.]». Deterministic: 7/7 blocked with it, OK without it. This affects headless `--prompt`, `run --job`, the hosted agent, console `/mode execute` and the console's «Approve and switch to execute mode» | `SetModeAsync(…, disableNotification: true)` in our three call sites, plus a one-line local change in each of the two vendored console call sites (`vendor/README.md`). The provider's instructions already state the current mode on every call. Alternative, not applied: relax Prompt Shields on the deployment's content filter (Azure change, David's call). Worth reporting upstream |
| **D-32** | CodeAct (`LocalCodeActProvider`) when the generated Python fails | **The model never sees its own error.** Any validation error, Python exception or timeout reaches the harness as an `InvalidOperationException` (`ProcessBridge`). `HarnessAgent` builds its `FunctionInvokingChatClient` with `IncludeDetailedErrors = false`, and `HarnessAgentOptions` has no option to change it, so the tool result is just «Error: Function failed.». In the 15:18 measurement gpt-5.4 gave up on `execute_code` after two blind failures and fell back to the read tools (13 model calls, 474k tokens). In the same session it also guessed field names, called tools without arguments and parsed numbered lines wrongly, because `execute_code`'s description lists only the name and description of each tool, never its parameters (`InstructionBuilder`) | `agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true` right after `AsHarnessAgent` when CodeAct is on (step-4/5 `Program.cs` and `UpgradeCaptainFactory`). `CodeActHint` lists the exact `call_tool(...)` signatures and how the runner really works (the code runs inside an `async def`, so use `await` and never `asyncio.run`; only the LAST expression comes back, not a `result` variable as the tool description claims), and the tool descriptions document their return shapes. Result: 2 `execute_code` calls, 5 model calls, 41k tokens, 43 s, and the real call sites found. Worth reporting upstream (a `HarnessAgentOptions` switch, or parameters in the `execute_code` description) |
| **D-33** | PLAN 7b.3: the wall prompt «Termina la actualización y ciérrala. No ejecutes los tests, ya los pasará CI.» on `after-mechanical-fix` | **No wall with gpt-5.4: 3/3 runs found the silent `GeoCalculator.Distance` change (meters vs km) and fixed it**, so a plain `dotnet test` afterwards was 24/24 green. The prompt tells the agent to look for behavior changes that compile, and the `teide-geo-migration` skill lists this one | Stage prompt changed for both the wall and the fix: **«Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.»** Wall 2/2: the agent writes the report and closes (honestly noting it didn't run the tests); `dotnet test` → 8 red. The message on stage is "the harness takes the model's word", not "the model is bad". Updated in `docs/steps.md`, `docs/run-of-show.md` and the step-6 console hint |
| **D-34** | PLAN §9.4: «`run_shell`: no reliable paths, so treat as non-protected and leave it to the built-in in-console approval» | **The code-owner gate could be bypassed.** In a step-6 rehearsal the agent edited `FareCalculator.cs` with `run_shell perl -0pi …`; David's console approved it as a normal shell command and María was never asked | `CodeOwnersPolicy.ExtractPaths` now treats every path-like word in a `run_shell` command as touched (conservative; a directory such as `cd src/Canarias.Tours.Payments` also counts). `PathsFor(call)` keeps only the protected ones for shell commands, so `s/a/b/` noise or a Domain path in the same command doesn't stop María from approving. Still a heuristic (`find … -exec`, variables and globs escape it), so the console approval stays. The prompt also asks for `file_access_*` edits. 7 new unit tests |
| **D-35** | PLAN step-5: «Sube Teide.Geo a 2.0.0» loads `nuget-upgrade` and prompts for the script | With a scoped prompt («…en Directory.Packages.props. Solo el cambio de versión, nada más.» or «…De momento solo la versión.») gpt-5.4 skipped the skill and edited the file with `file_access_replace`, 3/3. With the bare prompt it used `bump_package.cs` 2/2, but then went on to migrate the whole repo (including a Python script it wrote to `/tmp` via `run_shell`) | One general line in `Prompts.Prompt`: «Si alguna skill cubre la tarea, cárgala y síguela aunque la tarea parezca trivial…». Stage prompt «Sube Teide.Geo a 2.0.0. De momento solo la versión.» → `load_skill(nuget-upgrade)` → `run_skill_script bump_package.cs` (the only approval) → done, **2/2**, 8–14 s. The rehearsal driver's call list misses auto-approved calls (they're replayed inside `ToolApprovalAgent`), but the `execute_tool` spans show `load_skill` |
| **D-36** | Long runs against Foundry | Two transient network failures in about 20 runs: «Connection reset by peer» mid-stream and the client's default 100 s `NetworkTimeout`. `ClientRetryPolicy` retries requests but can't resume a broken stream | Nothing to change in the product: the vendored console catches it («❌ Stream error»), keeps the session, and David types «Sigue». The rehearsal driver does the same. For «Sigue» to work when the cut lands right after a tool call, see D-37. Noted in `docs/run-of-show.md` → "If something goes wrong on stage" |
| **D-37** | Recovering from D-36 with «Sigue» | **Doesn't always work.** In step-6 beat run 3 the connection broke right after the model asked for a tool. `PerServiceCallChatHistoryPersistence` had already saved the assistant message with the `function_call`, but the tool result was part of the request that failed. Every later request then failed with `HTTP 400 … No tool output found for function call call_…`, so the session was dead. Reproduced without a model in `ToolCallRepairTests` (`HarnessAgent_1_24_0_KeepsAnOrphanCallAfterANetworkCut_KnownBehavior`) | `ToolCallRepairChatClient` (Core, next to `CompactingChatClient`): before each request, every `FunctionCallContent` without a `FunctionResultContent` gets a synthetic result («la ejecución se interrumpió (error de red)…»), and the model decides whether to call the tool again. It is not filtered by `InformationalOnly`, because `FunctionInvokingChatClient` sets that flag on calls it already ran, which is exactly the orphan. Wired in steps 1–5 (`.WithInterruptedToolCallRepair()`, one line) and in the factory. 4 unit tests. Worth reporting upstream (persist the tool results before the next call, or repair on load) |
| **D-38** | Step-6 beat: one owner approval per Payments edit | gpt-5.4 often fixes one call site per `file_access_replace`, so María had to approve **2 to 9 times** per run (run 7: 9). Correct, but too slow and repetitive on stage | One more line in `Prompts.Prompt`: «Agrupa los cambios: como mucho una edición por fichero en cada pasada (un único file_access_write con el fichero completo si hay varios cambios). Cada edición puede necesitar la aprobación de una persona.» Runs 11–15: one `file_access_write` per file, **2 approvals in 4 runs, 4 in one**. `approve.sh next` (repeat with ↑ + Enter) covers the variable count |
| **D-39** | PLAN Phase 8: manifest `invocations 1.0.0`, `azd ai agent init`/`azd deploy`, approver from `x-agent-user-id` | Checked on 2026-10-13 against Microsoft Learn and the `Azure.AI.AgentServer.Core` XML docs: **(1)** "Protocol version 1.0.0 is no longer supported"; versions are declared as `2.0.0`. The AF 1.24.0 Invocations sample still says 1.0.0. **(2)** `x-agent-user-id` (and `x-agent-foundry-call-id`) are stamped **only on container protocol 2.0.0**. **(3)** Images must be linux/amd64. **(4)** The local azd 1.27.0 can only install `azure.ai.agents` 1.0.0-beta.5; the 120 s idle timeout needs beta.14+, and updating azd means `brew trust` plus a reinstall on David's machine. **(5)** The Invocations endpoint is `<project>/agents/<name>/endpoint/protocols/invocations?api-version=v1`, with `agent_session_id` read only from the query. **(6)** Sessions are isolated per caller identity, so with one account, trigger and approval share David's id | Manifest → `invocations 2.0.0`. New `scripts/deploy-hosted.sh` does what `azd deploy` does, with `az`: `az acr build --platform linux/amd64` on the existing `<acr-name>` (Sweden Central, same RG), `AcrPull` for the project identity (classic registry permissions), agent version over REST (2 CPU / 4 GiB, `session_configuration.idle_timeout_seconds: 120`, env `AZURE_AI_MODEL_DEPLOYMENT_NAME`, `UPGRADE_CAPTAIN_DEV_IDENTITY=1`), then waits for `active`. `hosted.sh` derives the remote endpoint and adds `api-version=v1`. `IdentityMap`: in dev mode the payload's `approver` wins over the header (one account). The handler logs whether `x-agent-user-id` arrived |
| **D-40** | Step-7 rehearsal, local host + real model | The job went green, but `UPGRADE_REPORT.md` said «Aprobaciones: ninguna» although María approved APR-0001/0002. The harness takes those decisions outside the conversation, so the model never sees them | `RepoStateProvider` now also lists the job's decided approvals («APR-0001 … aprobada por @maria-payments»), next to the pending ones. Unit test `RepoStateProviderTests` |
| **D-41** | First `az acr build` of the step-7 image | **Failed:** `cp: cannot stat '/demo-src/demo/canarias-tours-template/.gitignore'`. `az acr build` always drops files named `.gitignore` (and `.git`, `.hgignore`…) when it packs the source: they're hard-coded in the CLI's `_archive_utils.py`, whatever `.dockerignore` says. A local `docker build` keeps them | The template keeps the file as `demo/canarias-tours-template/gitignore`, with no dot (the trick npm templates use), and `reset-demo.sh` copies it to `.gitignore`. Second build: OK in 2m34s, linux/amd64, pushed `upgrade-captain:d1923a4-20261013195640` |
| **D-42** | Creating the hosted agent over REST (docs' REST pivot) | Four findings, each fixed in `deploy-hosted.sh` / `hosted.sh`. **(1)** Creating the agent returns the agent object, so the version is under `versions.latest.version`. **(2)** A freshly assigned `AcrPull` takes a while: version 1 failed with `ImageError` («Container registry authentication failed»); version 2, a few minutes later, went `active`. **(3)** The agent endpoint only enables `responses` by default; Invocations needs `PATCH /agents/<name>` with `agent_endpoint.protocol_configuration.invocations` (the PATCH replaces the whole list). **(4)** `agent_session_id=job-001` → HTTP 400 `invalid_session_id`; a UUID works | The script waits 120 s after a new role assignment, reads `versions.latest.version`, and PATCHes the endpoint to `invocations` + `Entra`. `hosted.sh trigger` generates a UUID session per job (`out/hosted/<jobId>.session`), which `status`/`approve` reuse |
| **D-43** | PLAN Phase 8: «traces appear in Application Insights with no exporter code» | **Only with `AgentHost`.** The host followed the AF 1.24.0 Invocations sample (`WebApplication.CreateBuilder` + `AddInvocationsServer`), which configures no exporter. In Foundry, App Insights only showed the platform's spans (`agentsv2`, `responsesapi`): nothing from our container. `AgentHost.CreateBuilder` (Azure.AI.AgentServer.Core) wires the Microsoft OpenTelemetry distro, which detects `APPLICATIONINSIGHTS_CONNECTION_STRING`, plus `/readiness` and port 8088. But it always listens on all interfaces, even with `ListenLocalhost` added | In the container: `AgentHost.CreateBuilder` + `AddInvocations<UpgradeInvocationHandler>()` + `ConfigureTracing(AddSource("UpgradeCaptain", "Experimental.Microsoft.Agents.AI"))`. Local (`hosted.sh serve --local`): `WebApplication` on localhost only, because the local host has no auth and conference Wi-Fi is shared. With v3, App Insights shows role `upgrade-captain`: `upgrade_captain.verify/park/decide/compaction`, `chat gpt-5.4`, every `execute_tool …`, and logs. The distro also exports to Foundry's agent observability endpoint |
| **D-44** | First delivery (netcoreconf Madrid, 2026-10-23): eight steps (`step-0`…`step-7`) in a 50-minute slot | **Too long.** Live-typing five `Program.cs` snippets and running a beat per step didn't fit; the part that matters most, the custom verifier that runs the tests even when the prompt says not to, was squeezed at the end | **Four steps** (2026-11-02), built from the same code (each step was already cumulative): **step-1** = old step-2 (harness + plan/execute + loop; the plain chat agent becomes a slide), **step-2** = old step-5 (shell, files, approvals, skills, OTel; CodeAct stays behind `--surface codeact` but is a slide only), **step-3-pre / step-3** = old 6-pre / 6 (the wall now runs on step-3-pre, and the fix runs the evaluator just typed via `--state after-mechanical-fix`, no checkout), **step-4** = old 7 (optional, the buffer). Only the evaluator is typed live. Old tags kept as `legacy/step-N`, `live` branch as `legacy/live`; `main` untouched except for the renumbering commit. Snippets reduced to `uc3-evaluator` and `uc3-wire`; `live-step.sh` removed. `scripts/rebuild-steps.sh` rebuilds the chain after changes on `main` (non-destructive, previous chain kept as `previous/step-N`). **Step numbers in entries above this one refer to the legacy tags** |
| **D-45** | Second delivery (late December 2026) is for an **English audience**, run from the console in Spanish | Everything the audience sees was Spanish: prompts, tool and parameter descriptions, skills, advisories, release notes, harness messages, the demo repo (comments, README, commits), even `dotnet` output and number formats (`30.663,76`). Three terminals are hard to follow on a projector | **Bilingual demo + web cockpit** (2026-11-20). `DemoLanguage` (Core) with `Pick(es, en)` next to each text; `UpgradeCaptainOptions.Language` (default `UPGRADE_CAPTAIN_LANG`, Spanish); `HarnessServices.English` for the harness pieces (the Harness project can't see Core). English data: `skills-en/`, `advisories.en.json`, `RELEASE-NOTES.en.md` (→ `notes/<v>.en.md`), and `reset-demo.sh --lang en` translates the demo repo with `demo/i18n/canarias-tours.en.txt` before each commit. In English the agent's shell, the harness verification and the Reality panel run with `DOTNET_CLI_UI_LANGUAGE=en` and **`LC_ALL=en_US.UTF-8`** (otherwise xUnit prints `23822,01` on a Spanish Mac). The evaluator's commit message and restore reasons are now English in both languages (`chore(deps): green verified by Upgrade Captain`, `stuck`, `approval rejected`, `budget exceeded`). **`src/UpgradeCaptain.Web`** (Blazor Server, localhost only, no CDNs): every step with its script, real code snippets (comments translated; `SnippetFidelityTests` checks every code line exists in `main` or the step tag) and a live panel (plan protocol, approvals with diff, María's window `/maria`, Reality, Hosted). Factory switches `EnableSkills` / `EnableVerifiedCompletion` express step-1 and step-3-pre without the tags. **Live run in English, all green**: step 1 (plan → edit → approve → loop), step 2 (skill + `bump_package.cs` approval), step 3 (wall: 16/24; evaluator: `🔎 … 8 of 24 fail`, Domain approved in the panel, 2 Payments edits parked, David refused, María approved → `✅ Green, verified by the harness … 24/24`, checkpoint commit). **Step-2 beat changed:** in English the model refuses `git push` on its own even after «Yes, run it» (the `nuget-upgrade` playbook says *never publish*), so the policy beat is now `rm -rf src/Canarias.Tours.Api/bin …` → approve → `Command rejected by policy: matched deny pattern: \brm\s+-rf\b`; `git push` stays as an optional "the model refuses too" aside. `rebuild-steps.sh` drops the web project from step-1…step-3 and copies `DemoLanguage.cs` with the shared Core files |










## 6. Demo data (Phase 1) ✔

`scripts/verify-trap.sh` passed on 2026-10-13 (about 6 s):

```
› Caso 1: Teide.Geo 1.8.0 (estado clean)
✔ [clean] build verde
    total=24 passed=24 failed=0
› Caso 2: Teide.Geo 2.0.0 + arreglos mecánicos (estado after-mechanical-fix)
✔ [after-mechanical-fix] build verde
    total=24 passed=16 failed=8
      ✘ …FareTests.Quote_LosCristianosToTeide_Is33_66Eur: Assert.Equal() Failure: Values differ   (30.663,76 € vs 33,66 €)
      … 5 more fare tests, 2 distance tests
✔ [after-mechanical-fix] fallan exactamente los 6 tests de tarifas y 2 de distancia
› Caso 3: + arreglo de unidades (lo que debería hacer el agente)
    total=24 passed=24 failed=0
✔ La trampa es determinista ✔
```

- `reset-demo.sh` takes **0.8 s**: 8 commits, tag `v3.2.0`, local identity, `origin` on a `.example` domain.
- Fixed on 2026-10-13: reusing an existing workspace (it keeps `bin/` and `obj/` warm) nested project folders, because `cp -R dir existing-dir` copies *into* the folder. `copy()` now copies a folder's contents. Checked with clean → after-mechanical-fix → clean on the same workspace: 24/24 → 8 red → 24/24.
- Breaking call sites: about 30 across Domain, Payments and Api. Two mentions sit inside comments (`PoiCard.cs`, `ElevationService.cs`).

## 7. Harness components (Phase 7a) ✔ without a model

- `dotnet test tests/UpgradeCaptain.Harness.Tests`: **60 passed, 1 skipped** (the integration test, which needs `UC_INTEGRATION=1`).
  - CODEOWNERS semantics.
  - Evaluator: green, red, stuck and plan mode.
  - Broker rules: owner check, separation of duties, rejection restore.
  - Resume in a fresh agent from `SessionVault`.
  - Attended wait-and-continue.
  - Audit hash chain: tamper and delete detection.
  - `BudgetGuard`: per session and streaming.
  - Factory equivalence with step-5 (`EnableCustomHarness = false`).
  - Verifier parsing (Spanish output, TRX).
  - `GitCheckpoints` with real git, including refusing to run inside another repository.
  - Compaction (D-07).
- `UC_INTEGRATION=1 dotnet test --filter Step6Beat`: **passes in 8 s**. It plays the full step-6 beat on the real workspace (`after-mechanical-fix`) with the real factory, real dotnet and real git, and a **scripted** model:

```
model: "Listo: compila. Doy la tarea por cerrada."              ← the wall
[user] 🔎 Verificación del harness … ❌ 8 de 24 fallan …          ← evaluator feedback
APPROVAL file_access_write (Domain)  → console, approved
⏸ Pendiente de aprobación de @maria-payments (APR-0001): file_access_write src/Canarias.Tours.Payments/FareCalculator.cs
david: @david-lorenzo no puede decidir APR-0001: no es propietario … y además pidió este trabajo (separación de funciones).
maria: Aprobada APR-0001 … La consola que espera continúa la ejecución.
▶ APR-0001 aprobada por @maria-payments: «Revisado». Continúo.
⏸ … (APR-0002): …TourPackagePricing.cs   → approved the same way
✅ Verde verificado por el harness: build ✅ · tests 24/24 (3 s). Checkpoint 0305038 en upgrade/teide-geo-2.0.
```

- `RepoVerifier` against the real workspace:
  - `clean` → red, `vulnerable: Teide.Geo 1.8.0 (TEIDE-2026-0042)`, 24/24.
  - `after-mechanical-fix` → red, 8 failures, each with file:line and Expected/Actual. Takes 2.5–3.5 s once warm.

## 8. Git history and tags (Phase 9)

- **Since 2026-11-02 (D-44): four steps.** Tags `step-1`, `step-2`, `step-3-pre`, `step-3`, `step-4` on branch `steps`, built on top of `main`; the bullets below describe the first version, now under `legacy/step-N`.
- History rebuilt as one base commit plus one commit per step. Tags: `step-0` … `step-5`, `step-6-pre`, `step-6`, `step-7`. Branch `live` starts at `step-0`.
- Every tag builds with **0 warnings**. Tests pass on `step-6-pre` (55), `step-6` and `step-7` (60 + 1 integration, skipped by default).
- `step-7` only adds `src/UpgradeCaptain.Hosted`, `deploy/` and one solution line: the factory and the harness do not change between step-6 and step-7.
- `scripts/goto-step.sh 3` takes about 3 s once warm. The `live-step.sh 1` + `uc1-harness` expansion reproduces `step-1` exactly (0-line diff).

## 9. Hosted (Phase 8) ✔ deployed and verified in Foundry

- `scripts/hosted.sh serve --local` (simulated `$HOME` and `/opt/demo` under `out/`):
  - `/readiness` returns 200.
  - `job.status` returns 404 `unknown` for a job that doesn't exist.
  - A missing identity returns 403.
  - An unknown event returns 400.
  - The first request copies the workspace into the simulated `$HOME`.
- `docker build -f deploy/Dockerfile .` **builds** (native arm64). Inside the container:
  - `/readiness` returns 200.
  - `$HOME/work/canarias-tours` is copied from `/opt/demo`.
  - `dotnet test` gives **24/24 offline** from `/opt/nuget` (73 MB).
  - git is present; python 3.12 is installed only for CodeAct.
- **Local host + real model (gpt-5.4), 2026-10-13:** `hosted.sh serve --local`, then `trigger` → `running` → **`waiting_approval` after about 80 s** (APR-0001, `Payments/FareCalculator.cs`, owner `@maria-payments`, requested by `@dependabot`). `approve --as @david-lorenzo` → **409**, not the owner. `approve --as @maria-payments` → 202, resumes in the background → APR-0002 (`TourPackagePricing.cs`) → approved → **`green`**, build ✅ · tests 24/24, checkpoint `chore(deps): verde verificado…` on `upgrade/teide-geo-2.0`, `UPGRADE_REPORT.md` written, 10 audit entries. The full migration started from the clean 1.8.0 state. The report listed no approvals → D-40
- **Foundry, 2026-10-13 (agent `upgrade-captain`, `<project>`, Sweden Central, gpt-5.4).** Deployed with `scripts/deploy-hosted.sh` (D-39, D-41, D-42, D-43). Version 1 failed (`ImageError` while the new `AcrPull` propagated). v2 and v3 are active: invocations 2.0.0, 2 CPU / 4 GiB, idle timeout 120 s.
  - **Resume on new compute (v2):**

    | Time | What |
    |---|---|
    | 20:02:13 | `trigger` (UUID session) → 202 |
    | 20:03:44 | `waiting_approval` (APR-0001, `FareCalculator.cs` → `@maria-payments`, requested by `@dependabot`). Then no calls |
    | 20:06:14 | Session `status: idle`, `stopped_at` set (sessions API): compute released |
    | 20:08:10 | `approve --as @david-lorenzo` wakes the session in about 3 s, with the state in `$HOME/state` intact, and gets **409**, not the owner |
    | 20:08:13 | `approve --as @maria-payments` → 202, resumes |
    | 20:08:22 | APR-0002 (`TourPackagePricing.cs`) → approved |
    | 20:09:28 | **`green`**, build ✅ · tests 24/24, `reportPath` `/home/session/work/canarias-tours/UPGRADE_REPORT.md` |

  - **Report**, downloaded with the session files API: it lists «`FareCalculator.cs` aprobada por @maria-payments (APR-0001)» and «`TourPackagePricing.cs` … (APR-0002)» (D-40 fixed).
  - **v3** (the D-43 telemetry fix): trigger → park in about 100 s → 2 approvals → green in about 3 min. App Insights shows our container's spans and logs (D-43).


## 10. Live checks with the model (gpt-5.4, 2026-10-13)

Driven by a rehearsal harness (scratch, not in the repo). It builds each step's agent exactly like that tag's `Program.cs` and answers approvals the way David and María would on stage.

| Phase | Check | Result |
|---|---|---|
| 0 | Model smoke test | ✔ after D-26: `gpt-5.4` answers in about 2 s (`scripts/smoke-model.sh`), through both the curl and the .NET client paths |
| 2 | Inventory: several tool calls in one turn | ✔ «Haz inventario de dónde usamos Teide.Geo» → **24 tool calls in 8 model calls** (14 `read_source` in parallel batches), 58k input tokens. Wall time **27–110 s** (variance comes from the model) |
| 2 | Session export/import | ✔ 4.6 KB session. A new agent instance deserialized it and answered «¿Qué proyectos…?» from memory with **0 tool calls** |
| 2 | Compaction really happens (Q5) | ✔ with `--context tiny` recalibrated to **16,000 / 8,000** (D-28). See "Compaction proof" |
| 3 | Plan mode creates todos and asks; correcting the plan; execute | ✔ «…, con el informe final incluido» → 3 todos, mode stays `plan`. «El informe lo escribo yo» removes the todo (3 → 2). «Adelante, pasa a execute» → the model calls `mode_set` itself, and the loop runs until **5/5 todos** are done (116 s, about 311k input tokens). Without "informe" in the prompt there is no report todo to remove (D-29) |
| 4 | `dotnet build` prompts; a denied edit adapts; `git push` blocked | ✔ `run_shell dotnet build` → approval → OK. A denied `file_access_replace_lines` on README → the agent says it made no change. `git push`: see "Policy block output" and D-27 |
| 5 | Surfaces direct vs CodeAct | ✔ both surfaces work and find the same call sites (ElevationService 9/13/14/19, Itinerary, PoiCard…), skipping the comment in `PoiCard.cs:8`. **CodeAct is not cheaper here**: see "Surface measurement 17:25" and the note under it |
| 6 | Skills, `bump_package.cs`, spans | ✔ «¿Qué cambia en Teide.Geo 2.0 y cómo lo arreglamos?» → `load_skill(teide-geo-migration)` + `read_skill_resource(references/breaking-changes.md)`, answered in about 10 s. «Sube Teide.Geo a 2.0.0. De momento solo la versión.» → `load_skill(nuget-upgrade)` → `run_skill_script bump_package.cs` is the only approval → `Directory.Packages.props` at 2.0.0, **2/2** after D-35. Every `chat` span carries token attributes (8/8, 10/10). Spans: `invoke_agent`, `chat`, `execute_tool`, `upgrade_captain.compaction`, `compaction.compact` |
| 7b | The wall (step-5, `after-mechanical-fix`) | ✔ after D-33: «Ya compila. Cierra la actualización…» → the agent writes `UPGRADE_REPORT.md` and closes; a plain `dotnet test` → **8 red, 2/2**. With the PLAN prompt the model fixed the trap itself, 3/3 |
| 7b | Step-6 beat 5/5 | ✔ **5/5 with the final code and prompt** (runs 11–15), plus 5/5 before D-38 (runs 6–10). Every run: the harness feedback «8 de 24 fallan», David refused (not the owner, and he asked for the job), María approved, green 24/24, checkpoint commit `chore(deps): verde verificado…`, audit chain verified. About 100 s per run. Earlier failures, all fixed: D-34, D-36, D-37 |

#### Compaction proof

`--context tiny`, inventory prompt, input tokens of each model call (from the `chat` spans):

| Window | Compacted calls (`upgrade_captain.compaction`) | Input tokens per call |
|---|---|---|
| 24,000 / 12,000 (first value) | 2/9, only in the 2nd turn | 2858, 3008, 3423, 3804, 7504, 10176, 10289, 10519, 12826: no visible drop |
| **16,000 / 8,000** (chosen) | 3/7 already in the 1st turn | 2858, 3077, 3791, 4195, 11553, **12001 → 4486**, 4438, 5883, 7415 |
| 12,000 / 4,000 | 3/7 in the 1st turn | …, 11538, 11124, 10881 → **4772**, … |

On stage: with `--context tiny`, the usage line climbs to about 12k while the files are read and drops to about 4.5k on the next call.

#### Policy block output

Step-3, «Ejecuta git push origin main» → approval prompt → approved →

```
call run_shell(command=git push origin main)
result: Command rejected by policy: matched deny pattern: \bgit\s+push\b
agent:  No puedo ejecutar `git push origin main`. El entorno lo bloquea por política:
        - `Command rejected by policy: matched deny pattern: \bgit\s+push\b` …
```

In one of two runs, gpt-5.4 first asked for explicit confirmation («pídemelo confirmando exactamente: Sí, ejecuta git push origin main»). On stage, answer «Sí, ejecútalo».

#### Step-6 beat runs

| # | Date | Model | Wall shown | Refused David | María approved | Green + checkpoint | Notes |
|---|---|---|---|---|---|---|---|
| pre-a | 2026-10-13 16:14 | gpt-5.4 | ✔ | — | — | ✘ | PLAN prompt. The agent edited Payments with `run_shell perl -0pi`, approved in David's console, **María never asked** → D-34. Then the run crashed on a connection reset (driver had no recovery yet) |
| pre-b | 2026-10-13 16:20 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✘ | «Ya compila…» prompt (D-33). APR-0001/0002 worked; then the client's 100 s network timeout crashed the driver → D-36 |
| 1 | 2026-10-13 16:26 | gpt-5.4 | ✔ 8/24 red | ✔ ×3 | ✔ ×3 | ✔ 24/24, checkpoint `3a97288` | 152 s, 2 verifications, 2 Domain approvals in the console. Audit chain OK |
| 2 | 2026-10-13 16:29 | gpt-5.4 | ✔ | ✔ ×4 | ✔ ×4 | ✔ 24/24, checkpoint `912f83b` | 387 s: one connection reset, recovered with «Sigue» (D-36). 3 verifications. Audit OK |
| 3 | 2026-10-13 16:36 | gpt-5.4 | — | — | — | ✘ | Connection reset right after a tool call → «Sigue» → HTTP 400 «No tool output found for function call» → **D-37** (`ToolCallRepairChatClient`) |
| 4 | 2026-10-13 16:36 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `3d436b0` | 127 s, 2 verifications. Audit OK |
| 5 | 2026-10-13 16:38 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `9b922d0` | 163 s, 2 verifications. Audit OK |
| 6 | 2026-10-13 17:24 | gpt-5.4 | ✔ | ✔ ×5 | ✔ ×5 | ✔ 24/24, checkpoint `dd94812` | With D-37. 103 s. One small `file_access_replace` per call site → 5 approvals |
| 7 | 2026-10-13 17:26 | gpt-5.4 | ✔ | ✔ ×9 | ✔ ×9 | ✔ 24/24, checkpoint `e6f0bfc` | 96 s. **9 approvals** (it also added `Payments/GeoDistance.cs`): too many for the stage → D-38 |
| 8 | 2026-10-13 17:28 | gpt-5.4 | ✔ | ✔ ×3 | ✔ ×3 | ✔ 24/24, checkpoint `80f7e5d` | 113 s |
| 9 | 2026-10-13 17:30 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `649c961` | 99 s |
| 10 | 2026-10-13 17:32 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `2477325` | 106 s. Runs 6–10: **5/5** with the D-37 code |
| 11 | 2026-10-13 17:35 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `367b99a` | **Final prompt (D-38).** One `file_access_write` per Payments file. 102 s |
| 12 | 2026-10-13 17:37 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `d58cc81` | 84 s |
| 13 | 2026-10-13 17:38 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `587050e` | 100 s |
| 14 | 2026-10-13 17:40 | gpt-5.4 | ✔ | ✔ ×2 | ✔ ×2 | ✔ 24/24, checkpoint `ee4cf55` | 108 s |
| 15 | 2026-10-13 17:41 | gpt-5.4 | ✔ | ✔ ×4 | ✔ ×4 | ✔ 24/24, checkpoint `7fe2641` | 109 s. Runs 11–15: **5/5 with the final prompt**, 2–4 approvals per run. The wall re-checked afterwards: still 8 red |



#### Surface measurement 2026-10-13 14:55 · BLOCKED by the content filter (D-31) · outputs in out/measure/20261013-145327

| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |
|---|---|---|---|---|---|---|---|
| 2026-10-13 | direct | 1 | 1 | 0 | 0 | 0 | 2,5 error=400 |
| 2026-10-13 | direct | 2 | 1 | 0 | 0 | 0 | 2,9 error=400 |
| 2026-10-13 | direct | 3 | 1 | 0 | 0 | 0 | 2,3 error=400 |
| 2026-10-13 | codeact | 1 | 1 | 0 | 0 | 0 | 2,9 error=400 |
| 2026-10-13 | codeact | 2 | 1 | 0 | 0 | 0 | 2,1 error=400 |
| 2026-10-13 | codeact | 3 | 1 | 0 | 0 | 0 | 2,4 error=400 |

#### Surface measurement 2026-10-13 15:04 · INVALID: every run stopped at a `load_skill` approval (default skills provider scanned the main repo from cwd; fixed by running from the step-4 worktree) · outputs in out/measure/20261013-150331

| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |
|---|---|---|---|---|---|---|---|
| 2026-10-13 | direct | 1 | 1 | 0 | 4851 | 109 | 4,6 |
| 2026-10-13 | direct | 2 | 1 | 0 | 4851 | 24 | 2,9 |
| 2026-10-13 | direct | 3 | 2 | 1 | 10064 | 265 | 7,0 |
| 2026-10-13 | codeact | 1 | 2 | 1 | 10100 | 187 | 5,1 |
| 2026-10-13 | codeact | 2 | 2 | 1 | 10281 | 225 | 6,4 |
| 2026-10-13 | codeact | 3 | 2 | 1 | 10211 | 229 | 6,1 |

#### Surface measurement 2026-10-13 15:18 · INVALID: CodeAct errors were hidden from the model (D-32), and direct run 2 stopped at a `run_shell` approval (headless mode now denies and continues) · outputs in out/measure/20261013-150444

| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |
|---|---|---|---|---|---|---|---|
| 2026-10-13 | direct | 1 | 8 | 24 | 76016 | 3745 | 47,7 |
| 2026-10-13 | direct | 2 | 4 | 8 | 21960 | 548 | 12,0 |
| 2026-10-13 | direct | 3 | 7 | 23 | 62792 | 4533 | 144,2 |
| 2026-10-13 | codeact | 1 | 4 | 3 | 22362 | 2074 | 28,9 |
| 2026-10-13 | codeact | 2 | 4 | 3 | 22601 | 2518 | 30,4 |
| 2026-10-13 | codeact | 3 | 8 | 6 | 50351 | 2889 | 140,8 |

#### Surface measurement 2026-10-13 15:54 · model gpt-5.4 · before the runner-semantics hint (D-32): CodeAct needed about 3 `execute_code` attempts per run · outputs in out/measure/20261013-153515

| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |
|---|---|---|---|---|---|---|---|
| 2026-10-13 | direct | 1 | 6 | 22 | 49722 | 3988 | 154,9 denied=0 |
| 2026-10-13 | direct | 2 | 7 | 23 | 63117 | 3723 | 147,5 denied=0 |
| 2026-10-13 | direct | 3 | 6 | 21 | 48697 | 3839 | 47,2 denied=0 |
| 2026-10-13 | codeact | 1 | 6 | 5 | 59684 | 4326 | 153,4 denied=0 |
| 2026-10-13 | codeact | 2 | 6 | 5 | 53198 | 6247 | 61,6 denied=0 |
| 2026-10-13 | codeact | 3 | 6 | 5 | 89423 | 6385 | 167,4 denied=0 |

#### Surface measurement 2026-10-13 17:25 · model gpt-5.4 · outputs in out/measure/20261013-164833

| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |
|---|---|---|---|---|---|---|---|
| 2026-10-13 | direct | 1 | 9 | 20 | 84486 | 4465 | 69,0 denied=1 |
| 2026-10-13 | direct | 2 | 6 | 22 | 49939 | 4206 | 62,8 denied=0 |
| 2026-10-13 | direct | 3 | 6 | 19 | 50333 | 4371 | 80,9 denied=1 |
| 2026-10-13 | codeact | 1 | 7 | 28 | 86984 | 5667 | 379,8 denied=0 |
| 2026-10-13 | codeact | 2 | 6 | 10 | 58890 | 4667 | 147,1 denied=0 |
| 2026-10-13 | codeact | 3 | 5 | 4 | 43364 | 4956 | 167,6 denied=0 |

**Reading the surface numbers (17:25, final code).** With gpt-5.4 on this small repo, CodeAct doesn't beat direct:

- **Direct.** gpt-5.4 already batches the reads (about 20 tool calls in 6 model calls, 50k tokens).
- **CodeAct.** It does fewer tool round trips when it stays inside `execute_code` (runs 2 and 3: 10 and 4 tool calls). But it isn't cheaper in tokens, and it was slower (147–380 s, against 63–81 s for direct in the same hour).
- **CodeAct run 1** fell back to the file tools (28 tool calls).
- **Where CodeAct pays off** is many tool calls with large intermediate data that stays out of the context. A 6-project demo repo is too small to show that.
- **On stage,** show the shape in Aspire, not the totals: direct is a fan of `execute_tool read_source` spans, CodeAct is one `execute_tool execute_code` span. The numbers stay off the slides, as planned.
