# Teide.Geo 1.8.0

Release notes for version 1.8.0.

## What's new

- `GeoFormat.Parse` accepts decimal coordinates with spaces around the comma.
- Elevation table extended with Anaga and Garachico.

## API

- `GeoPoint` (namespace `Teide.Geo`): a point with latitude and longitude in decimal degrees.
- `GeoCalculator.Distance(a, b)`: haversine distance **in kilometers**.
- `GeoCalculator.Bearing(a, b)`: initial bearing in degrees (0–360).
- `ElevationClient.GetElevation(p)`: elevation in meters from an offline table.
- `GeoFormat.ToDms(p)`: degrees/minutes/seconds format.
- `GeoFormat.Parse(text)`: turns `"lat, lon"` into a `GeoPoint`.

## Known security advisories

- TEIDE-2026-0042 (high): `GeoFormat.Parse` is vulnerable to ReDoS with long inputs.
  Fixed in 2.0.0. There is no patch for 1.x.
