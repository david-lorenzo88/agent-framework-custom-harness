#!/usr/bin/env bash
# Antes de salir al escenario (PLAN.md fase 9). Imprime una lista verde/roja:
#   · build de todas las tags        · restore y dotnet test calientes en el repo de la demo
#   · llamada mínima al modelo       · Aspire Dashboard en marcha
#   · Python de CodeAct              · Hosted Agent: readiness y trabajo aparcado (si hay endpoint)
#
# Uso: scripts/prewarm.sh [--skip-tags] [--skip-hosted]
set -uo pipefail
source "$(dirname "$0")/lib.sh"
load_env

SKIP_TAGS=0; SKIP_HOSTED=0
for arg in "$@"; do
  case "$arg" in
    --skip-tags) SKIP_TAGS=1 ;;
    --skip-hosted) SKIP_HOSTED=1 ;;
  esac
done

RESULTS=()
record() { if [[ "$2" == 0 ]]; then RESULTS+=("${C_GREEN}✔${C_RESET} $1"); else RESULTS+=("${C_RED}✘${C_RESET} $1"); fi; }

cd "$ROOT"
"$ROOT/scripts/check-env.sh" >/dev/null 2>&1; record "Entorno (check-env.sh)" $?

if [[ $SKIP_TAGS -eq 0 ]]; then
  current="$(git rev-parse --abbrev-ref HEAD)"
  [[ "$current" == "HEAD" ]] && current="$(git rev-parse HEAD)"
  stash=0
  if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then git stash -q && stash=1; fi
  WT="$(cd "$(mktemp -d "${TMPDIR:-/tmp}/uc-tags.XXXXXX")" && pwd -P)"   # ruta real: ver measure-surfaces.sh
  for tag in step-0 step-1 step-2 step-3 step-4 step-5 step-6-pre step-6 step-7; do
    if git worktree add -q --detach "$WT/$tag" "$tag" 2>/dev/null; then
      (cd "$WT/$tag" && dotnet build UpgradeCaptain.slnx --nologo -v q -clp:ErrorsOnly >/dev/null 2>&1 \
        && test -f src/UpgradeCaptain.Console/bin/Debug/net10.0/upgrade-captain.dll); record "Build $tag" $?
      git worktree remove --force "$WT/$tag" >/dev/null 2>&1
    else
      record "Build $tag (no se pudo crear el worktree)" 1
    fi
  done
  rm -rf "$WT"
  git worktree prune
  [[ $stash -eq 1 ]] && git stash pop -q
fi

"$ROOT/scripts/pack-feed.sh" >/dev/null 2>&1; record "Feed de Teide.Geo" $?
"$ROOT/scripts/verify-trap.sh" >/dev/null 2>&1; record "Trampa determinista (verify-trap.sh)" $?
"$ROOT/scripts/reset-demo.sh" --quiet >/dev/null 2>&1 \
  && (cd "$WORKSPACE_DEFAULT" && dotnet test --nologo -v q >/dev/null 2>&1); record "Repo de la demo: restore + test en caliente" $?
dotnet test "$ROOT/tests/UpgradeCaptain.Harness.Tests" --nologo -v q >/dev/null 2>&1; record "Tests del harness a medida" $?
"$ROOT/scripts/setup-venv.sh" >/dev/null 2>&1; record "Python de CodeAct (.venv)" $?
# El script C# de la skill se compila la primera vez que se ejecuta: lo dejamos en la caché de dotnet run.
SKILL_SCRIPT="$ROOT/src/UpgradeCaptain.Console/bin/Debug/net10.0/skills/nuget-upgrade/scripts/bump_package.cs"
WARM="$(mktemp -d "${TMPDIR:-/tmp}/uc-warm.XXXXXX")"
dotnet build "$ROOT/src/UpgradeCaptain.Console" --nologo -v q >/dev/null 2>&1 \
  && cp "$ROOT/demo/canarias-tours-template/Directory.Packages.props" "$WARM/" \
  && dotnet run "$SKILL_SCRIPT" -- "$WARM/Directory.Packages.props" Teide.Geo 1.8.0 >/dev/null 2>&1; record "Script C# de la skill nuget-upgrade (dotnet run en caché)" $?
rm -rf "$WARM"
"$ROOT/scripts/smoke-model.sh" >/dev/null 2>&1; record "Modelo responde (${FOUNDRY_MODEL:-gpt-5.4})" $?
"$ROOT/scripts/aspire.sh" >/dev/null 2>&1 && curl -s -o /dev/null http://localhost:18888; record "Aspire Dashboard en http://localhost:18888" $?

if [[ $SKIP_HOSTED -eq 0 && -n "${UPGRADE_CAPTAIN_HOSTED_ENDPOINT:-}" ]]; then
  out="$("$ROOT/scripts/hosted.sh" status 2>/dev/null)"
  echo "$out" | grep -q '"status": "waiting_approval"'; record "Hosted: trabajo aparcado (waiting_approval)" $?
else
  RESULTS+=("${C_YELLOW}!${C_RESET} Hosted: sin UPGRADE_CAPTAIN_HOSTED_ENDPOINT (se omite)")
fi

echo ""
printf '%s\n' "${RESULTS[@]}"
