#!/usr/bin/env bash
# Levanta el Aspire Dashboard en Docker para ver las trazas de Upgrade Captain (step-2).
#   UI:        http://localhost:18888   (acceso anónimo, solo para la demo)
#   OTLP gRPC: http://localhost:4317    → export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
# Idempotente: si ya está en marcha, solo lo dice. Con --stop lo para.
#
# Uso: scripts/aspire.sh [--stop]
set -euo pipefail
source "$(dirname "$0")/lib.sh"

NAME="upgrade-captain-aspire"
IMAGE="mcr.microsoft.com/dotnet/aspire-dashboard:${ASPIRE_DASHBOARD_TAG:-latest}"

docker info >/dev/null 2>&1 || die "Docker no está en marcha"

if [[ "${1:-}" == "--stop" ]]; then
  docker rm -f "$NAME" >/dev/null 2>&1 && ok "Aspire Dashboard parado" || warn "No estaba en marcha"
  exit 0
fi

state="$(docker inspect -f '{{.State.Status}}' "$NAME" 2>/dev/null || true)"
case "$state" in
  running) ok "Aspire Dashboard ya está en marcha" ;;
  exited|created) docker start "$NAME" >/dev/null && ok "Aspire Dashboard arrancado de nuevo" ;;
  *)
    docker run -d --name "$NAME" \
      -p 18888:18888 -p 4317:18889 \
      -e DASHBOARD__FRONTEND__AUTHMODE=Unsecured \
      -e DASHBOARD__OTLP__AUTHMODE=Unsecured \
      -e ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
      "$IMAGE" >/dev/null
    ok "Aspire Dashboard creado ($IMAGE)"
    ;;
esac

info "UI:   http://localhost:18888"
info "OTLP: export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317"
