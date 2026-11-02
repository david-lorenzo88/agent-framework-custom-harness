#!/usr/bin/env bash
# Funciones comunes para los scripts de Upgrade Captain. Se carga con: source "$(dirname "$0")/lib.sh"
# Todos los scripts son bash y se ejecutan desde zsh en macOS (ver PLAN.md §5).

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export ROOT
WORKSPACE_DEFAULT="$ROOT/demo-workspace/canarias-tours"
FEED_DIR="$ROOT/demo/feed"

if [[ -t 1 ]]; then
  C_RESET=$'\033[0m'; C_GREEN=$'\033[32m'; C_RED=$'\033[31m'; C_YELLOW=$'\033[33m'; C_CYAN=$'\033[36m'; C_DIM=$'\033[2m'
else
  C_RESET=""; C_GREEN=""; C_RED=""; C_YELLOW=""; C_CYAN=""; C_DIM=""
fi

ok()   { printf '%s✔%s %s\n' "$C_GREEN" "$C_RESET" "$*"; }
fail() { printf '%s✘%s %s\n' "$C_RED" "$C_RESET" "$*"; }
warn() { printf '%s!%s %s\n' "$C_YELLOW" "$C_RESET" "$*"; }
info() { printf '%s›%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
die()  { fail "$*"; exit 1; }

# Carga .env (si existe) sin sobrescribir variables ya exportadas.
load_env() {
  local file="${1:-$ROOT/.env}"
  [[ -f "$file" ]] || return 0
  while IFS='=' read -r key value; do
    [[ -z "$key" || "$key" == \#* ]] && continue
    value="${value%\"}"; value="${value#\"}"
    if [[ -z "${!key:-}" ]]; then export "$key=$value"; fi
  done < <(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' "$file")
}
