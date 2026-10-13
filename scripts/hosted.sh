#!/usr/bin/env bash
# Habla con el Hosted Agent (step-7) por el protocolo Invocations.
#
#   scripts/hosted.sh serve  --local                          arranca el host en localhost:8088 (sin Docker)
#   scripts/hosted.sh trigger [--local] [--job <fichero>] [--wait]
#   scripts/hosted.sh approve <APR-id> --as <handle> [--reason <texto>] [--reject] [--local] [--wait]
#   scripts/hosted.sh status  [--local] [--job-id <id>] [--watch]
#
# Remoto (Foundry): <FOUNDRY_PROJECT_ENDPOINT>/agents/upgrade-captain/endpoint/protocols (o UPGRADE_CAPTAIN_HOSTED_ENDPOINT),
# token de `az login` y api-version=v1. Se despliega con scripts/deploy-hosted.sh.
# Local: http://localhost:8088. La identidad se simula con la cabecera x-agent-user-id (demo/identities.json).
# Sesión: agent_session_id = UPGRADE_CAPTAIN_SESSION_ID (por defecto, el jobId).
set -euo pipefail
source "$(dirname "$0")/lib.sh"
load_env

CMD="${1:-}"; shift || true
LOCAL=0; WAIT=false; WATCH=0; JOB_FILE="$ROOT/demo/jobs/upgrade-teide-geo.json"; JOB_ID=""; AS=""; REASON=""; DECISION="approve"; APR=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --local) LOCAL=1; shift ;;
    --wait) WAIT=true; shift ;;
    --watch) WATCH=1; shift ;;
    --job) JOB_FILE="$2"; shift 2 ;;
    --job-id) JOB_ID="$2"; shift 2 ;;
    --as) AS="$2"; shift 2 ;;
    --reason) REASON="$2"; shift 2 ;;
    --reject) DECISION="reject"; shift ;;
    APR-*) APR="$1"; shift ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done

# Valor de una clave de texto en un JSON plano ("clave": "valor"), sin herramientas externas.
json_value() { sed -n "s/.*\"$2\"[[:space:]]*:[[:space:]]*\"\\([^\"]*\\)\".*/\\1/p" "$1" | head -1; }
# Escapa una cadena para meterla entre comillas en JSON.
json_escape() { local s="${1//\\/\\\\}"; s="${s//\"/\\\"}"; printf '%s' "$s"; }

[[ -n "$JOB_ID" ]] || JOB_ID="$(json_value "$JOB_FILE" jobId)"
# Sesión (agent_session_id). En local vale el jobId. Foundry rechaza ids como "job-001" (invalid_session_id):
# trigger genera un UUID para el trabajo y lo guarda en out/hosted/<jobId>.session; status y approve lo reutilizan.
SESSION_FILE="$ROOT/out/hosted/$JOB_ID.session"
if [[ -n "${UPGRADE_CAPTAIN_SESSION_ID:-}" ]]; then
  SESSION="$UPGRADE_CAPTAIN_SESSION_ID"
elif [[ $LOCAL -eq 1 ]]; then
  SESSION="$JOB_ID"
elif [[ "$CMD" == trigger ]]; then
  mkdir -p "$(dirname "$SESSION_FILE")"
  SESSION="$(uuidgen | tr '[:upper:]' '[:lower:]')"
  printf '%s\n' "$SESSION" > "$SESSION_FILE"
elif [[ -f "$SESSION_FILE" ]]; then
  SESSION="$(cat "$SESSION_FILE")"
else
  SESSION=""
fi
LOCAL_HOME="$ROOT/out/hosted-home"
LOCAL_DEMO="$ROOT/out/opt-demo"

if [[ $LOCAL -eq 1 ]]; then
  BASE="http://localhost:8088"
  AUTH=()
  QUERY=""
