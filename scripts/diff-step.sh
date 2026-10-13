#!/usr/bin/env bash
# Muestra qué añade un paso respecto al anterior (lo que cuenta cada diapositiva).
#   scripts/diff-step.sh 3            → git diff step-2 step-3 -- src tests skills deploy
#   scripts/diff-step.sh 6 --stat
set -euo pipefail
source "$(dirname "$0")/lib.sh"
STEP="${1:-}"; shift || die "Uso: scripts/diff-step.sh <1-7|6-pre> [opciones de git diff]"
case "$STEP" in
  6-pre) PREV="step-5" ;;
  6) PREV="step-6-pre" ;;
  *) PREV="step-$((STEP - 1))" ;;
esac
git -C "$ROOT" diff "$@" "$PREV" "step-$STEP" -- src tests skills deploy UpgradeCaptain.slnx
