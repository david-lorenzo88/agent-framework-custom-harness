#!/usr/bin/env bash
# Reconstruye el repo que arregla el agente (demo-workspace/canarias-tours) desde la plantilla.
#
#   1. Copia demo/canarias-tours-template en el workspace.
#   2. git init con identidad LOCAL (nunca la global) y ~8 commits creíbles + tag v3.2.0.
#   3. Añade un remoto "origin" ficticio (dominio .example, nunca resuelve).
#   4. Limpia state/, agent-file-memory/ y sesiones exportadas.
#   5. Con --state after-mechanical-fix aplica el bump a 2.0.0 y los arreglos mecánicos
#      (demo/reference-fix/) en la rama upgrade/teide-geo-2.0: compila, pero los tests de tarifas fallan.
#   6. Con --lang en (o UPGRADE_CAPTAIN_LANG=en) el repo sale en inglés: comentarios, README y mensajes de commit
#      (tabla demo/i18n/canarias-tours.en.txt, aplicada antes de cada commit).
#
# Uso:
#   scripts/reset-demo.sh [--state clean|after-mechanical-fix] [--lang es|en]
#                         [--workspace <dir>] [--feed <dir>] [--keep-state] [--quiet]
# Idempotente; tarda menos de 5 s.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

STATE="clean"
LANG_CODE="$(printf '%s' "${UPGRADE_CAPTAIN_LANG:-es}" | tr '[:upper:]' '[:lower:]' | cut -c1-2)"
WS="$WORKSPACE_DEFAULT"
FEED="$FEED_DIR"
KEEP_STATE=0
QUIET=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --state) STATE="$2"; shift 2 ;;
    --lang) LANG_CODE="$2"; shift 2 ;;
    --workspace) WS="$2"; KEEP_STATE=1; shift 2 ;;   # un workspace explícito nunca borra el estado global
    --feed) FEED="$2"; shift 2 ;;
    --keep-state) KEEP_STATE=1; shift ;;
    --quiet) QUIET=1; shift ;;
    -h|--help) sed -n '2,16p' "$0"; exit 0 ;;
    *) die "Argumento desconocido: $1" ;;
  esac
done

[[ "$STATE" == "clean" || "$STATE" == "after-mechanical-fix" ]] || die "--state debe ser clean o after-mechanical-fix"
[[ "$LANG_CODE" == "es" || "$LANG_CODE" == "en" ]] || die "--lang debe ser es o en"

TEMPLATE="$ROOT/demo/canarias-tours-template"
FIX="$ROOT/demo/reference-fix"
I18N="$ROOT/demo/i18n/canarias-tours.$LANG_CODE.txt"

# El feed local debe existir (lo genera pack-feed.sh). En el contenedor ya viene empaquetado.
if [[ ! -f "$FEED/Teide.Geo.1.8.0.nupkg" ]]; then
  [[ "$FEED" == "$FEED_DIR" ]] || die "No hay paquetes en $FEED"
  "$ROOT/scripts/pack-feed.sh" >/dev/null
fi

# ── 1. Workspace limpio ────────────────────────────────────────────────────────
# Conservamos bin/ y obj/ entre resets para que el primer build en el escenario sea rápido.
if [[ -d "$WS/.git" ]]; then
  find "$WS" -mindepth 1 -maxdepth 1 ! -name src ! -name tests -exec rm -rf {} +
  find "$WS/src" "$WS/tests" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | while read -r proj; do
    find "$proj" -mindepth 1 -maxdepth 1 ! -name bin ! -name obj -exec rm -rf {} +
  done
else
  rm -rf "$WS"
fi
mkdir -p "$WS"
cd "$WS"

git init -q -b main
git config user.name "Upgrade Captain"
git config user.email "upgrade-captain@canarias-tours.example"
git config commit.gpgsign false
git config tag.gpgsign false
git config core.hooksPath /dev/null

# nuget.config con la ruta ABSOLUTA del feed local y nuget.org. <clear/> evita heredar
# el nuget.config de upgrade-captain (el workspace vive dentro de ese repo).
write_nuget_config() {
  cat > nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="teide-local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="teide-local">
      <package pattern="Teide.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML
}

# Copia ficheros o carpetas de la plantilla. Para carpetas copia el CONTENIDO (cp -R dir/. destino/):
# si el destino ya existe (bin/ y obj/ conservados) un "cp -R dir destino" anidaría dir dentro de destino.
copy() {
  for path in "$@"; do
    if [[ -d "$TEMPLATE/$path" ]]; then
      mkdir -p "$path"
      cp -R "$TEMPLATE/$path/." "$path/"
    else
      mkdir -p "$(dirname "$path")"
      cp "$TEMPLATE/$path" "$path"
    fi
  done
}

# Traduce el árbol de trabajo con la tabla de $I18N (solo en inglés): sustituciones literales, en orden,
# byte a byte (tabla y ficheros en UTF-8, sin decodificar: así coinciden también las tildes).
translate_tree() {
  [[ "$LANG_CODE" == "es" ]] && return 0
  find . -type f \( -name '*.cs' -o -name '*.md' -o -name '*.props' -o -name CODEOWNERS \) \
    -not -path './.git/*' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 perl -i -pe 'BEGIN { open my $t, "<", shift @ARGV or die "$!"; while (<$t>) { chomp; next if /^(--|\s*$)/; my ($es, $en) = split / => /, $_, 2; push @pairs, [$es, $en] } } for my $p (@pairs) { s/\Q$p->[0]\E/$p->[1]/g }' "$I18N"
}

