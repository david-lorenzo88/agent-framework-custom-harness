using System.Diagnostics;
using System.Text;

namespace UpgradeCaptain.Harness;

/// <summary>Resultado de un proceso hijo.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public string Output => this.StdOut + this.StdErr;
}

/// <summary>Ejecuta procesos (dotnet, git). Interfaz para poder sustituirlo por un falso en los tests.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ejecuta procesos DIRECTAMENTE (sin shell): el harness verifica por su cuenta, no a través de la tool
/// run_shell del modelo. Mata el árbol de procesos si se pasa del tiempo.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public static ProcessRunner Default { get; } = new();

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"No se pudo arrancar {fileName}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        return new ProcessResult(timedOut ? 124 : process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), timedOut);
    }
}
