// apply-unit-fix.cs · app de un solo fichero de .NET 10
//   dotnet run scripts/apply-unit-fix.cs -- <repo>
//
// Aplica el arreglo de UNIDADES que debería hacer el agente en el step-6 (lo usa verify-trap.sh, caso 3):
// en Domain y Payments, GeoCalculator.Distance(a, b) → GeoCalculator.Distance(a, b, DistanceUnit.Kilometers).

using System.Text.RegularExpressions;

if (args.Length == 0 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Uso: dotnet run scripts/apply-unit-fix.cs -- <repo>");
    return 2;
}

string repo = args[0];

// Admite un nivel de paréntesis anidados: Distance(PointsOfInterest.Get(a), PointsOfInterest.Get(b))
var distanceCall = new Regex(@"GeoCalculator\.Distance\(((?:[^()]|\([^()]*\))*)\)");

foreach (string project in new[] { "src/Canarias.Tours.Domain", "src/Canarias.Tours.Payments" })
{
    foreach (string file in Directory.EnumerateFiles(Path.Combine(repo, project), "*.cs"))
    {
        string text = File.ReadAllText(file);
        string fixedText = distanceCall.Replace(text, "GeoCalculator.Distance($1, DistanceUnit.Kilometers)");
        if (fixedText != text)
        {
            File.WriteAllText(file, fixedText);
            Console.WriteLine($"✔ {Path.GetRelativePath(repo, file)}");
        }
    }
}

return 0;
