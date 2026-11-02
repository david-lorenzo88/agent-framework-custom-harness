# Teide.Geo 2.0.0

Versión mayor. Corrige el aviso de seguridad **TEIDE-2026-0042** (ReDoS en `GeoFormat.Parse`)
e introduce **cuatro cambios incompatibles**.

## Cambios incompatibles

1. **`GeoPoint` cambia de namespace.** Ahora vive en `Teide.Geo.Primitives`.
   Síntoma: `error CS0246: The type or namespace name 'GeoPoint' could not be found`.
   Arreglo: añade `using Teide.Geo.Primitives;`.

2. **`ElevationClient.GetElevation` desaparece.** Se sustituye por
   `Task<int> GetElevationAsync(GeoPoint point, CancellationToken cancellationToken = default)`.
   Síntoma: `error CS1061`. Arreglo: `await client.GetElevationAsync(p, ct)` y propagar `async`
   hacia arriba (los métodos que la llaman deben devolver `Task`/`Task<T>`).

3. **`GeoFormat.ToDms` desaparece.** Usa `GeoPoint.ToString("dms")`, que produce exactamente
   el mismo texto (`28°16'20"N 16°38'33"W`).
   Síntoma: `error CS0117`.

4. **`GeoCalculator.Distance(a, b)` devuelve METROS, no kilómetros.** ⚠️ Compila sin errores.
   Hay una sobrecarga nueva `Distance(a, b, DistanceUnit unit)`.
   Arreglo: donde el código espere kilómetros, usa `GeoCalculator.Distance(a, b, DistanceUnit.Kilometers)`
   (o divide entre 1000). Revisa especialmente cálculos de precios y totales.

## Seguridad

- `GeoFormat.Parse` ya no usa expresiones regulares con backtracking (TEIDE-2026-0042).

## Sin cambios

- `GeoCalculator.Bearing(a, b)`.
