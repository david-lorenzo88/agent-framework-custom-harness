# Fix recipes · Teide.Geo 1.x → 2.0

## 1. `GeoPoint` namespace

```csharp
// Before
using Teide.Geo;

// After (if the file also uses GeoCalculator, GeoFormat or ElevationClient, keep both)
using Teide.Geo;
using Teide.Geo.Primitives;
```

## 2. Async elevation

```csharp
// Before
public int GetElevationFor(string poi) => client.GetElevation(PointsOfInterest.Get(poi));

// After: async all the way up (service, endpoint and tests)
public Task<int> GetElevationForAsync(string poi, CancellationToken ct = default) =>
    client.GetElevationAsync(PointsOfInterest.Get(poi), ct);

// Minimal API endpoint
app.MapGet("/elevation/{poi}", async (string poi, ElevationService s, CancellationToken ct) =>
    Results.Ok(await s.DescribeAsync(poi, ct)));

// xUnit test
[Fact]
public async Task GetElevationFor_Teide_Is3715m() =>
    Assert.Equal(3715, await service.GetElevationForAsync("teide"));
```

## 3. DMS format

```csharp
// Before
string dms = GeoFormat.ToDms(point);

// After
string dms = point.ToString("dms");
```

## 4. Distance in kilometers

```csharp
// Before (implicit kilometers)
double km = GeoCalculator.Distance(from, to);

// After: ask for kilometers explicitly
double km = GeoCalculator.Distance(from, to, DistanceUnit.Kilometers);
```

Also check comments that say "returns kilometers": that is no longer true.