else
  BASE="${UPGRADE_CAPTAIN_HOSTED_ENDPOINT:-${FOUNDRY_PROJECT_ENDPOINT%/}/agents/${UPGRADE_CAPTAIN_AGENT_NAME:-upgrade-captain}/endpoint/protocols}"
  [[ "$BASE" == https://* ]] || die "Define FOUNDRY_PROJECT_ENDPOINT o UPGRADE_CAPTAIN_HOSTED_ENDPOINT (o usa --local)"
  TOKEN="$(az account get-access-token --scope https://ai.azure.com/.default --query accessToken -o tsv)"
  AUTH=(-H "Authorization: Bearer $TOKEN")
  QUERY="api-version=v1&"   # Foundry exige api-version; agent_session_id va SIEMPRE en la query (Q2)
  [[ -n "$SESSION" || "$CMD" == serve ]] || die "No hay sesión para $JOB_ID: lanza 'scripts/hosted.sh trigger' o define UPGRADE_CAPTAIN_SESSION_ID"
  [[ "$CMD" != trigger ]] || info "Sesión de $JOB_ID: $SESSION (guardada en ${SESSION_FILE/#$ROOT\//})"
fi

post() {
  local body="$1"; shift
  curl -sS -X POST "$BASE/invocations?${QUERY}agent_session_id=$SESSION&wait=$WAIT" \
    -H "Content-Type: application/json" ${AUTH[@]+"${AUTH[@]}"} "$@" --data-binary "$body" \
    -w '\n(HTTP %{http_code})\n'
}

case "$CMD" in
  serve)
    [[ $LOCAL -eq 1 ]] || die "serve solo existe en local: scripts/hosted.sh serve --local"
    # Simula /opt/demo y $HOME del contenedor bajo out/ (nunca en tu $HOME real).
    if [[ ! -d "$LOCAL_DEMO/canarias-tours/.git" ]]; then
      mkdir -p "$LOCAL_DEMO/security" "$LOCAL_DEMO/feed"
      [[ -f "$FEED_DIR/Teide.Geo.2.0.0.nupkg" ]] || "$ROOT/scripts/pack-feed.sh" >/dev/null
      cp -R "$FEED_DIR/." "$LOCAL_DEMO/feed/"
      cp "$ROOT/demo/security/advisories.json" "$LOCAL_DEMO/security/"
      cp "$ROOT/demo/identities.json" "$LOCAL_DEMO/"
      "$ROOT/scripts/reset-demo.sh" --workspace "$LOCAL_DEMO/canarias-tours" --feed "$LOCAL_DEMO/feed" --quiet
    fi
    mkdir -p "$LOCAL_HOME"
    info "Hosted Agent local en http://localhost:8088 · HOME simulado: ${LOCAL_HOME/#$ROOT\//}"
    cd "$ROOT"
    UPGRADE_CAPTAIN_HOME="$LOCAL_HOME" UPGRADE_CAPTAIN_DEMO_ROOT="$LOCAL_DEMO" ASPNETCORE_URLS="http://localhost:8088" \
      exec dotnet run --project src/UpgradeCaptain.Hosted --no-launch-profile
    ;;
  trigger)
    post "@$JOB_FILE"            # el fichero del trabajo es ya el evento upgrade.requested
    ;;
  approve)
    [[ -n "$APR" && -n "$AS" ]] || die "Uso: scripts/hosted.sh approve APR-0001 --as @maria-payments [--reason texto] [--reject]"
    reason_json="null"; [[ -z "$REASON" ]] || reason_json="\"$(json_escape "$REASON")\""
    body="{\"type\":\"approval.decided\",\"jobId\":\"$(json_escape "$JOB_ID")\",\"approvalId\":\"$APR\",\"decision\":\"$DECISION\",\"reason\":$reason_json,\"approver\":\"$(json_escape "$AS")\"}"
    if [[ $LOCAL -eq 1 ]]; then
      post "$body" -H "x-agent-user-id: $(json_value "$ROOT/demo/identities.json" "$AS")"
    else
      warn "En Foundry, x-agent-user-id es TU identidad de az login (protocolo 2.0.0). Con una sola cuenta manda 'approver' porque deploy-hosted.sh pone UPGRADE_CAPTAIN_DEV_IDENTITY=1: dilo en el escenario."
      post "$body"
    fi
    ;;
  status)
    while true; do
      post "{\"type\":\"job.status\",\"jobId\":\"$(json_escape "$JOB_ID")\"}"
      [[ $WATCH -eq 1 ]] || break
      sleep 5
    done
    ;;
  *)
    sed -n '2,12p' "$0"; exit 1 ;;
esac
