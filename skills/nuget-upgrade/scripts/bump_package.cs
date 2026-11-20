// bump_package.cs · app de un solo fichero de .NET 10
//   dotnet run bump_package.cs -- <propsPath> <package> <version>
//
// Cambia la versión de un paquete en Directory.Packages.props (gestión central de NuGet):
//   · solo toca el atributo Version del <PackageVersion Include="package" .../> indicado
//   · rechaza versiones prerelease (playbook: nunca prereleases)
//   · imprime JSON con el resultado; código de salida 0 si todo va bien
// Una ruta relativa se resuelve contra el repo (UPGRADE_CAPTAIN_REPO o el directorio actual).

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 3)
{
    return Fail("Uso: bump_package.cs <propsPath> <package> <version>");
}

string propsPath = args[0], package = args[1], version = args[2];
if (!Path.IsPathRooted(propsPath))
{
    propsPath = Path.Combine(Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_REPO") ?? Environment.CurrentDirectory, propsPath);
}

if (Path.GetFileName(propsPath) != "Directory.Packages.props")
{
    return Fail("Solo se puede editar Directory.Packages.props (gestión central de paquetes).");
}

if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
{
    return Fail($"'{version}' no es una versión estable (mayor.menor.parche). El playbook prohíbe prereleases.");
}

if (!File.Exists(propsPath))
{
    return Fail($"No existe {propsPath}.");
}

// <PackageVersion Include="Teide.Geo" Version="1.8.0" />  (el orden de atributos de nuestros repos)
string text = File.ReadAllText(propsPath);
var pattern = new Regex($"(<PackageVersion\\s+Include=\"{Regex.Escape(package)}\"\\s+Version=\")([^\"]+)(\")", RegexOptions.IgnoreCase);
Match match = pattern.Match(text);
if (!match.Success)
{
    return Fail($"{package} no aparece en {Path.GetFileName(propsPath)}.");
}

string current = match.Groups[2].Value;
bool changed = current != version;
if (changed)
{
    File.WriteAllText(propsPath, pattern.Replace(text, m => m.Groups[1].Value + version + m.Groups[3].Value, 1));
}

WriteJson(writer =>
{
    writer.WriteString("package", package);
    writer.WriteString("from", current);
    writer.WriteString("to", version);
    writer.WriteBoolean("changed", changed);
});
return 0;

static int Fail(string message)
{
    WriteJson(writer => writer.WriteString("error", message));
    return 1;
}

static void WriteJson(Action<Utf8JsonWriter> body)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        writer.WriteStartObject();
        body(writer);
        writer.WriteEndObject();
    }

    Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
}
