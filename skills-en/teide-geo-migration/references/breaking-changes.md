# Teide.Geo 2.0.0 · breaking changes

| # | Change | Compiles? | Symptom |
|---|---|---|---|
| 1 | `GeoPoint` moves from `Teide.Geo` to **`Teide.Geo.Primitives`** | ❌ | `CS0246: The type or namespace name 'GeoPoint' could not be found` |
| 2 | `ElevationClient.GetElevation(GeoPoint)` is removed; it is now **`Task<int> GetElevationAsync(GeoPoint, CancellationToken = default)`** | ❌ | `CS1061`; forces you to propagate `async` upwards |
| 3 | `GeoFormat.ToDms(GeoPoint)` is removed; use **`GeoPoint.ToString("dms")`** (exactly the same text) | ❌ | `CS0117` |
| 4 | **`GeoCalculator.Distance(a, b)` returns METERS** (it used to return kilometers). New overload `Distance(a, b, DistanceUnit unit)` | ✅ **compiles** | Values 1000 times larger: skyrocketing fares, absurd totals. Only the tests catch it |

## Change 4 in detail (units)

- 1.x: `GeoCalculator.Distance(a, b)` → kilometers (`double`).
- 2.0: `GeoCalculator.Distance(a, b)` → **meters**; `GeoCalculator.Distance(a, b, DistanceUnit.Kilometers)` → kilometers.
- `DistanceUnit` lives in the `Teide.Geo` namespace (values `Meters` and `Kilometers`).
- Real example: a 25.5 km transfer at €1.20/km + €3 goes from **€33.66** to **€30,663.76** if it isn't fixed.

## Unchanged

- `GeoCalculator.Bearing(a, b)`.
- `GeoFormat.Parse(text)` keeps its signature (internally it no longer uses a regex).
