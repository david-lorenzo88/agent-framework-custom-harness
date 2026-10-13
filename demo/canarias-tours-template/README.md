# Canarias.Tours

Backend de reservas de excursiones por Tenerife.

| Proyecto | Qué hace | Propietario |
|---|---|---|
| `src/Canarias.Tours.Domain` | Puntos de interés, planificador de rutas, itinerarios | @david-lorenzo |
| `src/Canarias.Tours.Payments` | Tarifas de traslado en euros | @maria-payments |
| `src/Canarias.Tours.Api` | API mínima: `/routes`, `/fare`, `/elevation/{poi}` | @david-lorenzo |
| `tests/Canarias.Tours.Tests` | Tests xUnit | — |

Dependencia geográfica: **Teide.Geo** (gestión central de paquetes en `Directory.Packages.props`).

```bash
dotnet build
dotnet test
dotnet run --project src/Canarias.Tours.Api
```
