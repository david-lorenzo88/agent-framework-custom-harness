using System.Diagnostics;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Console;

/// <summary>--state clean|after-mechanical-fix: deja el repo de la demo en ese estado antes de arrancar.</summary>
internal static class DemoReset
{
    public static void Run(string state)
    {
        string script = Path.Combine(DemoPaths.Current.ProjectRoot, "scripts", "reset-demo.sh");
        var startInfo = new ProcessStartInfo("bash") { WorkingDirectory = DemoPaths.Current.ProjectRoot };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("--state");
        startInfo.ArgumentList.Add(state);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo ejecutar reset-demo.sh");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"reset-demo.sh --state {state} falló ({process.ExitCode}).");
        }
    }
}
