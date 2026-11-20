#!/usr/bin/env bash
# Reconstruye la cadena de pasos de la charla (tags step-1, step-2, step-3-pre, step-3 y step-4, rama "steps")
# encima de main. No reescribe main ni borra nada: la cadena anterior queda como previous/step-N y
# previous/steps. Ver docs/verification-log.md, D-44.
#
#   scripts/rebuild-steps.sh                          tras cambiar algo en main (código compartido, tests, scripts, docs)
#   scripts/rebuild-steps.sh --step1-from wip-step1   con un Program.cs del step-1 cambiado en otra rama
#   scripts/rebuild-steps.sh --step2-from wip-step2   ídem para el step-2
#
# De dónde sale cada paso:
#   · ficheros comunes (scripts, docs, demo, vendor, .vscode…): siempre de main
#   · step-1 y step-2: src/ y la solución salen de la tag actual (o de --stepN-from), pero los ficheros compartidos
#     del Core, CliArgs.cs, Headless.cs, DotnetScriptRunner.cs y skills/ se refrescan desde main
#   · step-3-pre, step-3 y step-4: salen de main (que es el step-4). step-3 quita el Hosted Agent y deploy/;
#     step-3-pre y step-3 quitan también la cabina web (src/UpgradeCaptain.Web, que tiene todos los pasos dentro);
#     step-3-pre, además, quita el evaluador (que se escribe en directo con el snippet uc3-evaluator)
#
# Para cambiar el Program.cs del step-1 o del step-2:
#   git switch -c wip-step1 step-1      # edita, compila, haz commit
#   git switch main
#   scripts/rebuild-steps.sh --step1-from wip-step1
#
# Cada paso se compila (y los que tienen tests, se prueban) antes de mover las tags. Si algo falla, no se
# toca ninguna tag ni rama. Necesita main sin cambios sin commitear.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

STEP1_FROM="step-1"; STEP2_FROM="step-2"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --step1-from) STEP1_FROM="$2"; shift 2 ;;
    --step2-from) STEP2_FROM="$2"; shift 2 ;;
    *) die "Argumento desconocido: $1 (usa --step1-from <ref> o --step2-from <ref>)" ;;
  esac
done

cd "$ROOT"
[[ -z "$(git status --porcelain --untracked-files=no)" ]] || die "Hay cambios sin commitear: haz commit en main antes de reconstruir"
for ref in main "$STEP1_FROM" "$STEP2_FROM"; do
  git rev-parse -q --verify "$ref^{commit}" >/dev/null || die "No existe $ref"
done

TAGS=(step-1 step-2 step-3-pre step-3 step-4)
TRAILER="Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
SHARED_CORE=(DemoLanguage.cs DemoPaths.cs DotEnv.cs Prompts.cs Telemetry.cs UpgradeTools.cs CompactingChatClient.cs ToolCallRepairChatClient.cs)
STEP1_SRC="$(git rev-parse "$STEP1_FROM^{commit}")"
STEP2_SRC="$(git rev-parse "$STEP2_FROM^{commit}")"

# pwd -P: ruta real. En macOS $TMPDIR es un enlace simbólico y el restore de NuGet pierde las ProjectReference (D-30).
WT="$(cd "$(mktemp -d "${TMPDIR:-/tmp}/uc-rebuild.XXXXXX")" && pwd -P)/wt"
cleanup() {
  git -C "$ROOT" worktree remove --force "$WT" >/dev/null 2>&1 || true
  git -C "$ROOT" worktree prune
  git -C "$ROOT" branch -D steps-new >/dev/null 2>&1 || true
}
trap cleanup EXIT
git branch -D steps-new >/dev/null 2>&1 || true
git worktree add -q -b steps-new "$WT" main
cd "$WT"

reset_specific() { git rm -rqf --ignore-unmatch src tests skills deploy UpgradeCaptain.slnx; rm -rf src tests skills deploy UpgradeCaptain.slnx; }
shared_from_main() { for f in "${SHARED_CORE[@]}"; do git checkout main -- "src/UpgradeCaptain.Core/$f"; done; git checkout main -- src/UpgradeCaptain.Console/CliArgs.cs; }
drop_hosted() { rm -rf src/UpgradeCaptain.Hosted; perl -i -ne 'print unless /UpgradeCaptain\.Hosted/' UpgradeCaptain.slnx; }
# La cabina web (src/UpgradeCaptain.Web) tiene todos los pasos dentro: vive en main (= step-4), no en las tags anteriores.
drop_web() { rm -rf src/UpgradeCaptain.Web; perl -i -ne 'print unless /UpgradeCaptain\.Web/' UpgradeCaptain.slnx; }
build() {
  dotnet build UpgradeCaptain.slnx --nologo -v q -clp:ErrorsOnly >"$WT.build.log" 2>&1 || { cat "$WT.build.log"; die "No compila $1"; }
}
run_tests() {
  dotnet test tests/UpgradeCaptain.Harness.Tests --no-build --nologo -v q >"$WT.test.log" 2>&1 || { tail -30 "$WT.test.log"; die "Fallan los tests de $1"; }
  info "$(grep -E 'Correctas|Passed!' "$WT.test.log" | tail -1 | sed 's/^ *//')"
}
commit_step() {
  git add -A
  git commit -q -m "$2" -m "$3" -m "$TRAILER"
  printf '%s %s\n' "$1" "$(git rev-parse HEAD)" >>"$WT.commits"
  ok "$1 compila ($(git rev-parse --short HEAD))"
}
: >"$WT.commits"

