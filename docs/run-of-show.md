# Run of show · netcoreconf Madrid, 23 Oct 2026

50-minute slot, about 47 minutes of content. Terminal at 20 pt, VS Code at 20 pt (`.vscode/settings.json`).

## Before the session (T-60 min)

```bash
scripts/prewarm.sh                         # everything green? (builds every tag, warms dotnet test, model call, Aspire, hosted)
scripts/aspire.sh --stop                   # clears old traces (Aspire keeps them in memory)
scripts/aspire.sh                          # http://localhost:18888
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
scripts/hosted.sh trigger                  # ≥ 30 min before: the stage job must be parked and idle
scripts/hosted.sh status                   # → waiting_approval (APR for @maria-payments), about 2 min after trigger
                                           # then STOP calling it: every call wakes the session. After 2 min idle
                                           # its compute is released, and the approval on stage resumes on new compute
scripts/goto-step.sh 1                    # stage starts at step-1 (the plain chat agent is a slide)
```

Terminals:
- **T1 David**, repo root.
- **T2 María's laptop**, repo root, different colour scheme.
- **T3 plain shell** in `demo-workspace/canarias-tours`, for `dotnet test`.

## Timeline

Four steps (D-44). The first delivery had eight and ran long: now only the evaluator is typed live, steps 1 and 2 are shown with `scripts/diff-step.sh`, and step 4 is the buffer.

| Time | Block | Commands and prompts |
|---|---|---|
| 0:00 | Opening → step map | ~7 min of slides: the problem, "a model that only talks" (the plain chat agent), harness anatomy, built-in vs opt-in, the Upgrade Captain scenario |
| 7:00 | **Step 1 · Harness** (8 min) | Already at `step-1`; `diff-step.sh 1` on screen while explaining. `dotnet run --project src/UpgradeCaptain.Console`. «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido» → reads the repo, todos, asks (plan mode). Correct it out loud: «el informe lo escribo yo, quítalo». `/todos`, then «adelante, pasa a execute» → the loop runs to the end. The first answer takes 30–110 s with gpt-5.4: talk over it (history, compaction) |
| 15:00 | **Step 2 · Approve** (8 min) | Aspire already up. `goto-step.sh 2`, `diff-step.sh 2 --stat`. «Sube Teide.Geo a 2.0.0. De momento solo la versión.» → `load_skill nuget-upgrade` → `bump_package.cs` prompts → approve. «Ejecuta git push origin main» (if it asks for confirmation: «Sí, ejecútalo») → approve → **blocked by the policy**. Aspire: `invoke_agent → chat → execute_tool`. **CodeAct slide** (1 min): the two traces side by side |
| 23:00 | **Step 3 · Tu harness** (14 min) | Slide **"El muro"**. **Wall:** `goto-step.sh 3-pre --state after-mechanical-fix`, `/mode execute`, «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.» → it says it's done → T3: `dotnet test` → 8 red (30.663,76 € vs 33,66 €). **Code:** type `uc3-evaluator` into `VerifiedCompletionEvaluator.cs` (new file), `uc3-wire` into the factory; reveal the rest (slide "Tu harness a medida"). **Fix:** `dotnet run --project src/UpgradeCaptain.Console -- --state after-mechanical-fix` (runs the code you just typed, on a fresh demo repo), `/mode execute`, **the same prompt** («… No ejecutes los tests …») → `🔎 Verificación del harness … 8 de 24 fallan` → Domain edit (approve in T1) → `⏸ … @maria-payments (APR-0001)` → T2: `approve.sh next --as @david-lorenzo` (refused: not the owner, and he asked for the job) → `approve.sh next --as @maria-payments --reason "Revisado: tarifa en km"` → T1 continues; each new `⏸` (usually 2, D-38) → T2: ↑ + Enter → ✅ green + checkpoint |
| 37:00 | **Step 4 · Hosted** (5 min, optional) | `goto-step.sh 4`. `hosted.sh status` → `waiting_approval`. `hosted.sh approve APR-0001 --as @maria-payments --reason "Revisado: tarifa en km"` (say it: with one account, María's identity comes from the payload, dev identity mode, D-39) → resumes on new compute with `$HOME` intact → `hosted.sh status --watch` → usually APR-0002: approve it the same way → `green`, `reportPath` set. Trace in App Insights. **If you're past 38:00, skip to the close** and show the screenshots slide |
| 42:00 | Close | Harness vs your own loop, takeaways, resources, questions |

## Lines to say

- Step 1: "The plan is a contract you can edit before anything runs."
- Step 2: "The deny list is a UX filter, not a security boundary. The human approval is the gate, and the policy still applies after the human says yes."
- Step 2 (CodeAct slide): "LocalCodeActProvider runs Python on this laptop: **not a sandbox**. Hyperlight needs KVM or WHP, so not on a Mac."
- Step 3: "I told it not to run the tests. The harness runs them anyway: done means green, not 'the model says so'."
- Step 3: "This is not a new loop. Four pieces, four documented extension points."
- Step 3: "The approver is whoever owns the code, never whoever asked for the change."
- Step 4 (if the dev identity fallback is used): "In this demo María's identity comes in the payload because I only have one account here. In production it comes from `x-agent-user-id`."

## If something goes wrong on stage

- **`❌ Stream error` (connection reset, 100 s network timeout).** Seen 4 times in about 25 runs on 2026-10-13. The console survives and the session stays valid: type «Sigue» (D-36, D-37).
- **The agent edits Payments through `run_shell` (`sed -i`, `perl -pi`).** It still parks for @maria-payments, because any shell command that names a protected path counts as touching it (D-34). Say so: "the shell is opaque, so the harness is conservative".
- **The step-2 bump doesn't use the skill.** Check that the prompt includes «De momento solo la versión.», and that `goto-step.sh 2` copied `skills/`.
- **Typing the evaluator goes wrong.** `scripts/goto-step.sh 3 --state after-mechanical-fix --force` leaves exactly the finished tag.

## Reset between rehearsals

```bash
scripts/reset-demo.sh                          # or --state after-mechanical-fix
scripts/approve.sh --list                      # should say: no approvals
```
