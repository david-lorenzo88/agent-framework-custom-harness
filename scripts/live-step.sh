#!/usr/bin/env bash
# Prepara el siguiente paso para escribirlo EN DIRECTO en la rama live:
# trae de la tag step-N todo (csproj, skills, CliArgs...) MENOS Program.cs de la consola,
# que se escribe con el snippet ucN-* de .vscode/upgrade-captain.code-snippets.
# Si algo se tuerce: scripts/goto-step.sh N --force deja exactamente la tag.
#
#   scripts/live-step.sh 2
set -euo pipefail
source "$(dirname "$0")/lib.sh"

STEP="${1:-}"; [[ "$STEP" =~ ^[1-5]$ ]] || die "Uso: scripts/live-step.sh <1-5> (el 6 se revela con goto-step.sh 6-pre)"
cd "$ROOT"
TAG="step-$STEP"
PROGRAM="src/UpgradeCaptain.Console/Program.cs"

git ls-tree -r --name-only "$TAG" -- src skills tests UpgradeCaptain.slnx | grep -v "^$PROGRAM$" | while read -r file; do
  git checkout -q "$TAG" -- "$file"
done

ok "Ficheros de $TAG listos (salvo $PROGRAM)"
info "Ahora en VS Code: abre $PROGRAM, selecciona todo y escribe el snippet uc${STEP}-…"
info "Diferencias con la tag: git diff $TAG -- $PROGRAM"
