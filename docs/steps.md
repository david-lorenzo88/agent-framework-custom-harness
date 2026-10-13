# How the harness grows, step by step

Each step is a **git tag**. Every tag builds and runs on its own:

```bash
scripts/goto-step.sh 3            # checkout step-3, reset the demo repo, build
dotnet run --project src/UpgradeCaptain.Console
```

`scripts/diff-step.sh N` shows exactly what step N adds (`git diff step-(N-1) step-N`). The slide for each step is that diff.

| Tag | Slide | Adds | Built-in or custom | Main file |
|---|---|---|---|---|
| `step-0` | Chat básico | `ChatClientAgent`, no tools, no session | built-in | `src/UpgradeCaptain.Console/Program.cs` |
| `step-1` | Harness | `AsHarnessAgent`, `UpgradeTools.All`, persisted history, compaction, OTel | built-in | `Program.cs` |
| `step-2` | Plan / execute | `DefaultMode = "plan"`, `TodoCompletionLoopEvaluator`, `MaxIterations` | built-in | `Program.cs` |
| `step-3` | Approve | `LocalShellExecutor` + deny list, `FileAccessStore`, approval rules | built-in + preview package | `Program.cs` |
| `step-4` | CodeAct | `LocalCodeActProvider`, `--surface direct\|codeact` | preview package | `Program.cs` |
| `step-5` | Skills y OTel | `AgentFileSkillsSource` + script runner, Aspire | built-in | `Program.cs`, `skills/` |
| `step-6-pre` | (live coding) | Factory + every custom piece **except** the evaluator | custom | `src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs` |
| `step-6` | Tu harness | `VerifiedCompletionEvaluator` + one wiring line | **custom** | `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs` |
| `step-7` | Hosted Agent | Same factory behind an Invocations host | built-in hosting + custom state | `src/UpgradeCaptain.Hosted/` |

What exists in **every** tag (the base commit): the demo data (`demo/`), the scripts, the vendored console (`vendor/`), the docs and `UpgradeCaptain.Core`, which holds:
- the Spanish `Prompts.Prompt`
- the five read-only tools in `UpgradeTools.All`
- `DemoPaths`, `DotEnv`, `Telemetry`
- `CompactingChatClient` (see D-07)

---

## step-0 · Chat básico

**Goal:** show what an agent is *before* the harness. It can only talk.

```csharp
IChatClient chatClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential(), ...)
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(model);

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Prompt },
});

// plain Console.ReadLine loop → agent.RunAsync(input) with NO session
```

**Show:** «Actualiza Teide.Geo a 2.0 en Canarias.Tours». It explains how you *would* do it. Then «¿qué te acabo de pedir?»: it does not remember, because there is no session.

---

## step-1 · Harness

**Goal:** the same client, but `AsHarnessAgent` brings the whole harness.

Diff highlights:
- **Client without server-side storage:**

  ```csharp
  .GetProjectResponsesClientForModel(model)
  .AsIChatClientWithStoredOutputDisabled(model)
  .WithInterruptedToolCallRepair()
  .WithContextWindowCompaction(maxContextWindowTokens, maxOutputTokens);
  ```

  With `store` on, the service keeps the conversation and client-side compaction has no effect (Q5). The built-in compaction strategy runs on every model call through `CompactingChatClient`, because the harness's own in-loop compaction drops messages in 1.24.0 (D-07). `WithInterruptedToolCallRepair()` keeps the session valid after a network cut: a tool call whose result never got saved gets an "interrupted" result, so «Sigue» works (D-37). Both are good "lessons from preparing this talk" moments.
- **The harness:**

  ```csharp
  AIAgent agent = chatClient.AsHarnessAgent(new HarnessAgentOptions
  {
      ChatOptions = new ChatOptions { Instructions = Prompt, Tools = UpgradeTools.All },
      DisableWebSearch = true,
      AgentModeProviderOptions = new AgentModeProviderOptions { DefaultMode = "execute" },
      FileMemoryStore = new FileSystemAgentFileStore(Path.Combine(paths.ProjectRoot, "agent-file-memory")),
      OpenTelemetrySourceName = Telemetry.SourceName,
  });
  ```

