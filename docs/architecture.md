# Architecture

"Custom harness" in this talk means **`HarnessAgent` plus a few components plugged into its documented extension points**. It is not a from-scratch loop.

```
Trigger: console (attended) | run --job (headless) | Invocations webhook (hosted)
  │
  ▼
CodeOwnerApprovalAgent                      custom · DelegatingAIAgent, outermost
  │  parks approvals for protected paths, blocks the session while parked, resumes saved sessions,
  │  writes the audit log, adds the harness verdict line (✅ / ⛔ / ❌)
  ▼
HarnessAgent                                built-in (Microsoft.Agents.AI.Harness 1.24.0)
  ├─ decorators: LoopAgent (outermost), ToolApprovalAgent, OpenTelemetryAgent
  │    LoopEvaluators, in order:
  │      1. TodoCompletionLoopEvaluator     built-in, mode "execute"
  │      2. VerifiedCompletionEvaluator     custom,   mode "execute"
  ├─ context providers
  │    Todo, AgentMode, FileMemory, AgentSkills, FileAccess    built-in
  │    ShellEnvironmentProvider, LocalCodeActProvider          preview packages
  │    RepoStateProvider                                       custom
  └─ chat pipeline: approval binding → bypass → function invocation → message injection → per-call persistence → OTel
        ▼
     BudgetGuard                            custom · DelegatingChatClient
        ▼
     CompactingChatClient                   built-in strategy, stateless application (D-07)
        ▼
     IChatClient                            Foundry project, Responses API, store disabled
```

Shared services (`HarnessServices`): `RepoVerifier`, `RepoStateCache`, `GitCheckpoints`, `SessionVault`, `ApprovalStore`, `AuditLog`, `CodeOwnersPolicy`. They all emit on the `UpgradeCaptain` ActivitySource and Meter, the same name passed to `OpenTelemetrySourceName`, so everything lands in one trace:

| Span or metric | Where |
|---|---|
| `invoke_agent`, `chat`, `execute_tool` | AF `OpenTelemetryAgent` / `OpenTelemetryChatClient` |
| `upgrade_captain.verify` (`build.ok`, `tests.failed`, `vulnerable.count`) | `RepoVerifier` |
| `upgrade_captain.park`, `upgrade_captain.decide` | `CodeOwnerApprovalAgent` |
| `upgrade_captain.checkpoint`, `upgrade_captain.restore` | `GitCheckpoints` |
| `upgrade_captain.compaction` (`messages.before`, `messages.after`) | `CompactingChatClient` |
| counter `upgrade_captain.tokens` (`token.type`) | `BudgetGuard` |
| counter `upgrade_captain.verifications` (`result`) | `RepoVerifier` |

## Projects

| Project | Role |
|---|---|
| `UpgradeCaptain.Core` | Prompt, tools, paths, telemetry, compaction. From step-3, also `UpgradeCaptainFactory` / `UpgradeCaptainOptions` / `UpgradeCaptainBuild`. References `UpgradeCaptain.Harness`. |
| `UpgradeCaptain.Harness` | The custom pieces. Depends only on `Microsoft.Agents.AI` and `Microsoft.Extensions.AI`. |
| `UpgradeCaptain.Console` | `Program.cs` builds the agent by hand in steps 1–2. From step-3: CLI (`--context`, `--surface`, `--as`, `--state`), `run --job`, `approvals …`. |
| `UpgradeCaptain.Hosted` | Step-4 Invocations host (`UpgradeInvocationHandler`, `UpgradeJobs`, `IdentityMap`). |
| `vendor/*` | AF sample console projects at `dotnet-1.24.0`, unchanged except for the package reference. |

## Approval flow (step-3)

1. A run ends with a `ToolApprovalRequestContent`. `ToolApprovalAgent` surfaces **one at a time** (D-11).
2. `CodeOwnerApprovalAgent` extracts the touched paths (`CodeOwnersPolicy.ExtractPaths`):
   - `file_access_write/replace/replace_lines/delete` → `fileName`
   - `run_skill_script` of `bump_package.cs` → `Directory.Packages.props`
   - `run_shell` → no reliable paths
3. **Attended and nothing protected:** the request is passed through to the console's built-in prompt.
4. **Something protected, or unattended:** the request is **parked**.
   - The session (serialized *after* the inner run finished) goes to `SessionVault`; its SHA-256 is stored in the approval.
   - `PendingApproval` (with the original request JSON) goes to `ApprovalStore`.
   - `requested` and `parked` go to `AuditLog`.
   - The request is removed from the output, and `⏸ Pendiente de aprobación de @owner (APR-xxxx)` is shown.
5. **Rules** (`DecideAsync`):
   - the approver owns **every** touched path (CODEOWNERS, last match wins), and
   - the approver is not the requester.

   Otherwise `refused` is audited with every reason.
6. **Resume:** `CreateResponse(approved, reason)` on the stored request (Q3) → `RunAsync([response], session)`.
   - Attended: the console that parked it keeps waiting inside the same run and continues.
   - Headless or hosted: the deciding process loads the session from the vault (hash checked) and continues.
   - Rejection: `GitCheckpoints.RestoreLastGreenAsync()` first.
7. While a batch is open, the session does not advance: another run would wipe the approval-binding record.

"Always approve this tool…" answers for tools that touch files are downgraded to a one-time approval (D-12). Otherwise a standing rule inside `ToolApprovalAgent` could approve a later protected edit before the owner sees it.

## Why the hosted agent keeps the shell on

Microsoft's Claw hosted sample turns the shell off. Upgrade Captain keeps shell **and** file access on in Foundry (`UpgradeCaptainOptions.ForHosted`), because a dependency upgrade that cannot build and test is not an upgrade. The boundary is built from several layers, from strongest to weakest:

1. **Per-session VM isolation.** Each Foundry hosted-agent session runs in its own sandboxed compute with its own `$HOME`. The agent never touches the speaker's machine or other sessions.
2. **Confinement.** `WorkingDirectory = $HOME/work/canarias-tours`, `ConfineWorkingDirectory = true`, `CleanEnvironment = true` (no inherited secrets); file access is rooted in the same folder.
3. **Allow list.** The shell only accepts `dotnet …` and `git status|diff|log|add|commit`, plus the deny list (`rm -rf`, `sudo`, `git push`, `dotnet nuget push`). Patterns are a pre-filter, not a guarantee (chained commands), which is why the layers below exist.
4. **Approval gate on protected code.** Writes to protected paths (CODEOWNERS) park until their owner, who is not the requester, decides. Unprotected writes and allow-listed shell commands are auto-approved in the hosted, unattended mode (D-16); everything is audited.
5. **Audit log and checkpoints.** Every decision is in a hash-chained JSONL file in `$HOME/state`. Every green state is a commit, and a rejection or a budget overrun restores the last green.

CodeAct uses `LocalCodeActProvider` in the container (`CodeAct = Local`). Locally it is **not** a sandbox and the speaker says so; in Foundry the session VM is the sandbox.

## State layout

| Path | Contents |
|---|---|
| `StateRoot/approvals.json` | parked approvals (file lock + atomic writes; shared by David's console, María's terminal and the hosted agent) |
| `StateRoot/sessions/<jobId>.json` | serialized sessions |
| `StateRoot/audit.jsonl` | append-only audit log with SHA-256 chain |
| `StateRoot/checkpoints.json` | baseline and green checkpoint SHAs |
| `StateRoot/jobs/<jobId>.json` | last known job status (hosted, survives scale-to-zero) |

`StateRoot` is `./state` locally (cleared by `reset-demo.sh`) and `$HOME/state` when hosted.