# ── step-1 · Harness ─────────────────────────────────────────────────────────
reset_specific
git checkout "$STEP1_SRC" -- src UpgradeCaptain.slnx
shared_from_main
build step-1
commit_step step-1 "step-1 · Harness" "AsHarnessAgent con UpgradeTools.All, historial persistido, compactación (CompactingChatClient, D-07),
consola vendorizada, telemetría OTLP opcional, modo plan por defecto y TodoCompletionLoopEvaluator
en modo execute (MaxIterations = 10). El chat básico (legacy/step-0) va en una diapositiva."

# ── step-2 · Approve ─────────────────────────────────────────────────────────
reset_specific
git checkout "$STEP2_SRC" -- src UpgradeCaptain.slnx
git checkout main -- skills src/UpgradeCaptain.Console/Headless.cs src/UpgradeCaptain.Core/DotnetScriptRunner.cs
shared_from_main
build step-2
commit_step step-2 "step-2 · Approve" "LocalShellExecutor confinado con lista de denegación, FileAccessStore sobre el repo de la demo,
aprobaciones en consola (leer no pregunta), skills en ficheros ejecutadas con DotnetScriptRunner y
Aspire Dashboard. CodeAct (--surface codeact) queda en el código para la diapositiva."

# ── step-3-pre · Tu harness sin el evaluador ─────────────────────────────────
reset_specific
git checkout main -- src tests skills UpgradeCaptain.slnx
drop_hosted
drop_web
git rm -qf src/UpgradeCaptain.Harness/VerifiedCompletionEvaluator.cs \
  tests/UpgradeCaptain.Harness.Tests/VerifiedCompletionEvaluatorTests.cs \
  tests/UpgradeCaptain.Harness.Tests/Step3BeatSimulationTests.cs
perl -0pi -e 's{            // "Terminado" = compila y pasan los tests\. Va después del evaluador de todos\.\n            evaluators\.Add\(new VerifiedCompletionEvaluator\(services\.Verifier, services\.Cache, services\.Checkpoints\)\);\n}{            // uc3: aquí va el evaluador ("terminado" = compila y pasan los tests), después del de todos.\n}' \
  src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs
grep -q "// uc3: aquí va el evaluador" src/UpgradeCaptain.Core/UpgradeCaptainFactory.cs \
  || die "No encuentro la línea de cableado del evaluador en UpgradeCaptainFactory.cs (¿ha cambiado en main?)"
perl -pi -e 's{LoopEvaluators = \["TodoCompletionLoopEvaluator", "VerifiedCompletionEvaluator"\],}{LoopEvaluators = ["TodoCompletionLoopEvaluator"], // step-3 añade VerifiedCompletionEvaluator}' \
  tests/UpgradeCaptain.Harness.Tests/FactoryEquivalenceTests.cs
grep -q 'step-3 añade VerifiedCompletionEvaluator' tests/UpgradeCaptain.Harness.Tests/FactoryEquivalenceTests.cs \
  || die "No encuentro LoopEvaluators en FactoryEquivalenceTests.cs (¿ha cambiado en main?)"
perl -pi -e 's{Assert\.Equal\(\["TodoCompletionLoopEvaluator", "VerifiedCompletionEvaluator"\], step3\.}{Assert.Equal(["TodoCompletionLoopEvaluator"], step3.}' \
  tests/UpgradeCaptain.Harness.Tests/EnglishTests.cs
grep -q 'Assert.Equal(\["TodoCompletionLoopEvaluator"\], step3\.' tests/UpgradeCaptain.Harness.Tests/EnglishTests.cs \
  || die "No encuentro los LoopEvaluators del step-3 en EnglishTests.cs (¿ha cambiado en main?)"
build step-3-pre
run_tests step-3-pre
commit_step step-3-pre "step-3-pre · Tu harness (sin el evaluador, para escribirlo en directo)" "Factoría UpgradeCaptainFactory y piezas a medida (CodeOwnerApprovalAgent, GitCheckpoints,
RepoStateProvider, BudgetGuard, ApprovalStore, SessionVault, AuditLog) sin VerifiedCompletionEvaluator.
Aquí se ve el muro: el agente obedece «no ejecutes los tests» y cierra sin comprobar nada."

# ── step-3 · Tu harness ──────────────────────────────────────────────────────
reset_specific
git checkout main -- src tests skills UpgradeCaptain.slnx
drop_hosted
drop_web
build step-3
run_tests step-3
commit_step step-3 "step-3 · Tu harness" "VerifiedCompletionEvaluator: «terminado» = compila y pasan los tests, ejecutados por el harness,
aunque el prompt diga lo contrario. Una línea de cableado en la factoría."

# ── step-4 · Hosted Agent (= main) ───────────────────────────────────────────
reset_specific
git checkout main -- src tests skills deploy UpgradeCaptain.slnx
build step-4
run_tests step-4
commit_step step-4 "step-4 · Hosted Agent" "La misma factoría detrás de un host Invocations (upgrade.requested, approval.decided, job.status),
Dockerfile, agent.manifest.yaml y scripts/deploy-hosted.sh."
git diff --quiet main HEAD || die "step-4 no coincide con main"

# ── Todo verde: se mueven las tags (la cadena anterior queda como previous/*) ──
cd "$ROOT"
for tag in "${TAGS[@]}"; do
  if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
    git tag -f "previous/$tag" "$tag" >/dev/null
    git tag -d "$tag" >/dev/null
  fi
done
if git rev-parse -q --verify refs/heads/steps >/dev/null; then
  git branch -f previous/steps steps >/dev/null
fi
while read -r tag commit; do
  git tag -a "$tag" "$commit" -m "$(git log -1 --format=%s "$commit")"
done <"$WT.commits"
git branch -f steps steps-new >/dev/null
ok "Tags movidas: ${TAGS[*]} (rama steps). La cadena anterior está en previous/step-N y previous/steps"
info "Para borrar la copia anterior: git tag -l 'previous/*' | xargs git tag -d && git branch -D previous/steps"
