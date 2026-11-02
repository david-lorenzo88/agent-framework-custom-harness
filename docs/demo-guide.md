# Demo guide · step by step

A detailed version of [run-of-show.md](run-of-show.md). For each step: the goal, what to tell the audience, and every command or action, with the terminal where it runs. The code behind each step is in [steps.md](steps.md).

Four steps (D-44). The first delivery had eight and ran over 50 minutes; the old tags are kept as `legacy/step-N`. Only the evaluator is typed live; steps 1 and 2 are shown as diffs (`scripts/diff-step.sh N`), and step 4 is the buffer.

## The use case

**Canarias.Tours** is a small .NET solution (in `demo-workspace/canarias-tours`, rebuilt from `demo/canarias-tours-template` by `reset-demo.sh`) with three projects: `Api`, `Domain` and `Payments`. It uses **Teide.Geo**, a made-up geo library, to compute route distances and tour fares.

- A security advisory (`TEIDE-2026-0042`, a ReDoS in `GeoFormat.Parse`) affects `Teide.Geo < 2.0.0`. Dependabot opens the bump, the PR goes red, and nobody picks it up.
- Teide.Geo 2.0.0 has **four breaking changes**:
  1. `GeoPoint` moves to `Teide.Geo.Primitives` → CS0246.
  2. `GetElevation` becomes `GetElevationAsync` → an async cascade up to the API.
  3. `GeoFormat.ToDms` disappears → `GeoPoint.ToString("dms")`.
  4. **`GeoCalculator.Distance` now returns metres instead of km.** It compiles. Only the tests notice.
- **The trap:** after the bump and the three mechanical fixes, the build is green but 8 of 24 tests fail (6 `FareTests`, 2 `DistanceTests`). Example: Los Cristianos → Teide costs 30.663,76 € instead of 33,66 €.
- **Ownership** (`CODEOWNERS`, last match wins): everything belongs to `@david-lorenzo`, except `/src/Canarias.Tours.Payments/`, which belongs to `@maria-payments`. Fixing the fare bug means touching Payments, so María has to approve it.

**Upgrade Captain** is the agent that takes that bump to green: inventory the breaking points → fix them → build and run the tests → get the **code owner's** approval for protected code → write a report (`UPGRADE_REPORT.md`).

## The harness we build

"Custom harness" here means **Agent Framework's built-in `HarnessAgent` (1.24.0) plus a few pieces plugged into its documented extension points**. It is not a loop written from scratch. Steps 1–2 use only built-in parts; step 3 adds the custom ones; step 4 hosts the same thing in Foundry.

| Step | What gets added | Built-in or custom |
|---|---|---|
| (slide) | A plain `ChatClientAgent`: model + prompt, no tools, no memory | built-in |
| 1 · Harness | `AsHarnessAgent`: 5 read-only tools, persisted session, compaction, OTel, plan mode by default, a loop that keeps going until the todos are done | built-in |
| 2 · Approve | Confined shell with a deny list, file edits, human approval prompts, skills as files (playbook + script), traces in Aspire. CodeAct is in the code (`--surface codeact`) but goes on a slide | built-in + preview packages |
| 3 · Tu harness | The four custom pieces that fix the "wall" (below). `step-3-pre` has all but the evaluator, which is typed live | **custom** |
| 4 · Hosted (optional) | The same factory behind an Invocations host in Foundry, unattended, scale to zero | built-in hosting + custom state |

The four walls of the built-in harness, and the custom piece that fixes each one in step 3:

