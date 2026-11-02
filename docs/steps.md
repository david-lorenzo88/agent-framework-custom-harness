# How the harness grows, step by step

Four steps, each one a **git tag** that builds and runs on its own (plus `step-3-pre`, the starting point for the one piece typed live):

```bash
scripts/goto-step.sh 2            # checkout step-2, reset the demo repo, build
dotnet run --project src/UpgradeCaptain.Console
```

`scripts/diff-step.sh N` shows exactly what step N adds over the previous one. The slide for each step is that diff. `scripts/diff-step.sh 1` compares with the plain chat agent of the first version of the talk (`legacy/step-0`).

| Tag | Slide | Adds | Built-in or custom | Main file |
|---|---|---|---|---|
| `step-1` | Harness | `AsHarnessAgent`, `UpgradeTools.All`, persisted history, compaction, OTel, `DefaultMode = "plan"`, `TodoCompletionLoopEvaluator` | built-in | `src/UpgradeCaptain.Console/Program.cs` |
| `step-2` | Approve | `LocalShellExecutor` + deny list, `FileAccessStore`, approval rules, `AgentFileSkillsSource` + script runner, Aspire. CodeAct behind `--surface codeact` (slide only) | built-in + preview packages | `Program.cs`, `skills/` |
| `step-3-pre` | Tu harness (the wall) | Factory + every custom piece **except** the evaluator | custom | `src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs` |
| `step-3` | Tu harness | `VerifiedCompletionEvaluator` + one wiring line | **custom** | `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs` |
| `step-4` | Hosted Agent (optional) | Same factory behind an Invocations host | built-in hosting + custom state | `src/UpgradeCaptain.Hosted/` |

What exists in **every** tag: the demo data (`demo/`), the scripts, the vendored console (`vendor/`), the docs and `UpgradeCaptain.Core`, which holds:
- the Spanish `Prompts.Prompt`
- the five read-only tools in `UpgradeTools.All`
- `DemoPaths`, `DotEnv`, `Telemetry`
- `CompactingChatClient` (D-07) and `ToolCallRepairChatClient` (D-37)

The first version of the talk had eight steps (`step-0`…`step-7`); they are kept as `legacy/step-N` tags (D-44).

**Keeping the tags in sync.** The tags live on branch `steps`, built on top of `main`. After any change on `main`, run `scripts/rebuild-steps.sh`: base files come from `main`; steps 1 and 2 keep their own `Program.cs` (from the current tag, or from a branch passed with `--step1-from` / `--step2-from`) with the shared Core files refreshed from `main`; steps 3-pre, 3 and 4 are derived from `main`. Every step is built (and tested, from 3-pre on) before any tag moves, and the previous chain is kept as `previous/step-N`.

---

## step-1 · Harness

**Goal:** go from "a model that only talks" to a real agent: tools, memory, a plan you can correct, and a loop that doesn't stop until the work is done. The plain `ChatClientAgent` (no tools, no session) is one slide, not a step.

Diff highlights (`scripts/diff-step.sh 1`):
- **Client without server-side storage:**

  ```csharp
  .GetProjectResponsesClientForModel(model)
  .AsIChatClientWithStoredOutputDisabled(model)
  .WithInterruptedToolCallRepair()
  .WithContextWindowCompaction(maxContextWindowTokens, maxOutputTokens);
  ```

  With `store` on, the service keeps the conversation and client-side compaction has no effect (Q5). The built-in compaction strategy runs on every model call through `CompactingChatClient`, because the harness's own in-loop compaction drops messages in 1.24.0 (D-07). `WithInterruptedToolCallRepair()` keeps the session valid after a network cut (D-37). Both are good "lessons from preparing this talk" moments for a slide.
- **The harness, in plan mode, with the todo loop:**

  ```csharp
  AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
  {
      ChatOptions = new ChatOptions { Instructions = Prompt, Tools = UpgradeTools.All },
      DisableWebSearch = true,
      AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "plan" },
      LoopEvaluators =
      [
          new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions
          {
              Modes = ["execute"],
              FeedbackMessageTemplate = "Aún quedan tareas abiertas. … Pendientes:\n" + TodoCompletionLoopEvaluator.RemainingTodosPlaceholder,
          }),
      ],
      LoopAgentOptions = new LoopAgentOptions { MaxIterations = 10 },
      FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(paths.ProjectRoot, "agent-file-memory")),
      OpenTelemetrySourceName = Telemetry.SourceName,
  });
  ```

- **The vendored console:** `HarnessConsole.RunAgentAsync` with `BuildObserversWithPlanning` and `BuildDefaultCommandHandlers` (`/todos`, `/mode`, `/session-export`).

**Show (one beat):** «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido». It reads the repo with several tool calls in one turn, creates todos and asks. Correct it out loud: «el informe lo escribo yo, quítalo». Then `/todos` and «adelante, pasa a execute»: the loop runs until the todos are done or 10 iterations pass.

