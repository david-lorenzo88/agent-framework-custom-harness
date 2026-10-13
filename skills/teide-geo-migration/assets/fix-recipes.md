# Recetas de arreglo · Teide.Geo 1.x → 2.0

## 1. Namespace de `GeoPoint`

```csharp
// Antes
using Teide.Geo;

// Después (si el fichero usa también GeoCalculator, GeoFormat o ElevationClient, deja ambos)
using Teide.Geo;
using Teide.Geo.Primitives;
```

## 2. Elevación asíncrona

```csharp
// Antes
public int GetElevationFor(string poi) => client.GetElevation(PointsOfInterest.Get(poi));

// Después: async hacia arriba (servicio, endpoint y tests)
public Task<int> GetElevationForAsync(string poi, CancellationToken ct = default) =>
    client.GetElevationAsync(PointsOfInterest.Get(poi), ct);

// Endpoint de minimal API
app.MapGet("/elevation/{poi}", async (string poi, ElevationService s, CancellationToken ct) =>
    Results.Ok(await s.DescribeAsync(poi, ct)));

// Test xUnit
[Fact]
public async Task GetElevationFor_Teide_Is3715m() =>
    Assert.Equal(3715, await service.GetElevationForAsync("teide"));
```

## 3. Formato DMS

```csharp
// Antes
string dms = GeoFormat.ToDms(point);

// Después
string dms = point.ToString("dms");
```

## 4. Distancia en kilómetros

```csharp
// Antes (kilómetros implícitos)
double km = GeoCalculator.Distance(from, to);

// Después: pide kilómetros explícitamente
double km = GeoCalculator.Distance(from, to, DistanceUnit.Kilometers);
```

Revisa también los comentarios que digan "devuelve kilómetros": ya no es cierto.