| Wall | Custom piece | Extension point |
|---|---|---|
| "Done" is whatever the model says (or whatever the user's prompt allows) | `VerifiedCompletionEvaluator`: done = build green + tests green, run by the harness | `LoopEvaluators` |
| The person who asked is the one who approves | `CodeOwnerApprovalAgent` + `CodeOwnersPolicy`, `ApprovalStore`, `AuditLog` (hash chain): approval comes from the code owner, never the requester | outer `DelegatingAIAgent` |
| No undo | `GitCheckpoints`: commit on green, restore last green on repeated failure or rejection | used by the evaluator and the approval agent |
| Compaction can drop exact errors; no cost cap | `RepoStateProvider` (fresh repo state each turn), `BudgetGuard` (token cap) | `AIContextProviders`, inner `DelegatingChatClient` |

The shape of the final agent (more in [architecture.md](architecture.md)):

```
CodeOwnerApprovalAgent          custom: parks protected edits for the owner, audit log
  └─ HarnessAgent               built-in: loop, approvals, modes, todos, skills, file access, OTel
       ├─ LoopEvaluators: TodoCompletion (built-in) → VerifiedCompletion (custom)
       ├─ context providers: built-in ones + RepoStateProvider (custom)
       └─ chat pipeline → BudgetGuard (custom) → CompactingChatClient → Foundry (Responses API)
```

## The three terminals

| Terminal | Who it plays | Working directory | What runs there | Steps |
|---|---|---|---|---|
| **T1 · David** | You, the developer who asks for the upgrade (the *requester*) | repo root | `goto-step.sh` / `diff-step.sh`, the console (`dotnet run --project src/UpgradeCaptain.Console`), every console approval prompt, `hosted.sh status` in step 4 | all |
| **T2 · María's laptop** | María, owner of the Payments code (`@maria-payments` in CODEOWNERS), the only one who may approve changes there | repo root, **different colour scheme** so the audience sees it's "another person" | `scripts/approve.sh …` in step 3, `hosted.sh approve …` in step 4 | 3, 4 |
| **T3 · plain shell** | A neutral observer: "what does reality say?" Nothing agent-related runs here | `demo-workspace/canarias-tours` (the repo the agent edits) | `dotnet build` (refreshes `obj/`), `dotnet test` | 3 |

Why three:
- **T1 vs T2** is the core of step 3: *the approver is whoever owns the code, never whoever asked for the change*. If you approved Payments edits in T1, the point would be lost. T2 being visibly different sells "this is María, not me". Both run on the same laptop; identity is the `--as` handle.
- **T3** proves the agent closed without checking (the wall) without involving the agent. It is a plain `dotnet test` in the demo repo, no harness, no model.

Also open: **VS Code** (live coding the evaluator, font 20 pt) and the browser with **Aspire** (http://localhost:18888) and, for step 4, **App Insights**.

> **`OTEL_EXPORTER_OTLP_ENDPOINT` must be exported in T1**, the shell that runs the console. Exporting it in another tab does nothing.

## Before the session (T-60)

All in **T1**, repo root:

```bash
scripts/prewarm.sh                          # builds every tag, warms dotnet test, model call, Aspire, hosted
scripts/aspire.sh --stop                    # clears old traces (Aspire keeps them in memory)
scripts/aspire.sh                           # Aspire up: http://localhost:18888
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
scripts/hosted.sh trigger                   # ≥ 30 min before the talk
scripts/hosted.sh status                    # after ~2 min → waiting_approval (APR-0001 for @maria-payments)
                                            # then DON'T call it again until step 4: every call wakes the session
git status                                  # must be clean (the IDE sometimes adds demo projects to UpgradeCaptain.slnx)
scripts/goto-step.sh 1                      # the stage starts at step-1
```

- **T2**: open a new tab at the repo root, switch its colour profile. Leave it idle.
- **T3**: `cd demo-workspace/canarias-tours`. Leave it idle.

---

## Opening (0:00, 7 min)

Slides: the problem, **"a model that only talks"**, harness anatomy, built-in vs opt-in, the Upgrade Captain scenario.

The plain `ChatClientAgent` is now a slide, not a step: `GetResponsesClient().AsIChatClient(model)` + `AsAIAgent(new ChatClientAgentOptions { ChatOptions = new() { Instructions = Prompt } })` + a `ReadLine` loop with no session. Say it: "Ask it to do the job and it explains how *you* would do it. It doesn't even remember what I just said: there is no session." `scripts/diff-step.sh 1` shows exactly what the harness adds over that agent.

---

## Step 1 · Harness (7:00, 8 min)

**Goal:** one call (`AsHarnessAgent`) brings the whole harness: tools, persisted session, compaction, telemetry, plan mode, and a loop that doesn't stop until the todos are done.

**Code to show (`src/UpgradeCaptain.Console/Program.cs`, `scripts/diff-step.sh 1`):**
- The client chain: `.AsIChatClientWithStoredOutputDisabled(model)` (history kept by the harness, not the service), `.WithInterruptedToolCallRepair()` (D-37), `.WithContextWindowCompaction(maxContextWindowTokens, maxOutputTokens)` (D-07).
- **`chatClient.AsHarnessAgent(new HarnessAgentOptions { ... })`**: the one line that brings the loop.
- Inside the options: `Tools = UpgradeTools.All` (5 read-only tools), `DisableWebSearch = true`, **`DefaultMode = "plan"`**, **`LoopEvaluators = [ new TodoCompletionLoopEvaluator(...) { Modes = ["execute"] } ]`**, **`MaxIterations = 10`**, `FileMemoryStore`, `OpenTelemetrySourceName = Telemetry.SourceName`.
- `HarnessConsole.RunAgentAsync(...)`: streaming, `/todos`, `/mode`, `/session-export`.

**Tell the audience:**
- The diff is small: `AsHarnessAgent` + five read-only tools + the vendored console.
- `DefaultMode = "plan"`: it can propose, not act. `TodoCompletionLoopEvaluator`: in execute mode, if todos remain open, the harness sends it back to work. `MaxIterations = 10` is the safety cap.
- **Lesson from preparing the talk:** server-side storage is *off* (`AsIChatClientWithStoredOutputDisabled`); with `store` on, client-side compaction does nothing. Compaction runs on every model call through `CompactingChatClient`, because the harness's in-loop compaction drops messages in 1.24.0 (D-07).
- **Line to say:** "The plan is a contract you can edit before anything runs."

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | T1 | Already at `step-1` (from T-60). `dotnet run --project src/UpgradeCaptain.Console` |
| 2 | T1 | Prompt: «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido» → several tool calls in one turn (`list_projects`, `list_source_files`, `read_source`…), then todos and a question. (Keep «con el informe final incluido»: without it there's no report task to remove.) **30–110 s: talk over it** |
| 3 | T1 | The plan ends in a list: `> Approve and switch to execute mode` / `✏️ Type a custom response...`. **Don't type yet**: press **↓** to the custom-response row, type «El informe lo escribo yo, quítalo» and press **Enter**. (Enter on the first row approves straight away) |
| 4 | T1 | `/todos` → the edited plan. `/mode` → still `plan` |
| 5 | T1 | On the corrected plan, leave the cursor on `Approve and switch to execute mode` and press **Enter** → the loop runs until the todos are done or 10 iterations |
| 6 | T1 | `/exit` |

**Optional, only if ahead of time** (it was step 1 of the first version):
- `/session-export sessions/demo.json`, `/exit`, relaunch, `/session-import sessions/demo.json`, «¿Qué encontraste?» → "The session is just data."
- Compaction: `dotnet run --project src/UpgradeCaptain.Console -- --context tiny` (16k window / 8k output), the inventory prompt, then a follow-up («¿Y cuáles de esos usos están en tests?»). In Aspire, on the follow-up's first `chat gpt-5.4` span: `upgrade_captain.compaction` with `compacted = true`, `messages.before` 15 → `messages.after` 11, and the `compaction.compact` child (`ToolResultCompactionStrategy`, ~7,050 → ~5,750 tokens). **Show the span, not the token chart**: the `📊 Tokens` line is overwritten after every call. Measured 2026-10-20; numbers vary per run.

---

## Step 2 · Approve (15:00, 8 min)

**Goal:** the agent can act (shell, file edits), but only inside the demo repo and with a human in the loop; know-how lives in skills it loads only when needed; everything shows up in one trace.

**Code to show (`Program.cs`, `scripts/diff-step.sh 2`, plus the `skills/` folder):**
- **`new LocalShellExecutor(new LocalShellExecutorOptions { ... })`**: `WorkingDirectory = paths.RepoDir`, `ConfineWorkingDirectory = true`, `CleanEnvironment = true`, `Timeout = 120 s`, `Policy = new ShellPolicy(denyList: [ rm -rf, sudo, git push, dotnet nuget push ])`. `shell.AsAIFunction(requireApproval: true)`: `run_shell` always asks.
- `FileAccessStore = new FileSystemAgentFileStore(paths.RepoDir)`: `file_access_*` confined to the repo.
- **`AgentSkillsSource = new AgentFileSkillsSource([paths.SkillsDir], new DotnetScriptRunner(paths.RepoDir).RunAsync)`**: skills from files; `.cs` scripts run with `dotnet run script.cs`.
- `AutoApprovalRules = [ FileAccessProvider.ReadOnlyToolsAutoApprovalRule, AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule ]`: reading files and loading skills don't ask; writing and `run_skill_script` do.
- Open `skills/nuget-upgrade/SKILL.md`: the frontmatter (`name`, `description`) is all the model sees until it calls `load_skill`.

**Tell the audience:**
- **Line to say:** "The deny list is a UX filter, not a security boundary. The human approval is the gate, and the policy still applies after the human says yes."
- In Aspire: `invoke_agent → chat → execute_tool`, with token attributes. No OTel code here: it's been on since step 1.

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | T1 | `scripts/goto-step.sh 2`. Check `echo $OTEL_EXPORTER_OTLP_ENDPOINT` → `http://localhost:4317` |
| 2 | T1 | `dotnet run --project src/UpgradeCaptain.Console` |
| 3 | T1 | «Sube Teide.Geo a 2.0.0. De momento solo la versión.» → loads `nuget-upgrade`. If the plan list appears first, **Approve and switch to execute mode** (the prompt is scoped, so it only bumps). Then `🔐 Tool approval: run_skill_script` (`bump_package.cs`) → **Approve this call**. If anything else prompts afterwards (a `file_access_*` edit), **Deny**: it's going beyond the version. (Keep «De momento solo la versión»: without it the agent goes on to migrate the code) |
| 4 | T1 | «Ejecuta git push origin main» (if it asks for confirmation: «Sí, ejecútalo») → approve → **blocked**: `Command rejected by policy: matched deny pattern: \bgit\s+push\b` |
| 5 | Browser | Aspire: the trace tree, with `load_skill` and `run_skill_script` |
| 6 | T1 | `/exit` |
| 7 | — | **CodeAct slide** (1 min): the two traces side by side |

**The CodeAct slide** (the code is in this tag behind `--surface codeact`; not run live):
- Direct = a fan of `execute_tool read_source` spans; CodeAct = one `execute_tool execute_code` span.
- «Directo: el modelo es el bucle, y cada vuelta pasa por él con todo el contexto. CodeAct: el modelo escribe el bucle y solo ve el resultado. En este repo de 14 ficheros da igual; en uno de 2.000, es la diferencia entre llenar el contexto o no.» Don't promise fewer tokens: on this repo it wasn't cheaper in rehearsal.
- **Line to say:** "LocalCodeActProvider runs Python on this laptop: **not a sandbox**. Hyperlight needs KVM or WHP, so not on a Mac."
- **If someone says "so it's Code Interpreter":** «Code Interpreter es una calculadora en un sandbox del proveedor; CodeAct es el agente usando código como forma de llamar a *sus* herramientas: en vez de 13 llamadas a `read_source`, escribe un script que hace el bucle.»

**Optional, only if ahead of time:** «En el README, cambia "excursiones por Tenerife" por "excursiones por Tenerife y La Palma"» → approve the edit; then «Añade al final del README la línea "Mantenido por el equipo de Plataforma."» → **Deny** → the agent adapts. "Deny is not an error, it's information for the agent."

---

## Step 3 · Tu harness (23:00, 14 min)

The heart of the talk: **a custom verifier that makes the harness run the tests even though the user's prompt says not to.**

### 3a · The wall (4 min)

**Goal:** show where the built-in harness stops being enough. It takes the model's word for "done", and the model takes the user's word.

**Code to show:** almost none. `step-3-pre` has the factory and every custom piece **except** the evaluator. The point is what's *missing*.

**Tell the audience:**
- Slide **"El muro"**.
- The repo already compiles: bump + mechanical fixes applied. But Teide.Geo 2.0 silently changed `Distance` to return metres instead of km.
- "The model isn't bad. I tell it «no ejecutes los tests», it obeys, writes the report and closes, and *nothing checks*. It even tells you it didn't run the tests, and the harness accepts that."

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | T1 | `scripts/goto-step.sh 3-pre --state after-mechanical-fix` |
| 2 | **T3** | `dotnet build` → green. **Required**: it refreshes `obj/`. The agent builds with `--no-restore`, so a stale `obj/` makes it report a failing build (NU1101) that isn't real. Also shows "ya compila" on screen before the prompt |
| 3 | T1 | `dotnet run --project src/UpgradeCaptain.Console`, then `/mode execute` |
| 4 | T1 | «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.» → approve the report write when it prompts → it says it's done |
| 5 | T1 | `/exit` |
| 6 | **T3** | `dotnet test` → **8 red**: e.g. 30.663,76 € instead of 33,66 € (metres priced as if they were km). Pause on that number |

### 3b · Live coding the evaluator (4 min)

**Goal:** fix the first wall with ~30 lines in a documented extension point. Then reveal the other three.

**Code to show:**
- `Program.cs` is short: the agent is built by **`UpgradeCaptainFactory.Create(UpgradeCaptainOptions.ForLocal(...))`** (`src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs`), the same factory step 4 uses.
- **You type** `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs` (`uc3-evaluator`): a `LoopEvaluator` that, in execute mode, runs `verifier.VerifyAsync` → green: `CommitGreenAsync` + `Stop()`; stuck: `RestoreLastGreenAsync` + `Stop()`; red: `Continue(result.ToFeedback())`.
- **You type** in the factory (`uc3-wire`): `evaluators.Add(new VerifiedCompletionEvaluator(services.Verifier, services.Cache, services.Checkpoints));`, after `TodoCompletionLoopEvaluator`.
- **Reveal** (already written, slide "Tu harness a medida"): `CodeOwnerApprovalAgent` (outer `DelegatingAIAgent`), `GitCheckpoints`, `RepoStateProvider` (`AIContextProviders`), `BudgetGuard` (inner `DelegatingChatClient`).

**Tell the audience:**
- **Line to say:** "The tests are run by the harness, not by the model. The prompt can't switch them off."
- **Line to say:** "This is not a new loop. Four pieces, four documented extension points."

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | VS Code | Create `src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs`, type `uc3-evaluator`, save. Explain the five branches |
| 2 | VS Code | Open `src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs`, in `BuildHarnessOptions` select the `// uc3: …` line (without indentation), type `uc3-wire`, save |
| 3 | — | Slide "Tu harness a medida" |

### 3c · The fix (6 min)

**Goal:** the same prompt as the wall, «no ejecutes los tests» included; now the harness checks anyway, and the code owner approves her code.

**Code to show:** none new. It runs **the code you just typed**. If asked, point at `CodeOwnerApprovalAgent.cs` (the `⏸ … @maria-payments` comes from there).

**Tell the audience:**
- "Same prompt. It still says not to run the tests. The harness runs them anyway."
- **Line to say:** "The approver is whoever owns the code, never whoever asked for the change."

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | T1 | `dotnet run --project src/UpgradeCaptain.Console -- --state after-mechanical-fix` → it builds what you just typed and resets the demo repo (no checkout, so the typed files stay) |
| 2 | **T3** | `dotnet build` → green (**required** after every reset: refreshes `obj/`) |
| 3 | T1 | `/mode execute`, then the **same prompt** as 3a |
| 4 | T1 | `🔎 Verificación del harness … 8 de 24 fallan …` → the agent goes back to work |
| 5 | T1 | It edits `RoutePlanner.cs` (Domain, not protected) → normal prompt → **you approve in T1** |
| 6 | T1 | It edits `FareCalculator.cs` (Payments) → `⏸ Pendiente de aprobación de @maria-payments (APR-0001)` → T1 waits |
| 7 | **T2** | `scripts/approve.sh next --as @david-lorenzo` → **refused**: not the owner, and he's the requester |
| 8 | **T2** | `scripts/approve.sh next --as @maria-payments --reason "Revisado: tarifa en km"` → T1 continues |
| 9 | **T2** | Each new `⏸` in T1 (usually 2 in total: one per Payments file) → ↑ + Enter in T2 |
| 10 | T1 | `✅ Verde verificado por el harness: build ✅ · tests 24/24 … Checkpoint … en upgrade/teide-geo-2.0` |
| 11 | T1 | `/exit` |
| 12 | *(optional)* | T2: `scripts/approve.sh --audit` → `✔ Cadena de hashes verificada`. Aspire: the `upgrade_captain.verify` span. T3: `dotnet test` → all green, without the agent |

---

## Step 4 · Hosted Agent (37:00, 5 min, optional)

**If you're past 38:00, skip to the close** and show the screenshots slide instead.

**Goal:** the same factory, unattended, in Foundry. The job you triggered at T-60 has been parked on María's approval; its compute was released, and it resumes on new compute with `$HOME` intact.

**Code to show** (`scripts/diff-step.sh 4`, no checkout needed: `hosted.sh` works from any step):
- **`UpgradeCaptainOptions.ForHosted(home)`**: same factory, different options: shell allow list, CodeAct local, requester `@dependabot`, `Attended = false` (protected edits are parked for their owner), state in `$HOME/state`.
- **`AgentHost.CreateBuilder(args)`** + **`host.AddInvocations<UpgradeInvocationHandler>()`**: the Invocations webhook (`POST /invocations`, `/readiness`, port 8088).
- **`host.ConfigureTracing(t => t.AddSource(Telemetry.SourceName, "Experimental.Microsoft.Agents.AI"))`**: the only telemetry line; App Insights comes from the connection string Foundry injects (D-43).

**Tell the audience:**
- Same `UpgradeCaptainFactory`, behind an Invocations host: `upgrade.requested`, `approval.decided`, `job.status`.
- The requester here is `@dependabot`, not a person. María approves just the same.
- Scale to zero: while waiting for a human it costs no compute.
- **Line to say (dev identity, D-39):** "In this demo María's identity comes in the payload because I only have one account here. In production it comes from `x-agent-user-id`."

**Do:**

| # | Where | Action |
|---|---|---|
| 1 | T1 | `scripts/hosted.sh status` → `waiting_approval`, APR-0001 for @maria-payments |
| 2 | **T2** | `scripts/hosted.sh approve APR-0001 --as @maria-payments --reason "Revisado: tarifa en km"` → resumes on new compute |
| 3 | T1 | `scripts/hosted.sh status --watch` → usually APR-0002 (`TourPackagePricing.cs`) appears |
| 4 | **T2** | `scripts/hosted.sh approve APR-0002 --as @maria-payments --reason "Revisado: tarifa en km"` |
| 5 | T1 | `--watch` → `green` (~1 min), `reportPath` set; the report lists both approvals |
| 6 | Browser | App Insights: the trace of the hosted run |

---

## Close (42:00)

Slides: harness vs your own loop, takeaways, resources, questions. Nothing in the terminals.

## If something goes wrong

| Symptom | Fix |
|---|---|
| `❌ Stream error` (connection reset, 100 s timeout) | Console survives, session is valid: type «Sigue» |
| `goto-step.sh` says "Hay cambios sin commitear" | Re-run with `--force` |
| `goto-step.sh` / `git` says "untracked working tree files would be overwritten" | `rm` the file it names (usually the `VerifiedCompletionEvaluator.cs` you typed), re-run with `--force` |
| Typing the evaluator goes wrong | `rm src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs && scripts/goto-step.sh 3 --state after-mechanical-fix --force` gives you exactly the finished tag; continue with 3c from row 2 |
| Agent edits Payments via `run_shell` (`sed -i`, `perl -pi`) | Still parks for María: any shell command naming a protected path counts. Say "the shell is opaque, so the harness is conservative" |
| Step-2 bump doesn't use the skill | Check the prompt has «De momento solo la versión.» and that `goto-step.sh 2` ran |
| No traces in Aspire | `OTEL_EXPORTER_OTLP_ENDPOINT` isn't exported **in T1**; export it and relaunch the console |
| Running late | Drop step 4 (screenshots slide) and the optional rows; never cut 3a–3c |
