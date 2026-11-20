#!/usr/bin/env bash
# Compara las superficies direct y codeact de la tag step-2 (PLAN.md fase 5; en la charla, CodeAct va en una diapositiva).
# Ejecuta el mismo prompt N veces por superficie con el modo sin consola (--prompt) y registra
# llamadas al modelo (spans "chat"), llamadas a tools, tokens de entrada/salida y tiempo.
# Las salidas completas quedan en out/measure/ para comprobar que ambas encuentran los mismos puntos.
# Los números son referencia de ensayo: NUNCA van a las diapositivas.
#
# Uso: scripts/measure-surfaces.sh [--runs N] [--no-log]
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env

RUNS=3; LOG=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs) RUNS="$2"; shift 2 ;;
    --no-log) LOG=0; shift ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done

PROMPT="Haz inventario de las llamadas a APIs de Teide.Geo que rompen en 2.0: proyecto, fichero, línea y cambio necesario"
OUT="$ROOT/out/measure/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"

[[ -x "$ROOT/.venv/bin/python" ]] || "$ROOT/scripts/setup-venv.sh" >/dev/null

# Se mide el código de la tag step-2 (las dos superficies, sin el harness a medida) en un worktree temporal.
# UPGRADE_CAPTAIN_ROOT apunta a este repo: mismo .env, .venv y repo de la demo.
# pwd -P: ruta real. En macOS $TMPDIR es un enlace simbólico (/var → /private/var) y el restore de NuGet
# pierde las ProjectReference si la ruta del proyecto y la resuelta no coinciden.
WT="$(cd "$(mktemp -d "${TMPDIR:-/tmp}/uc-measure.XXXXXX")" && pwd -P)"
trap 'git -C "$ROOT" worktree remove --force "$WT" >/dev/null 2>&1; git -C "$ROOT" worktree prune' EXIT
git -C "$ROOT" worktree add -q --detach "$WT" step-2
dotnet build "$WT/src/UpgradeCaptain.Console" --nologo -v q -clp:ErrorsOnly >/dev/null || die "No compila la tag step-2"
export UPGRADE_CAPTAIN_ROOT="$ROOT"

ROWS=()
for surface in direct codeact; do
  for i in $(seq 1 "$RUNS"); do
    info "$surface · ejecución $i/$RUNS"
    file="$OUT/$surface-$i.txt"
    "$ROOT/scripts/reset-demo.sh" --quiet   # cada ejecución parte del mismo repo
    # cwd = el worktree: así nada del repo principal (sus skills/, su estado) se cuela en la medición.
    (cd "$WT" && dotnet run --no-build --project "$WT/src/UpgradeCaptain.Console" -- --surface "$surface" --prompt "$PROMPT") >"$file" 2>&1 || true
    metrics="$(grep '^METRICS' "$file" | tail -1 | sed 's/^METRICS //' || true)"
    [[ -n "$metrics" ]] || { fail "sin métricas (ver $file)"; continue; }
    ok "$metrics"
    ROWS+=("| $(date +%F) | $surface | $i | $(echo "$metrics" | sed -E 's/model_calls=([0-9]+) tool_calls=([0-9]+) input_tokens=([0-9]+) output_tokens=([0-9]+) wall_s=([0-9.]+)/\1 | \2 | \3 | \4 | \5/') |")
  done
done

if [[ $LOG -eq 1 && ${#ROWS[@]} -gt 0 ]]; then
  {
    echo ""
    echo "#### Surface measurement $(date '+%F %H:%M') · model ${FOUNDRY_MODEL:-gpt-5.4} · outputs in ${OUT/#$ROOT\//}"
    echo ""
    echo "| Date | Surface | Run | Model calls | Tool calls | Input tokens | Output tokens | Wall (s) |"
    echo "|---|---|---|---|---|---|---|---|"
    printf '%s\n' "${ROWS[@]}"
  } >> "$ROOT/docs/verification-log.md"
  ok "Resultados añadidos a docs/verification-log.md"
fi
info "Salidas completas: ${OUT/#$ROOT\//}"
