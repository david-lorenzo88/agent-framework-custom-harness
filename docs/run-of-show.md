# Run of show · netcoreconf Madrid, 23 Oct 2026

50-minute slot, about 47 minutes of content. Terminal at 20 pt, VS Code at 20 pt (`.vscode/settings.json`).

## Before the session (T-60 min)

```bash
scripts/prewarm.sh                         # everything green? (builds every tag, warms dotnet test, model call, Aspire, hosted)
scripts/aspire.sh                          # http://localhost:18888
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
scripts/hosted.sh trigger                  # ≥ 30 min before: the stage job must be parked and idle
scripts/hosted.sh status                   # → waiting_approval (APR for @maria-payments), about 2 min after trigger
                                           # then STOP calling it: every call wakes the session. After 2 min idle
                                           # its compute is released, and the approval on stage resumes on new compute
git switch live                            # starts at step-0
```

Terminals:
- **T1 David**, repo root.
- **T2 María's laptop**, repo root, different colour scheme.
- **T3 plain shell** in `demo-workspace/canarias-tours`, for `dotnet test`.

## Timeline

| Time | Block | Commands and prompts |
|---|---|---|
| 0:00 | Opening → step map | ~8 min of slides: the problem, harness anatomy, built-in vs opt-in, the Upgrade Captain scenario |
| 8:00 | **Steps 0–1** (6 min) | `goto-step.sh 0` → `dotnet run --project src/UpgradeCaptain.Console` → «Actualiza Teide.Geo a 2.0 en Canarias.Tours» (it can only explain). Live: `live-step.sh 1` + snippet `uc1-harness` (or `goto-step.sh 1`). «Haz inventario de dónde usamos Teide.Geo». `/session-export sessions/demo.json`, `/exit`, relaunch, `/session-import sessions/demo.json`. Then `-- --context tiny` (16k/8k) and the inventory again: input tokens jump to ~12k while it reads files and drop to ~4.5k on the next call (compaction). The inventory takes 30–110 s with gpt-5.4: talk over it |
| 14:00 | **Step 2** (5 min) | `live-step.sh 2` + `uc2-plan`. «Prepara la actualización de Teide.Geo a 2.0, con el informe final incluido». Correct the plan out loud: «el informe lo escribo yo, quítalo». `/todos`, `/mode`, then «adelante, pasa a execute» |
| 19:00 | **Step 3** (6 min) | `live-step.sh 3` + `uc3-shell`. «Compila la solución» → *Always approve this tool with these arguments*. An approved edit, a denied edit. «Ejecuta git push origin main» (if it asks for confirmation: «Sí, ejecútalo») → approve → **blocked by the policy** |
| 25:00 | **Steps 4–5** (7 min) | Aspire already up. `goto-step.sh 4`. Inventory prompt on `--surface direct`, then `--surface codeact`; in Aspire compare the shape (a fan of `read_source` spans vs one `execute_code`), not the token totals. `goto-step.sh 5`: «¿Qué cambia en Teide.Geo 2.0 y cómo lo arreglamos?» (skill loads on demand), «Sube Teide.Geo a 2.0.0. De momento solo la versión.» (`load_skill nuget-upgrade`, then `bump_package.cs` prompts) |
| 32:00 | **Step 6** (9 min) | Slide **"El muro"**. **Wall:** `goto-step.sh 5 --state after-mechanical-fix`, `/mode execute`, «Ya compila. Cierra la actualización: escribe UPGRADE_REPORT.md y listo. No ejecutes los tests, ya los pasará CI.» → it says it's done → T3: `dotnet test` → 8 red (30.663,76 € vs 33,66 €). **Code:** `goto-step.sh 6-pre`, type `uc6-evaluator` into `VerifiedCompletionEvaluator.cs`, `uc6-wire` into the factory; reveal the rest (slide "Tu harness a medida"). **Fix:** `goto-step.sh 6 --state after-mechanical-fix`, `/mode execute`, same prompt → harness feedback → Domain edit (approve in T1) → `⏸ … @maria-payments (APR-0001)` → T2: `approve.sh next --as @david-lorenzo` (refused: not the owner, and he asked for the job) → `approve.sh next --as @maria-payments --reason "Revisado: tarifa en km"` → T1 continues. Each new `⏸` in T1 (usually 2: one per Payments file, D-38) → T2: ↑ + Enter → ✅ green + checkpoint → T2: `approve.sh --audit` → Aspire: `upgrade_captain.verify` |
| 41:00 | **Step 7** (5 min) | `goto-step.sh 7`. `hosted.sh status` → `waiting_approval`. `hosted.sh approve APR-0001 --as @maria-payments --reason "Revisado: tarifa en km"` (say it: with one account, María's identity comes from the payload, dev identity mode, D-39) → resumes on new compute with `$HOME` intact → `hosted.sh status --watch` → usually APR-0002 (`TourPackagePricing.cs`): approve it the same way → `green` (about 1 min), `reportPath` set, the report lists both approvals. Trace in App Insights |
| 46:00 | Close | Harness vs your own loop, takeaways, resources, questions |

## Lines to say

- Step 3: "The deny list is a UX filter, not a security boundary. The human approval is the gate, and the policy still applies after the human says yes."
- Step 4: "LocalCodeActProvider runs Python on this laptop: **not a sandbox**. Hyperlight needs KVM or WHP, so not on a Mac."
- Step 6: "This is not a new loop. Four pieces, four documented extension points."
- Step 6: "The approver is whoever owns the code, never whoever asked for the change."
- Step 7 (if the dev identity fallback is used): "In this demo María's identity comes in the payload because I only have one account here. In production it comes from `x-agent-user-id`."

## If something goes wrong on stage

- **`❌ Stream error` (connection reset, 100 s network timeout).** Seen 4 times in about 25 runs on 2026-10-13. The console survives and the session stays valid: type «Sigue» (D-36, D-37).
- **The agent edits Payments through `run_shell` (`sed -i`, `perl -pi`).** It still parks for @maria-payments, because any shell command that names a protected path counts as touching it (D-34). Say so: "the shell is opaque, so the harness is conservative".
- **The step-5 bump doesn't use the skill.** Check that the prompt includes «De momento solo la versión.», and that `goto-step.sh 5` copied `skills/`.

## Reset between rehearsals

```bash
scripts/reset-demo.sh                          # or --state after-mechanical-fix
scripts/approve.sh --list                      # should say: no approvals
```
