#!/usr/bin/env bash
# La cabina web de la demo: http://localhost:5180 (solo localhost). Sustituye a los tres terminales:
#   · el panel en directo de cada paso (antes T1), la ventana de María en /maria (antes T2)
#   · el panel «Realidad» con dotnet build/test y git en el repo de la demo (antes T3)
# La consola sigue ahí como plan B (dotnet run --project src/UpgradeCaptain.Console).
#
# Uso: scripts/cockpit.sh [--lang en|es] [--no-open]
#   --lang     idioma inicial de la cabina (por defecto en; se cambia en la barra de arriba)
#   --no-open  no abre el navegador
# Si el Aspire Dashboard de scripts/aspire.sh está en marcha, exporta OTEL_EXPORTER_OTLP_ENDPOINT para ver las trazas.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

LANG_CODE="en"; OPEN=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --lang) LANG_CODE="$2"; shift 2 ;;
    --no-open) OPEN=0; shift ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done
[[ "$LANG_CODE" == "en" || "$LANG_CODE" == "es" ]] || die "--lang debe ser en o es"

cd "$ROOT"
[[ -f src/UpgradeCaptain.Web/UpgradeCaptain.Web.csproj ]] || die "La cabina vive en main (= step-4) y tiene todos los pasos dentro: git switch main (o scripts/goto-step.sh 4)"
URL="${UPGRADE_CAPTAIN_COCKPIT_URL:-http://localhost:5180}"
# Development: con dotnet run (sin publicar) es el entorno que sirve los recursos estáticos del framework (blazor.web.js).
export UPGRADE_CAPTAIN_LANG="$LANG_CODE" UPGRADE_CAPTAIN_COCKPIT_URL="$URL" ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

if [[ -z "${OTEL_EXPORTER_OTLP_ENDPOINT:-}" ]] \
   && [[ "$(docker inspect -f '{{.State.Status}}' upgrade-captain-aspire 2>/dev/null || true)" == "running" ]]; then
  export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
  ok "Aspire en marcha: trazas a $OTEL_EXPORTER_OTLP_ENDPOINT (http://localhost:18888)"
else
  [[ -n "${OTEL_EXPORTER_OTLP_ENDPOINT:-}" ]] && ok "Trazas a $OTEL_EXPORTER_OTLP_ENDPOINT" || warn "Sin trazas: arranca scripts/aspire.sh y vuelve a lanzar la cabina"
fi

info "Compilando la cabina…"
dotnet build src/UpgradeCaptain.Web --nologo -v q -clp:ErrorsOnly >/dev/null || dotnet build src/UpgradeCaptain.Web --nologo -v q -clp:ErrorsOnly

if [[ $OPEN -eq 1 ]]; then
  ( for _ in $(seq 1 60); do
      if curl -fs -o /dev/null "$URL/"; then (open "$URL" 2>/dev/null || xdg-open "$URL" 2>/dev/null || true); break; fi
      sleep 0.5
    done ) &
fi

ok "Cabina en $URL ($LANG_CODE) · Ctrl+C para salir"
exec dotnet run --project src/UpgradeCaptain.Web --no-build --no-launch-profile