# El mensaje en el idioma del repo: t "español" "english"
t() { if [[ "$LANG_CODE" == "en" ]]; then printf '%s' "$2"; else printf '%s' "$1"; fi; }

commit() {
  local who="$1" date="$2" message="$3"
  local name email
  case "$who" in
    david)      name="David Lorenzo";      email="david.lorenzo@canarias-tours.example" ;;
    maria)      name="María Hernández";    email="maria.hernandez@canarias-tours.example" ;;
    dependabot) name="dependabot[bot]";    email="49699333+dependabot[bot]@users.noreply.github.com" ;;
  esac
  translate_tree
  git add -A
  GIT_AUTHOR_NAME="$name" GIT_AUTHOR_EMAIL="$email" GIT_AUTHOR_DATE="$date" \
  GIT_COMMITTER_NAME="$name" GIT_COMMITTER_EMAIL="$email" GIT_COMMITTER_DATE="$date" \
    git commit -q -m "$message"
}

# ── 2. Historia creíble ───────────────────────────────────────────────────────
# La plantilla guarda el .gitignore SIN punto: az acr build (como npm) descarta siempre los ficheros llamados
# .gitignore al subir el código, y la imagen del step-4 se quedaba sin él (D-41).
cp "$TEMPLATE/gitignore" .gitignore
copy global.json Directory.Build.props Directory.Packages.props Canarias.Tours.slnx
printf '# Canarias.Tours\n\n%s\n' "$(t "Backend de reservas de excursiones por Tenerife." "Booking backend for excursions around Tenerife.")" > README.md
write_nuget_config
commit david "2026-05-04T09:12:00+01:00" "$(t "Estructura inicial de la solución" "Initial solution structure")"

copy src/Canarias.Tours.Domain/Canarias.Tours.Domain.csproj src/Canarias.Tours.Domain/PointsOfInterest.cs \
     src/Canarias.Tours.Domain/RouteLeg.cs src/Canarias.Tours.Domain/RoutePlanner.cs \
     src/Canarias.Tours.Domain/Itinerary.cs src/Canarias.Tours.Domain/Compass.cs
commit david "2026-05-11T17:40:00+01:00" "$(t "Dominio: puntos de interés y planificador de rutas" "Domain: points of interest and route planner")"

copy src/Canarias.Tours.Payments
commit maria "2026-05-19T11:05:00+01:00" "$(t "Pagos: tarifas de traslado y excursiones multiparada en euros" "Payments: transfer fares and multi-stop excursions in euros")"

copy src/Canarias.Tours.Api
commit david "2026-06-02T10:30:00+01:00" "$(t "API mínima: /routes, /fare y /elevation" "Minimal API: /routes, /fare and /elevation")"

copy tests/Canarias.Tours.Tests/Canarias.Tours.Tests.csproj tests/Canarias.Tours.Tests/DistanceTests.cs \
     tests/Canarias.Tours.Tests/FareTests.cs tests/Canarias.Tours.Tests/ElevationTests.cs
commit david "2026-06-09T16:20:00+01:00" "$(t "Tests de dominio, pagos y elevación" "Domain, payments and elevation tests")"

copy .github
commit david "2026-07-07T09:00:00+01:00" "$(t "CODEOWNERS: el equipo de pagos revisa Payments" "CODEOWNERS: the payments team reviews Payments")"

copy src/Canarias.Tours.Domain/PoiCard.cs tests/Canarias.Tours.Tests/FormatTests.cs
commit david "2026-09-15T12:45:00+01:00" "$(t "Fichas de puntos de interés con coordenadas DMS" "Point-of-interest cards with DMS coordinates")"

copy README.md
commit david "2026-09-22T18:10:00+01:00" "$(t "README: proyectos y propietarios" "README: projects and owners")"
git tag -a v3.2.0 -m "Canarias.Tours 3.2.0" >/dev/null 2>&1 || git tag v3.2.0

# ── 3. Remoto ficticio ────────────────────────────────────────────────────────
git remote add origin "https://git.canarias-tours.example/canarias-tours/canarias-tours.git"

# ── 4. Estado del harness ─────────────────────────────────────────────────────
if [[ $KEEP_STATE -eq 0 ]]; then
  rm -rf "$ROOT/state" "$ROOT/agent-file-memory" "$ROOT/sessions"
  mkdir -p "$ROOT/sessions"   # destino de /session-export sessions/<fichero>.json
fi

# ── 5. Estado "después de los arreglos mecánicos" (punto de entrada del step-3) ──
if [[ "$STATE" == "after-mechanical-fix" ]]; then
  git switch -q -c upgrade/teide-geo-2.0
  sed -i '' -e 's/Include="Teide.Geo" Version="1.8.0"/Include="Teide.Geo" Version="2.0.0"/' Directory.Packages.props 2>/dev/null \
    || sed -i -e 's/Include="Teide.Geo" Version="1.8.0"/Include="Teide.Geo" Version="2.0.0"/' Directory.Packages.props
  commit dependabot "2026-10-08T06:02:00+00:00" "Bump Teide.Geo from 1.8.0 to 2.0.0"
  cp -R "$FIX/." .   # overlay: sobrescribe solo los ficheros que cambian
  commit david "2026-10-09T19:30:00+01:00" "$(t "Arreglos mecánicos para Teide.Geo 2.0 (namespace, async, ToDms)" "Mechanical fixes for Teide.Geo 2.0 (namespace, async, ToDms)")"
fi

if [[ $QUIET -eq 0 ]]; then
  ok "$(t "Workspace listo" "Workspace ready"): ${WS/#$ROOT\//} ($STATE, $(t rama branch) $(git branch --show-current), $(git rev-list --count HEAD) commits)"
fi