- **The vendored console:** `HarnessConsole.RunAgentAsync` with `BuildObserversWithPlanning` (passing the context window values, so the usage line shows the budget) and `BuildDefaultCommandHandlers`.
- **Telemetry:** `Telemetry.StartIfConfigured()` exports over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. It never writes to the console.

**Show:**
1. «Haz inventario de dónde usamos Teide.Geo». Several tool calls in one turn (`list_projects`, `list_source_files`, `read_source`…).
2. `/session-export sessions/demo.json`, then `/exit`, relaunch, `/session-import sessions/demo.json`, and «¿qué encontraste?».
3. `dotnet run --project src/UpgradeCaptain.Console -- --context tiny` (16,000 / 8,000). Ask for the inventory again: input tokens climb to ~12k while it reads ~14 files and drop to ~4.5k on the next model call, once old tool results are collapsed (measured live, see verification log).

---

## step-2 · Plan / execute

**Goal:** plan first, execute only when told; then a loop that doesn't stop until the todos are done.

```csharp
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
```

**Show:** «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido» (without "informe" the plan has no report task to remove). It creates todos and asks for approval. Correct it out loud: «el informe lo escribo yo, quítalo». Then `/todos`, `/mode` (still `plan`), and «adelante, pasa a execute»: the loop runs until the todos are done or 10 iterations pass.

---

## step-3 · Approve

**Goal:** the agent can act, but only inside the demo repo and with a human in the loop.

```csharp
await using var shell = new LocalShellExecutor(new LocalShellExecutorOptions
{
    WorkingDirectory = paths.RepoDir,          // demo-workspace/canarias-tours, never this repo or $HOME
    ConfineWorkingDirectory = true,
    CleanEnvironment = true,                   // default is false: it would inherit FOUNDRY_*/AZURE_* (D-05)
    Timeout = TimeSpan.FromSeconds(120),       // default is no timeout
    Policy = new ShellPolicy(denyList: [@"\brm\s+-rf\b", @"\bsudo\b", @"\bgit\s+push\b", @"\bdotnet\s+nuget\s+push\b"]),
});
// Tools: [.. UpgradeTools.All, shell.AsAIFunction(requireApproval: true)]
// AIContextProviders: [new ShellEnvironmentProvider(shell)]
// FileAccessStore: new FileSystemAgentFileStore(paths.RepoDir)
// AutoApprovalRules: [FileAccessProvider.ReadOnlyToolsAutoApprovalRule]
```

**Show:**
1. «Compila la solución». `run_shell dotnet build` prompts; choose **"Always approve this tool with these arguments"**, never "any arguments".
2. An edit: `file_access_replace` prompts. Deny one and the agent adapts.
3. «Ejecuta git push origin main» (gpt-5.4 sometimes asks for confirmation first: «Sí, ejecútalo»). It prompts, you approve, and then the policy blocks it: `Command rejected by policy: matched deny pattern: \bgit\s+push\b`. The deny list applies even after a human says yes (D-13).

Say it plainly: the deny list is a UX filter, not a sandbox.

---

## step-4 · CodeAct

**Goal:** the same five tools, two surfaces.

```csharp
using LocalCodeActProvider? codeAct = cli.Surface == "codeact"
    ? new LocalCodeActProvider(Path.Combine(paths.ProjectRoot, ".venv", "bin", "python"),
        new LocalCodeActProviderOptions
        {
            Tools = UpgradeTools.All.OfType<AIFunction>(),
            ExecutionLimits = new ProcessExecutionLimits { TimeoutSeconds = 20 },
        })
    : null;
// direct:  Tools = [.. UpgradeTools.All, run_shell]
// codeact: Tools = [run_shell] + codeAct in AIContextProviders, instructions + CodeActHint

// After AsHarnessAgent: let the model see its own Python errors (D-32).
if (codeAct is not null)
{
    agent.GetService<FunctionInvokingChatClient>()!.IncludeDetailedErrors = true;
}
```