Optional if there's time: `--context tiny` (16,000 / 8,000) to watch the usage line drop from ~12k to ~4.5k tokens when old tool results are compacted, and `/session-export` + `/session-import`.

---

## step-2 · Approve

**Goal:** the agent can act, but only inside the demo repo and with a human in the loop; know-how lives in skills it loads on demand; everything is visible in one trace.

```csharp
await using var shell = new LocalShellExecutor(new LocalShellExecutorOptions
{
    WorkingDirectory = paths.RepoDir,          // demo-workspace/canarias-tours, never this repo or $HOME
    ConfineWorkingDirectory = true,
    CleanEnvironment = true,                   // default is false: it would inherit FOUNDRY_*/AZURE_* (D-05)
    Timeout = TimeSpan.FromSeconds(120),
    Policy = new ShellPolicy(denyList: [@"\brm\s+-rf\b", @"\bsudo\b", @"\bgit\s+push\b", @"\bdotnet\s+nuget\s+push\b"]),
});
// Tools: [.. UpgradeTools.All, shell.AsAIFunction(requireApproval: true)]
// FileAccessStore: new FileSystemAgentFileStore(paths.RepoDir)
AgentSkillsSource = new AgentFileSkillsSource([paths.SkillsDir], new DotnetScriptRunner(paths.RepoDir).RunAsync),
AutoApprovalRules =
[
    FileAccessProvider.ReadOnlyToolsAutoApprovalRule,    // reading files: no prompt; writing: prompt
    AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,   // load/read skills: no prompt; run_skill_script: prompt
],
```

Skills:
- `skills/nuget-upgrade`: the playbook (CPM only, no prereleases, branch and commit format, report template) plus `scripts/bump_package.cs`, a .NET 10 file-based app that `DotnetScriptRunner` runs with `dotnet run bump_package.cs -- …`.
- `skills/teide-geo-migration`: `references/breaking-changes.md` and `assets/fix-recipes.md`.

**Show:**

```bash
scripts/aspire.sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

1. «Sube Teide.Geo a 2.0.0. De momento solo la versión.» loads `nuget-upgrade`, and `run_skill_script bump_package.cs` prompts: approve it. Without «De momento solo la versión» the agent goes on to migrate the code.
2. «Ejecuta git push origin main» (gpt-5.4 sometimes asks for confirmation first: «Sí, ejecútalo»). It prompts, you approve, and then the policy blocks it: `Command rejected by policy: matched deny pattern: \bgit\s+push\b`. The deny list applies even after a human says yes (D-13). Say it plainly: the deny list is a UX filter, not a sandbox.
3. Aspire (http://localhost:18888): `invoke_agent → chat → execute_tool` with token attributes, including `load_skill`.

**CodeAct is a slide**, not a live beat (D-44): the code is in this tag behind `--surface codeact` (`LocalCodeActProvider`, `IncludeDetailedErrors` so the model sees its Python errors, D-32). Show the two Aspire traces side by side: direct is a fan of `execute_tool read_source` spans, CodeAct is a single `execute_tool execute_code` span. Don't promise fewer tokens: on this small repo it wasn't cheaper (verification log, "Reading the surface numbers"). **`LocalCodeActProvider` is not a sandbox.** Reference numbers: `scripts/measure-surfaces.sh`.

---

## step-3 · Tu harness

**Goal:** the custom harness, built on the harness's own extension points. **It is not a new loop.** The star is the custom verifier: the harness forces the tests to run even when the user's prompt says not to.

| Wall | Built-in at 1.24.0 | Custom piece | Extension point |
|---|---|---|---|
| "Done" = builds + tests pass | evaluators judge the conversation | `VerifiedCompletionEvaluator` | `LoopEvaluators` (after the todo evaluator) |
| Approver = code owner, not requester | the approval goes back to the caller | `CodeOwnerApprovalAgent` + `CodeOwnersPolicy`, `ApprovalStore`, `SessionVault`, `AuditLog` | outer `DelegatingAIAgent` |
| Undo a bad change | no rollback | `GitCheckpoints` | used by the evaluator and the broker |
| Ground truth + cost cap | compaction can drop exact errors; only iteration caps | `RepoStateProvider`, `BudgetGuard` | `AIContextProviders`, inner `DelegatingChatClient` |

The refactor: `Program.cs` now calls

```csharp
UpgradeCaptainOptions options = UpgradeCaptainOptions.ForLocal(DemoPaths.Current.RepoDir);
await using UpgradeCaptainBuild build = UpgradeCaptainFactory.Create(options);
```

With `EnableCustomHarness = false`, the factory produces exactly the step-2 agent; `FactoryEquivalenceTests` proves it.

**1. The wall** (`step-3-pre`: every custom piece except the evaluator):

```bash
scripts/goto-step.sh 3-pre --state after-mechanical-fix
dotnet run --project src/UpgradeCaptain.Console
```

`/mode execute` and «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.» The agent writes the report and declares it done. In a plain terminal, `cd demo-workspace/canarias-tours && dotnet test` shows 8 red tests, for example a fare of 30.663,76 € instead of 33,66 €.

Why «Ya compila…» (D-33): with a neutral «Termina la actualización y ciérrala…», gpt-5.4 found the silent `Distance` change by itself. The wall is not "the model is bad". It is "the harness takes the model's word": the user says «no ejecutes los tests», the agent obeys and closes, and nothing checks.

**2. Live coding:** open `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs` (new file) and type the `uc3-evaluator` snippet. Then, in `UpgradeCaptainFactory.BuildHarnessOptions`, replace the `// uc3:` comment with the `uc3-wire` snippet:

