// bump_package.cs · a .NET 10 single-file app
//   dotnet run bump_package.cs -- <propsPath> <package> <version>
//
// Changes the version of a package in Directory.Packages.props (NuGet central package management):
//   · it only touches the Version attribute of the given <PackageVersion Include="package" .../>
//   · it rejects prerelease versions (playbook: never prereleases)
//   · it prints JSON with the result; exit code 0 if everything went well
// A relative path is resolved against the repo (UPGRADE_CAPTAIN_REPO or the current directory).

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 3)
{
    return Fail("Usage: bump_package.cs <propsPath> <package> <version>");
}

string propsPath = args[0], package = args[1], version = args[2];
if (!Path.IsPathRooted(propsPath))
{
    propsPath = Path.Combine(Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_REPO") ?? Environment.CurrentDirectory, propsPath);
}

if (Path.GetFileName(propsPath) != "Directory.Packages.props")
{
    return Fail("Only Directory.Packages.props can be edited (central package management).");
}

if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
{
    return Fail($"'{version}' is not a stable version (major.minor.patch). The playbook forbids prereleases.");
}

if (!File.Exists(propsPath))
{
    return Fail($"{propsPath} doesn't exist.");
}

// <PackageVersion Include="Teide.Geo" Version="1.8.0" />  (the attribute order used in our repos)
string text = File.ReadAllText(propsPath);
var pattern = new Regex($"(<PackageVersion\\s+Include=\"{Regex.Escape(package)}\"\\s+Version=\")([^\"]+)(\")", RegexOptions.IgnoreCase);
Match match = pattern.Match(text);
if (!match.Success)
{
    return Fail($"{package} doesn't appear in {Path.GetFileName(propsPath)}.");
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
