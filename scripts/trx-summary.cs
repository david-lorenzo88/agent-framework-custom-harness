// trx-summary.cs · app de un solo fichero de .NET 10
//   dotnet run scripts/trx-summary.cs -- <fichero.trx> [--names]
//
// Resume un fichero TRX de `dotnet test`: totales y tests fallidos (uno por línea).
//   --names  imprime solo los nombres completos de los tests fallidos, ordenados (para comparar).

using System.Xml.Linq;

if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Uso: dotnet run scripts/trx-summary.cs -- <fichero.trx> [--names]");
    return 2;
}

XNamespace trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
List<XElement> results = XDocument.Load(args[0]).Descendants(trx + "UnitTestResult").ToList();
List<XElement> failed = results.Where(r => (string?)r.Attribute("outcome") == "Failed").ToList();

if (args.Contains("--names"))
{
    foreach (string name in failed.Select(r => (string?)r.Attribute("testName") ?? "?").Order(StringComparer.Ordinal))
    {
        Console.WriteLine(name);
    }

    return 0;
}

int passed = results.Count(r => (string?)r.Attribute("outcome") == "Passed");
Console.WriteLine($"total={results.Count} passed={passed} failed={failed.Count}");
foreach (XElement result in failed)
{
    string message = result.Descendants(trx + "Message").FirstOrDefault()?.Value.Trim() ?? string.Empty;
    string first = message.Split('\n')[0];
    Console.WriteLine($"  ✘ {result.Attribute("testName")?.Value}: {(first.Length > 160 ? first[..160] : first)}");
}

return 0;
