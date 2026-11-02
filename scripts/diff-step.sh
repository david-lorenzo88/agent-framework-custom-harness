#!/usr/bin/env bash
# Muestra qué añade un paso respecto al anterior (lo que cuenta cada diapositiva).
#   scripts/diff-step.sh 2            → git diff step-1 step-2 -- src tests skills deploy
#   scripts/diff-step.sh 3 --stat
#   scripts/diff-step.sh 1            → lo que añade el harness al agente que solo conversa (legacy/step-0)
set -euo pipefail
source "$(dirname "$0")/lib.sh"
STEP="${1:-}"; shift || die "Uso: scripts/diff-step.sh <1-4|3-pre> [opciones de git diff]"
case "$STEP" in
  1) PREV="legacy/step-0" ;;
  2) PREV="step-1" ;;
  3-pre) PREV="step-2" ;;
  3) PREV="step-3-pre" ;;
  4) PREV="step-3" ;;
  *) die "Paso desconocido: $STEP (usa 1, 2, 3-pre, 3 o 4)" ;;
esac
git -C "$ROOT" diff "$@" "$PREV" "step-$STEP" -- src tests skills deploy UpgradeCaptain.slnx