Two things make CodeAct work with a real model (both learned in rehearsal, D-32):

- **Errors.** When the generated Python fails, `LocalCodeActProvider` throws. `HarnessAgent`'s `FunctionInvokingChatClient` hides exceptions by default, so the model would only see «Error: Function failed.» and give up on `execute_code`. With `IncludeDetailedErrors` it gets the exception and traceback and fixes its code.
- **Signatures.** `execute_code`'s description lists each tool's name and description, never its parameters. `CodeActHint` gives the exact `call_tool(...)` signatures, and the tool descriptions document what each one returns (for example, `read_source` prefixes every line with `"   N: "`).

**Show:** with Aspire up, run «Haz inventario de las llamadas a APIs de Teide.Geo que rompen en 2.0: proyecto, fichero, línea y cambio necesario» on `--surface direct` and then `--surface codeact`. Compare the **shape** of the two traces: direct is a fan of `execute_tool read_source` spans, CodeAct is a single `execute_tool execute_code` span with the tool calls inside Python. Don't promise fewer tokens: on this small repo gpt-5.4 already batches the reads, and CodeAct wasn't cheaper in rehearsal (verification log, "Reading the surface numbers"). **`LocalCodeActProvider` is not a sandbox**: say it on stage (Hyperlight needs KVM or WHP).

Reference numbers: `scripts/measure-surfaces.sh` writes them to the verification log. They never go on slides.

---

## step-5 · Skills y OTel

**Goal:** package know-how as files the model loads on demand; see everything in one trace.

```csharp
AgentSkillsSource = new AgentFileSkillsSource([paths.SkillsDir], new DotnetScriptRunner(paths.RepoDir).RunAsync),
AutoApprovalRules =
[
    FileAccessProvider.ReadOnlyToolsAutoApprovalRule,
    AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,   // load/read skills: no prompt; run_skill_script: prompt
],
```

Skills:
- `skills/nuget-upgrade`: the playbook (CPM only, no prereleases, branch and commit format, report template) plus `scripts/bump_package.cs`, a .NET 10 file-based app that `DotnetScriptRunner` runs with `dotnet run bump_package.cs -- …` (clean environment, repo as working directory).
- `skills/teide-geo-migration`: `references/breaking-changes.md` and `assets/fix-recipes.md`.

**Show:**

```bash
scripts/aspire.sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

1. «¿Qué cambia en Teide.Geo 2.0 y cómo lo arreglamos?» loads `teide-geo-migration` and reads `breaking-changes.md`.
2. «Sube Teide.Geo a 2.0.0. De momento solo la versión.» loads `nuget-upgrade` and `bump_package.cs` prompts (the only approval). Without «De momento solo la versión» the agent goes on to migrate the code.
3. Aspire (http://localhost:18888) shows `invoke_agent → chat → execute_tool` with token attributes. Prompt text appears only with `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true`.

**The wall (end of step-5):**

```bash
scripts/goto-step.sh 5 --state after-mechanical-fix
```

Then `/mode execute` and «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.» The agent declares it done. In a plain terminal, `cd demo-workspace/canarias-tours && dotnet test` shows 8 red tests, for example a fare of 30.663,76 € instead of 33,66 €.

Why «Ya compila…» (D-33): with «Termina la actualización y ciérrala…», gpt-5.4 went looking for the silent `Distance` change by itself and fixed it, 3 runs out of 3. The prompt tells it to look for behavior changes, and the migration skill lists this one. The wall is not "the model is bad". It is "the harness takes the model's word": a human says «ya compila, ciérralo», the agent writes the report and closes, and nothing checks. The agent even says it didn't run the tests, and the built-in harness accepts that.

---

## step-6 · Tu harness

**Goal:** the four walls from slide "El muro", solved with the harness's own extension points. **It is not a new loop.**

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

With `EnableCustomHarness = false`, the factory produces exactly the step-5 agent; `FactoryEquivalenceTests` proves it.

**Live coding** (`scripts/goto-step.sh 6-pre`): open `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs` (new file) and type the `uc6-evaluator` snippet. Then, in `UpgradeCaptainFactory.BuildHarnessOptions`, replace the `// uc6:` comment with the `uc6-wire` snippet:

