#!/usr/bin/env bash
# Salta a un paso de la charla: checkout de la tag step-N, reset del repo de la demo y build.
#
#   scripts/goto-step.sh 2              → tag step-2, demo en estado clean
#   scripts/goto-step.sh 3-pre --state after-mechanical-fix
#                                       → step-3 sin el evaluador: el muro, y luego escribirlo en directo (snippet uc3-evaluator)
#   scripts/goto-step.sh 2 --force      → descarta cambios sin commitear
#
# Pasos: 1 harness (plan/execute) · 2 approve (shell, ficheros, skills, OTel; CodeAct con --surface) · 3 tu harness · 4 hosted
set -euo pipefail
source "$(dirname "$0")/lib.sh"

# bash lee el script a medida que lo ejecuta y el checkout puede reemplazarlo: nos copiamos a /tmp primero.
if [[ "${UC_GOTO_COPY:-}" != "1" ]]; then
  copy_dir="$(mktemp -d "${TMPDIR:-/tmp}/goto-step.XXXXXX")"
  cp "$0" "$copy_dir/goto-step.sh"
  cp "$(dirname "$0")/lib.sh" "$copy_dir/lib.sh"
  UC_GOTO_COPY=1 UC_ROOT="$ROOT" exec bash "$copy_dir/goto-step.sh" "$@"
fi
ROOT="${UC_ROOT:-$ROOT}"

STEP="${1:-}"; shift || die "Uso: scripts/goto-step.sh <1-4|3-pre> [--state clean|after-mechanical-fix] [--force]"
STATE="clean"; FORCE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --state) STATE="$2"; shift 2 ;;
    --force) FORCE=1; shift ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done

cd "$ROOT"
TAG="step-$STEP"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null || die "No existe la tag $TAG"

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  [[ $FORCE -eq 1 ]] || die "Hay cambios sin commitear. Usa --force para descartarlos (o haz commit en tu rama)."
  git reset -q --hard
fi

git checkout -q "$TAG"
ok "En $TAG ($(git log -1 --format=%s))"

bash "$ROOT/scripts/reset-demo.sh" --state "$STATE" --quiet
ok "Demo en estado $STATE"

if dotnet build "$ROOT/UpgradeCaptain.slnx" --nologo -v q -clp:ErrorsOnly >/tmp/uc-goto-build.log 2>&1; then
  ok "Build verde"
else
  cat /tmp/uc-goto-build.log; die "El build falla en $TAG"
fi

info "Consola: dotnet run --project src/UpgradeCaptain.Console$( [[ "$STEP" == 4 ]] && echo "   ·   Hosted: scripts/hosted.sh serve --local")"
