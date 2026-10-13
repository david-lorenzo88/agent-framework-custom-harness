#!/usr/bin/env bash
# Crea .venv/ (Python dedicado para CodeAct, PLAN.md fase 5). El runner de LocalCodeAct solo usa la stdlib.
# El step-4 pasa la ruta explícita: .venv/bin/python
#
# Uso: scripts/setup-venv.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"
if [[ ! -x "$ROOT/.venv/bin/python" ]]; then
  python3 -m venv "$ROOT/.venv"
fi
ok "Python de CodeAct: .venv/bin/python ($("$ROOT/.venv/bin/python" --version))"