```csharp
evaluators.Add(new VerifiedCompletionEvaluator(services.Verifier, services.Cache, services.Checkpoints));
```

The evaluator, about 30 lines:
1. execute mode only
2. `RepoVerifier.VerifyAsync()` runs `dotnet build` and `dotnet test` (TRX) itself, plus the advisory check: **the harness runs them, not the model**, so the prompt can't switch them off
3. green → `CommitGreenAsync` and `Stop()`
4. same failure 3 times → `RestoreLastGreenAsync` and `Stop()`
5. red → `Continue(result.ToFeedback())`

If typing goes wrong: `scripts/goto-step.sh 3 --state after-mechanical-fix --force`.

**3. The fix:** `dotnet run --project src/UpgradeCaptain.Console -- --state after-mechanical-fix`: it runs the code you just typed, on a fresh demo repo (the `--state` flag resets it). `/mode execute` and the **same prompt**, «… No ejecutes los tests …».
1. The harness runs the tests anyway: `🔎 Verificación del harness … 8 de 24 fallan …`.
2. The agent edits `RoutePlanner.cs` (Domain): a normal console prompt, which David approves.
3. The agent edits `FareCalculator.cs` (Payments): `⏸ Pendiente de aprobación de @maria-payments (APR-0001)`, and the console waits.
4. In a second terminal ("María's laptop"):
   - `scripts/approve.sh next --as @david-lorenzo` is **refused**: not an owner, and he is the requester.
   - `scripts/approve.sh next --as @maria-payments --reason "Revisado: tarifa en km"` → David's console continues.
   - Usually 2 approvals per run, one per Payments file (D-38). Repeat the last command (↑ + Enter) for every new `⏸`.
5. `✅ Verde verificado por el harness: build ✅ · tests 24/24 … Checkpoint abc1234 en upgrade/teide-geo-2.0`.
6. Optional: `scripts/approve.sh --audit` → `✔ Cadena de hashes verificada`; in Aspire, the `upgrade_captain.verify` span.

Rehearse without a model: `UC_INTEGRATION=1 dotnet test tests/UpgradeCaptain.Harness.Tests --filter Step3Beat` plays this beat with a scripted model (8 s).

---

## step-4 · Hosted Agent (optional)

**Goal:** the same factory, unattended, in Foundry. The session scales to zero while it waits for María and resumes on new compute with `$HOME` intact. First thing to cut if time is short: a slide with the screenshots works too.

- `UpgradeCaptainOptions.ForHosted(home)`:
  - shell and file access are still on, confined to `$HOME/work/canarias-tours`
  - the shell has an allow list (`dotnet`, `git status|diff|log|add|commit`)
  - `CodeAct = Local`
  - state lives in `$HOME/state`
  - unprotected changes are auto-approved, so only protected paths park (D-16)
- `UpgradeInvocationHandler` handles three events: `upgrade.requested`, `approval.decided`, `job.status`. The response is always `{ jobId, status, pendingApprovals[], lastVerification, reportPath }`.
- Approver identity: on container protocol **2.0.0** Foundry adds `x-agent-user-id` to every request (`context.PlatformContext.UserIdKey`), mapped through `demo/identities.json`. With one account, the trigger and the approval carry the same id, so the deployed agent runs with `UPGRADE_CAPTAIN_DEV_IDENTITY=1` and the payload's `approver` wins (D-39).

```bash
scripts/hosted.sh serve --local                               # terminal 1 (local rehearsal)
scripts/hosted.sh trigger --local                             # job-001, requested by @dependabot
scripts/hosted.sh status --local --watch                      # running → waiting_approval
scripts/hosted.sh approve APR-0001 --as @maria-payments --local
```

Deploy to Foundry (it does with `az`, step by step, what `azd deploy` does: remote linux/amd64 build in ACR, `AcrPull` for the project identity, agent version over REST with invocations 2.0.0, 2 CPU / 4 GiB and a 120 s idle timeout):

```bash
scripts/deploy-hosted.sh            # needs UPGRADE_CAPTAIN_ACR in .env
```

Then use the same `hosted.sh` commands without `--local`. The endpoint is `<FOUNDRY_PROJECT_ENDPOINT>/agents/upgrade-captain/endpoint/protocols`, with `api-version=v1` and `agent_session_id` in the query (D-39).
