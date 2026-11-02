# Teide.Geo 2.0.0 · cambios incompatibles

| # | Cambio | ¿Compila? | Síntoma |
|---|---|---|---|
| 1 | `GeoPoint` pasa de `Teide.Geo` a **`Teide.Geo.Primitives`** | ❌ | `CS0246: The type or namespace name 'GeoPoint' could not be found` |
| 2 | `ElevationClient.GetElevation(GeoPoint)` se elimina; ahora es **`Task<int> GetElevationAsync(GeoPoint, CancellationToken = default)`** | ❌ | `CS1061`; obliga a propagar `async` hacia arriba |
| 3 | `GeoFormat.ToDms(GeoPoint)` se elimina; usa **`GeoPoint.ToString("dms")`** (mismo texto exacto) | ❌ | `CS0117` |
| 4 | **`GeoCalculator.Distance(a, b)` devuelve METROS** (antes kilómetros). Nueva sobrecarga `Distance(a, b, DistanceUnit unit)` | ✅ **compila** | Valores 1000 veces mayores: tarifas disparadas, totales absurdos. Solo lo detectan los tests |

## Detalle del cambio 4 (unidades)

- 1.x: `GeoCalculator.Distance(a, b)` → kilómetros (`double`).
- 2.0: `GeoCalculator.Distance(a, b)` → **metros**; `GeoCalculator.Distance(a, b, DistanceUnit.Kilometers)` → kilómetros.
- `DistanceUnit` está en el namespace `Teide.Geo` (valores `Meters` y `Kilometers`).
- Ejemplo real: un traslado de 25,5 km a 1,20 €/km + 3 € pasa de **33,66 €** a **30.663,76 €** si no se corrige.

## Sin cambios

- `GeoCalculator.Bearing(a, b)`.
- `GeoFormat.Parse(text)` mantiene la firma (internamente ya no usa regex).
