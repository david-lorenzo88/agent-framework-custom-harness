#!/usr/bin/env bash
# Comprueba el entorno de la máquina del ponente (PLAN.md fase 0).
#   .NET 10, git, Docker, az (con sesión), azd + extensión azure.ai.agents, arquitectura.
#   python3 ≥ 3.11 SOLO para CodeAct (step-2): LocalCodeActProvider de Agent Framework ejecuta Python.
#   Con --model además hace una llamada mínima al modelo (scripts/smoke-model.sh).
#
# Uso: scripts/check-env.sh [--model]
set -uo pipefail
source "$(dirname "$0")/lib.sh"
load_env

FAILED=0
check() { if eval "$2" >/dev/null 2>&1; then ok "$1"; else fail "$1"; FAILED=1; fi; }

info "Arquitectura: $(uname -m) · macOS $(sw_vers -productVersion 2>/dev/null || uname -r)"

SDKS="$(dotnet --list-sdks 2>/dev/null | grep -E '^10\.' | awk '{print $1}' | tr '\n' ' ')"
PINNED="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$ROOT/global.json" | head -1)"
if [[ -n "$SDKS" ]]; then ok ".NET 10 SDK: $SDKS(global.json pide $PINNED)"; else fail ".NET 10 SDK no instalado"; FAILED=1; fi

check "git $(git --version 2>/dev/null | awk '{print $3}')" "command -v git"

# python3 solo para CodeAct (step-2). Ningún script ni componente de Upgrade Captain está escrito en Python.
PY="$(python3 --version 2>/dev/null | awk '{print $2}')"
PY_MAJOR="${PY%%.*}"; PY_MINOR="$(echo "$PY" | cut -d. -f2)"
if [[ -n "$PY" && "$PY_MAJOR" -eq 3 && "${PY_MINOR:-0}" -ge 11 ]]; then
  ok "python3 $PY (solo para CodeAct, step-2)"
else
  warn "python3 ≥ 3.11 no encontrado (${PY:-ninguno}): solo lo necesita CodeAct (step-2)"
fi

if docker info >/dev/null 2>&1; then ok "Docker $(docker info --format '{{.ServerVersion}}') en marcha"; else fail "Docker no está en marcha (necesario para Aspire Dashboard)"; FAILED=1; fi

if AZ_USER="$(az account show --query user.name -o tsv 2>/dev/null)"; then ok "az con sesión: $AZ_USER"; else fail "az sin sesión (az login)"; FAILED=1; fi

if command -v azd >/dev/null; then
  ok "azd $(azd version 2>/dev/null | awk '{print $3}')"
  if azd extension list --installed 2>/dev/null | grep -q 'azure.ai.agents'; then ok "extensión azd azure.ai.agents"; else warn "extensión azd azure.ai.agents no instalada (solo para el step-4): azd extension install azure.ai.agents"; fi
else
  fail "azd no instalado"; FAILED=1
fi

[[ "${AZURE_TOKEN_CREDENTIALS:-}" == "dev" ]] && ok "AZURE_TOKEN_CREDENTIALS=dev" || warn "AZURE_TOKEN_CREDENTIALS no es 'dev' (DefaultAzureCredential probará la identidad administrada)"
[[ -n "${FOUNDRY_PROJECT_ENDPOINT:-}" ]] && ok "FOUNDRY_PROJECT_ENDPOINT definido · modelo ${FOUNDRY_MODEL:-gpt-5.4}" || warn "FOUNDRY_PROJECT_ENDPOINT sin definir en .env"

if [[ "${1:-}" == "--model" ]]; then
  "$ROOT/scripts/smoke-model.sh" || FAILED=1
fi

exit $FAILED
