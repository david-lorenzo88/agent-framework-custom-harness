#!/usr/bin/env bash
# Llamada mínima al modelo de Foundry (Responses API) para comprobar endpoint, despliegue y credenciales.
# Lee FOUNDRY_PROJECT_ENDPOINT y FOUNDRY_MODEL de .env. Usa el token de `az login` (no toca recursos).
#
# Uso: scripts/smoke-model.sh [modelo]
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env

MODEL="${1:-${FOUNDRY_MODEL:-gpt-5.4}}"
[[ -n "${FOUNDRY_PROJECT_ENDPOINT:-}" ]] || die "FOUNDRY_PROJECT_ENDPOINT no está definido (copia .env.example a .env)"

TOKEN="$(az account get-access-token --scope https://ai.azure.com/.default --query accessToken -o tsv 2>/dev/null)" \
  || die "No hay token de Azure: ejecuta az login"

# El SDK de proyectos añade /openai/v1 al endpoint del proyecto; hacemos lo mismo.
URL="${FOUNDRY_PROJECT_ENDPOINT%/}/openai/v1/responses"
OUT="$(mktemp "${TMPDIR:-/tmp}/uc-smoke.XXXXXX")"
trap 'rm -f "$OUT"' EXIT
START=$SECONDS
HTTP_CODE=$(curl -sS -o "$OUT" -w '%{http_code}' "$URL" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"model\":\"$MODEL\",\"input\":\"Responde solo: ok\",\"max_output_tokens\":16,\"store\":false}")
ELAPSED=$((SECONDS - START))

number() { grep -o "\"$1\"[[:space:]]*:[[:space:]]*[0-9]*" "$OUT" | head -1 | grep -o '[0-9]*$'; }
if [[ "$HTTP_CODE" == "200" ]]; then
  ok "Modelo $MODEL responde en ~${ELAPSED}s (in=$(number input_tokens) out=$(number output_tokens))"
else
  fail "Modelo $MODEL: HTTP $HTTP_CODE"
  head -c 600 "$OUT"; echo
  if [[ "$HTTP_CODE" == "403" ]]; then
    warn "¿La cuenta por defecto de az es la del proyecto de Foundry? DefaultAzureCredential usa la de 'az account show'."
    warn "Cámbiala con: az account set --subscription <suscripción del recurso de Foundry>"
  fi
  exit 1
fi
