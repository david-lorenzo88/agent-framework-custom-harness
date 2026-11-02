#!/usr/bin/env bash
# step-4: despliega Upgrade Captain como Hosted Agent de Foundry. Hace con az, paso a paso, lo mismo que
# `azd deploy` (así no depende de la versión de azd, y en el escenario se ve qué pasa por debajo):
#   1. imagen linux/amd64 construida en Azure Container Registry (az acr build: sin Docker local)
#   2. la identidad del proyecto puede leer del registro (AcrPull; se asigna una sola vez)
#   3. versión nueva del agente por REST: protocolo invocations 2.0.0, 2 CPU / 4 GiB, inactividad 120 s
#   4. espera a que la versión esté "active"
#
#   scripts/deploy-hosted.sh                 construye y despliega
#   scripts/deploy-hosted.sh --tag <tag>     despliega una imagen ya construida (sin build)
#
# Lee de .env: FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL y UPGRADE_CAPTAIN_ACR (nombre del registro).
# Opcional: UPGRADE_CAPTAIN_AGENT_NAME (por defecto upgrade-captain).
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env

AGENT="${UPGRADE_CAPTAIN_AGENT_NAME:-upgrade-captain}"
ACR="${UPGRADE_CAPTAIN_ACR:-}"
ENDPOINT="${FOUNDRY_PROJECT_ENDPOINT%/}"
MODEL="${FOUNDRY_MODEL:-gpt-5.4}"
TAG=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="$2"; shift 2 ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done

[[ -n "$ENDPOINT" ]] || die "Falta FOUNDRY_PROJECT_ENDPOINT en .env"
[[ -n "$ACR" ]] || die "Falta UPGRADE_CAPTAIN_ACR en .env (nombre del Azure Container Registry, sin .azurecr.io)"

ACCOUNT="$(sed -E 's#^https://([^.]+)\..*#\1#' <<<"$ENDPOINT")"
PROJECT="${ENDPOINT##*/}"
API="api-version=v1"
LOGS="$ROOT/out/deploy"; mkdir -p "$LOGS"
foundry() { az rest --resource https://ai.azure.com "$@"; }   # plano de datos de Foundry (Agent Service)

# 1. Imagen ───────────────────────────────────────────────────────────────────
if [[ -z "$TAG" ]]; then
  TAG="$(git -C "$ROOT" rev-parse --short HEAD)-$(date +%Y%m%d%H%M%S)"
  info "1/4 · Construyendo $ACR.azurecr.io/$AGENT:$TAG en ACR (linux/amd64)… log: out/deploy/acr-build-$TAG.log"
  if ! az acr build -r "$ACR" --platform linux/amd64 -t "$AGENT:$TAG" -f "$ROOT/deploy/Dockerfile" "$ROOT" \
      > "$LOGS/acr-build-$TAG.log" 2>&1; then
    tail -30 "$LOGS/acr-build-$TAG.log"
    die "Falló la build en ACR"
  fi
  ok "Imagen construida"
else
  info "1/4 · Sin build: uso $ACR.azurecr.io/$AGENT:$TAG"
fi
IMAGE="$ACR.azurecr.io/$AGENT:$TAG"

# 2. El proyecto puede leer del registro ──────────────────────────────────────
ACCOUNT_ID="$(az resource list --name "$ACCOUNT" --resource-type Microsoft.CognitiveServices/accounts --query '[0].id' -o tsv)"
PRINCIPAL="$(az resource show --ids "$ACCOUNT_ID/projects/$PROJECT" --query identity.principalId -o tsv)"
ACR_ID="$(az acr show -n "$ACR" --query id -o tsv)"
if [[ -z "$(az role assignment list --assignee "$PRINCIPAL" --scope "$ACR_ID" --role AcrPull --query '[0].id' -o tsv)" ]]; then
  info "2/4 · Dando AcrPull en $ACR a la identidad del proyecto $PROJECT…"
  az role assignment create --assignee-object-id "$PRINCIPAL" --assignee-principal-type ServicePrincipal \
    --role AcrPull --scope "$ACR_ID" >/dev/null
  # Un rol recién asignado tarda en propagarse: sin esta espera la primera versión falla con ImageError (D-39).
  info "AcrPull asignado; espero 120 s a que se propague…"
  sleep 120
else
  ok "2/4 · La identidad del proyecto ya tiene AcrPull en $ACR"
fi

# 3. Versión del agente ───────────────────────────────────────────────────────
# UPGRADE_CAPTAIN_DEV_IDENTITY=1: con una sola cuenta, María aprueba con el campo "approver" (se dice en el escenario).
DEFINITION=$(cat <<JSON
{
  "kind": "hosted",
  "container_configuration": { "image": "$IMAGE" },
  "cpu": "2",
  "memory": "4Gi",
  "protocol_versions": [ { "protocol": "invocations", "version": "2.0.0" } ],
  "environment_variables": {
    "AZURE_AI_MODEL_DEPLOYMENT_NAME": "$MODEL",
    "UPGRADE_CAPTAIN_DEV_IDENTITY": "1"
  },
  "session_configuration": { "idle_timeout_seconds": 120 }
}
JSON
)
if foundry --method GET --url "$ENDPOINT/agents/$AGENT?$API" >/dev/null 2>&1; then
  info "3/4 · Creando una versión nueva de $AGENT…"
  VERSION="$(foundry --method POST --url "$ENDPOINT/agents/$AGENT/versions?$API" \
    --body "{\"definition\": $DEFINITION}" --query version -o tsv)"
else
  info "3/4 · Creando el agente $AGENT…"
  # Crear el agente devuelve el objeto agente: la versión está en versions.latest.
  VERSION="$(foundry --method POST --url "$ENDPOINT/agents?$API" \
    --body "{\"name\": \"$AGENT\", \"definition\": $DEFINITION}" --query versions.latest.version -o tsv)"
fi
[[ -n "$VERSION" ]] || die "Foundry no devolvió la versión creada"

# El endpoint del agente solo habla Responses por defecto: hay que activar Invocations. El PATCH sustituye la
# lista entera de protocolos y de esquemas de autorización, así que se mandan completas.
foundry --method PATCH --url "$ENDPOINT/agents/$AGENT?$API" --headers "Content-Type=application/merge-patch+json" \
  --body '{"agent_endpoint": {"protocol_configuration": {"invocations": {}}, "authorization_schemes": [{"type": "Entra"}]}}' \
  --query "agent_endpoint.protocols" -o tsv >/dev/null
ok "Endpoint del agente: protocolo invocations, autorización Entra"

# 4. Esperar a "active" ───────────────────────────────────────────────────────
info "4/4 · Esperando a que la versión $VERSION esté activa…"
for _ in $(seq 1 90); do
  STATUS="$(foundry --method GET --url "$ENDPOINT/agents/$AGENT/versions/$VERSION?$API" --query status -o tsv)"
  case "$STATUS" in
    active) ok "$AGENT v$VERSION activa · imagen $IMAGE"; break ;;
    failed)
      foundry --method GET --url "$ENDPOINT/agents/$AGENT/versions/$VERSION?$API" --query error -o json
      die "La versión $VERSION falló" ;;
  esac
  sleep 10
done
[[ "$STATUS" == "active" ]] || die "La versión $VERSION sigue en '$STATUS' tras 15 minutos"

info "Prueba: scripts/hosted.sh trigger   (endpoint: $ENDPOINT/agents/$AGENT/endpoint/protocols)"
