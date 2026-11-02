# Teide.Geo 1.8.0

Notas de la versión 1.8.0.

## Novedades

- `GeoFormat.Parse` acepta coordenadas decimales con espacios alrededor de la coma.
- Tabla de elevaciones ampliada con Anaga y Garachico.

## API

- `GeoPoint` (namespace `Teide.Geo`): punto con latitud y longitud en grados decimales.
- `GeoCalculator.Distance(a, b)`: distancia haversine **en kilómetros**.
- `GeoCalculator.Bearing(a, b)`: rumbo inicial en grados (0–360).
- `ElevationClient.GetElevation(p)`: elevación en metros desde una tabla offline.
- `GeoFormat.ToDms(p)`: formato grados/minutos/segundos.
- `GeoFormat.Parse(text)`: convierte `"lat, lon"` en `GeoPoint`.

## Avisos de seguridad conocidos

- TEIDE-2026-0042 (alta): `GeoFormat.Parse` es vulnerable a ReDoS con entradas largas.
  Corregido en 2.0.0. No hay parche para 1.x.
