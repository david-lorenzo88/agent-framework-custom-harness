using Canarias.Tours.Api;
using Canarias.Tours.Domain;
using Canarias.Tours.Payments;
using Teide.Geo;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ElevationClient>();
builder.Services.AddSingleton<ElevationService>();
builder.Services.AddSingleton<RoutePlanner>();
builder.Services.AddSingleton<FareCalculator>();

var app = builder.Build();

// GET /routes?from=teide&to=masca,garachico
app.MapGet("/routes", (string from, string to, RoutePlanner planner) =>
{
    Itinerary itinerary = planner.Plan(from, to.Split(',', StringSplitOptions.TrimEntries));
    return Results.Ok(new { legs = itinerary.Describe(), totalKm = Math.Round(itinerary.TotalKm, 1) });
});

// GET /fare?from=la-laguna&to=puerto-de-la-cruz  (también acepta "lat, lon")
app.MapGet("/fare", (string from, string to, FareCalculator fares) =>
    Results.Ok(new { eur = fares.Quote(PointsOfInterest.Resolve(from), PointsOfInterest.Resolve(to)) }));

// GET /elevation/teide
app.MapGet("/elevation/{poi}", async (string poi, ElevationService elevations, CancellationToken ct) =>
    Results.Ok(await elevations.DescribeAsync(poi, ct)));

// GET /pois
app.MapGet("/pois", async (ElevationService elevations, CancellationToken ct) =>
    Results.Ok(await elevations.DescribeAllAsync(ct)));

app.Run();

public partial class Program;