```csharp
evaluators.Add(new VerifiedCompletionEvaluator(services.Verifier, services.Cache, services.Checkpoints));
```

The evaluator, about 30 lines:
1. execute mode only
2. `RepoVerifier.VerifyAsync()` runs `dotnet build` and `dotnet test` (TRX), plus the advisory check
3. green → `CommitGreenAsync` and `Stop()`
4. same failure 3 times → `RestoreLastGreenAsync` and `Stop()`
5. red → `Continue(result.ToFeedback())`

**The fix beat:**

```bash
scripts/goto-step.sh 6 --state after-mechanical-fix
dotnet run --project src/UpgradeCaptain.Console
```

Then `/mode execute` and the **same prompt**.
1. The harness runs the tests anyway: `🔎 Verificación del harness … 8 de 24 fallan …`.
2. The agent edits `RoutePlanner.cs` (Domain): a normal console prompt, which David approves.
3. The agent edits `FareCalculator.cs` (Payments): `⏸ Pendiente de aprobación de @maria-payments (APR-0001)`, and the console waits.
4. In a second terminal ("María's laptop"):
   - `scripts/approve.sh next --as @david-lorenzo` is **refused**: not an owner, and he is the requester.
   - `scripts/approve.sh next --as @maria-payments --reason "Revisado: tarifa en km"` → David's console continues.
   - Each Payments edit parks on its own: 2 approvals per run in rehearsal, sometimes 4 (D-11, D-38). Repeat the last command (↑ + Enter) for every new `⏸`. `next` is the oldest pending approval; an explicit id (`APR-0001`) also works.
5. `✅ Verde verificado por el harness: build ✅ · tests 24/24 … Checkpoint abc1234 en upgrade/teide-geo-2.0`.
6. `scripts/approve.sh --audit` → `✔ Cadena de hashes verificada`. In Aspire: the `upgrade_captain.verify` span.

Rehearse without a model: `UC_INTEGRATION=1 dotnet test tests/UpgradeCaptain.Harness.Tests --filter Step6Beat` plays this exact beat with a scripted model (8 s).

---

## step-7 · Hosted Agent

**Goal:** the same factory, unattended, in Foundry. The session scales to zero while it waits for María and resumes on new compute with `$HOME` intact.

- `UpgradeCaptainOptions.ForHosted(home)`:
  - shell and file access are still on, confined to `$HOME/work/canarias-tours`
  - the shell has an allow list (`dotnet`, `git status|diff|log|add|commit`)
  - `CodeAct = Local`
  - state lives in `$HOME/state`
  - unprotected changes are auto-approved, so only protected paths park (D-16)
- `UpgradeInvocationHandler` handles three events: `upgrade.requested`, `approval.decided`, `job.status`. The response is always `{ jobId, status, pendingApprovals[], lastVerification, reportPath }`.
- Approver identity: on container protocol **2.0.0** Foundry adds `x-agent-user-id` to every request (`context.PlatformContext.UserIdKey`), mapped through `demo/identities.json`. With one account, the trigger and the approval carry the same id (yours), so the deployed agent runs with `UPGRADE_CAPTAIN_DEV_IDENTITY=1` and the payload's `approver` wins. Say so on stage (D-39).

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
