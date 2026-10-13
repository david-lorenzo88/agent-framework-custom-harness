# Datos de la demo

Todo lo que hay aquí simula el mundo real alrededor de Upgrade Captain. Nada de esto es código del harness.

| Carpeta | Qué es |
|---|---|
| `teide-geo/v1.8.0`, `teide-geo/v2.0.0` | Fuentes de **Teide.Geo**, la librería ficticia. 2.0.0 trae 4 cambios incompatibles (ver sus `RELEASE-NOTES.md`). |
| `feed/` | Feed NuGet local de carpeta. Lo genera `scripts/pack-feed.sh` (los `.nupkg` no se versionan). `feed/notes/<versión>.md` alimenta la tool `get_migration_notes`. |
| `canarias-tours-template/` | El repo que arregla el agente. `scripts/reset-demo.sh` lo copia a `demo-workspace/canarias-tours` con historia git creíble. |
| `reference-fix/` | **Nunca visible para el agente.** Overlay con los arreglos *mecánicos* (bump, namespace, async, `ToDms`). No arregla el cambio de unidades: esa es la trampa del step-6. |
| `security/advisories.json` | Aviso `TEIDE-2026-0042` (ReDoS en `GeoFormat.Parse`, afecta a `< 2.0.0`). |
| `identities.json` | Handle → id de objeto de Entra, para el host en Foundry (step-7). |
| `jobs/upgrade-teide-geo.json` | Trabajo para ejecuciones desatendidas (`run --job`). |

## Los cuatro cambios de Teide.Geo 2.0.0

1. `GeoPoint` pasa a `Teide.Geo.Primitives` → CS0246.
2. `GetElevation` → `Task<int> GetElevationAsync(GeoPoint, CancellationToken = default)` → cascada async en la API.
3. `GeoFormat.ToDms` desaparece → `GeoPoint.ToString("dms")`.
4. **`GeoCalculator.Distance` devuelve metros.** Compila. Solo los tests lo detectan.

## La trampa (lo demuestra `scripts/verify-trap.sh`)

- En 1.8.0: build verde y 24/24 tests verdes.
- Con 2.0.0 y solo los arreglos mecánicos: build verde y fallan **exactamente** los 6 tests de `FareTests`
  y 2 de `DistanceTests` (por ejemplo 30.663,76 € en vez de 33,66 € de Los Cristianos al Teide).
