using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace UpgradeCaptain.Core;

/// <summary>
/// Ejecuta los scripts de las skills escritos en C# como apps de un solo fichero de .NET 10:
/// <c>dotnet run script.cs -- arg1 arg2 ...</c>. Sin Python: la skill y su script son .NET.
///
///   · directorio de trabajo: el repo de la demo (una ruta relativa como "Directory.Packages.props" funciona)
///   · entorno limpio, como el shell: solo PATH, HOME, USER, temporales y variables de .NET/NuGet
///     (nada de FOUNDRY_* ni AZURE_*)
///   · 60 s como máximo (la primera ejecución compila; las siguientes salen de la caché de dotnet run)
/// Los argumentos llegan del modelo como un array JSON de cadenas.
/// </summary>
public sealed class DotnetScriptRunner(string workingDirectory, DemoLanguage language = DemoLanguage.Es)
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);

    private static readonly string[] PreservedVariables =
        ["PATH", "HOME", "USER", "TMPDIR", "TEMP", "TMP", "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES"];

    /// <summary>Firma de AgentFileSkillScriptRunner: se pasa como <c>new DotnetScriptRunner(repo).RunAsync</c>.</summary>
    public async Task<object?> RunAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(script.FullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return language.Pick(
                $"Error: solo se ejecutan scripts C# (.cs) con dotnet run; '{script.Name}' no lo es.",
                $"Error: only C# scripts (.cs) run with dotnet run; '{script.Name}' isn't one.");
        }

        if (!File.Exists(script.FullPath))
        {
            return language.Pick($"Error: no existe el script {script.FullPath}.", $"Error: the script {script.FullPath} doesn't exist.");
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(script.FullPath);
        startInfo.ArgumentList.Add("--");

        if (arguments is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (JsonElement element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return this.BadArguments(script);
                }

                startInfo.ArgumentList.Add(element.GetString()!);
            }
        }
        else if (arguments is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            return this.BadArguments(script);
        }

        // Entorno limpio: el script no hereda secretos del proceso del agente.
        var inherited = PreservedVariables
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(v => v.Value is not null)
            .ToList();
        startInfo.Environment.Clear();
        foreach (var (name, value) in inherited)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["UPGRADE_CAPTAIN_REPO"] = workingDirectory;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return language.Pick($"Error: no se pudo arrancar dotnet run para '{script.Name}'.", $"Error: dotnet run couldn't start for '{script.Name}'.");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScriptTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return language.Pick($"Error: '{script.Name}' superó {ScriptTimeout.TotalSeconds:0} s.", $"Error: '{script.Name}' exceeded {ScriptTimeout.TotalSeconds:0} s.");
        }

        string output = (await stdout.ConfigureAwait(false)).Trim();
        string error = (await stderr.ConfigureAwait(false)).Trim();
        if (error.Length > 0)
        {
            output += $"\nStderr:\n{error}";
        }

        if (process.ExitCode != 0)
        {
            output += language.Pick($"\nEl script terminó con código {process.ExitCode}", $"\nThe script exited with code {process.ExitCode}");
        }

        return output.Length == 0 ? language.Pick("(sin salida)", "(no output)") : output;
    }

    private string BadArguments(AgentFileSkillScript script) => language.Pick(
        $"Error: los argumentos de '{script.Name}' deben ser un array JSON de cadenas.",
        $"Error: the arguments of '{script.Name}' must be a JSON array of strings.");
}
