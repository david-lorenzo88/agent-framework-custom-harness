# Teide.Geo 2.0.0

Major version. Fixes security advisory **TEIDE-2026-0042** (ReDoS in `GeoFormat.Parse`)
and introduces **four breaking changes**.

## Breaking changes

1. **`GeoPoint` moves to a new namespace.** It now lives in `Teide.Geo.Primitives`.
   Symptom: `error CS0246: The type or namespace name 'GeoPoint' could not be found`.
   Fix: add `using Teide.Geo.Primitives;`.

2. **`ElevationClient.GetElevation` is gone.** It is replaced by
   `Task<int> GetElevationAsync(GeoPoint point, CancellationToken cancellationToken = default)`.
   Symptom: `error CS1061`. Fix: `await client.GetElevationAsync(p, ct)` and propagate `async`
   upwards (the methods that call it must return `Task`/`Task<T>`).

3. **`GeoFormat.ToDms` is gone.** Use `GeoPoint.ToString("dms")`, which produces exactly
   the same text (`28°16'20"N 16°38'33"W`).
   Symptom: `error CS0117`.

4. **`GeoCalculator.Distance(a, b)` returns METERS, not kilometers.** ⚠️ It compiles without errors.
   There is a new overload `Distance(a, b, DistanceUnit unit)`.
   Fix: wherever the code expects kilometers, use `GeoCalculator.Distance(a, b, DistanceUnit.Kilometers)`
   (or divide by 1000). Pay special attention to price calculations and totals.

## Security

- `GeoFormat.Parse` no longer uses backtracking regular expressions (TEIDE-2026-0042).

## Unchanged

- `GeoCalculator.Bearing(a, b)`.
