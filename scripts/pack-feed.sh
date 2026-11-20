#!/usr/bin/env bash
# Empaqueta Teide.Geo 1.8.0 y 2.0.0 en demo/feed, un feed NuGet local de carpeta.
# También copia las notas de versión a demo/feed/notes/<versión>.md (y <versión>.en.md, en inglés) para la tool get_migration_notes.
#
# Uso: scripts/pack-feed.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

mkdir -p "$FEED_DIR/notes"
rm -f "$FEED_DIR"/*.nupkg

for version in 1.8.0 2.0.0; do
  project="$ROOT/demo/teide-geo/v$version/Teide.Geo.csproj"
  dotnet pack "$project" -c Release -o "$FEED_DIR" --nologo -v q -p:ContinuousIntegrationBuild=true >/dev/null
  cp "$ROOT/demo/teide-geo/v$version/RELEASE-NOTES.md" "$FEED_DIR/notes/$version.md"
  cp "$ROOT/demo/teide-geo/v$version/RELEASE-NOTES.en.md" "$FEED_DIR/notes/$version.en.md"
  ok "Teide.Geo $version → demo/feed/Teide.Geo.$version.nupkg"
done

# Si el paquete se regenera con cambios, la caché global de NuGet guardaría la copia antigua.
rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/teide.geo"
ok "Caché local de teide.geo limpiada"
