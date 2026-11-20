using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UpgradeCaptain.Web.Cockpit;

public enum LineTone
{
    Plain,
    Command,
    Good,
    Bad,
    Warn,
    Dim,
}

public sealed record TerminalLine(string Text, LineTone Tone);

/// <summary>Resultado de <c>dotnet test</c> leído de la última línea de resumen (en inglés o en español).</summary>
public sealed record TestSummary(int Total, int Passed, int Failed)
{
    public bool Green => this.Failed == 0 && this.Total > 0;
}

/// <summary>
/// Un terminal de solo lectura en la página: ejecuta procesos (dotnet, git, scripts) y pinta su salida en directo.
/// Es el antiguo T3 «¿qué dice la realidad?»: nada del agente ni del modelo pasa por aquí.
/// </summary>
public sealed partial class Terminal(string name, Action notify)
{
    private const int MaxLines = 600;
    private readonly object _gate = new();
    private readonly List<TerminalLine> _lines = [];
    private CancellationTokenSource? _cts;

    public string Name { get; } = name;

    public bool Running { get; private set; }

    public string? Title { get; private set; }

    public int? LastExitCode { get; private set; }

    public TestSummary? LastTests { get; private set; }

    public bool? LastBuildOk { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyList<TerminalLine> Lines
    {
        get
        {
            lock (this._gate)
            {
                return [.. this._lines];
            }
        }
    }

    public void Clear()
    {
        lock (this._gate)
        {
            this._lines.Clear();
        }

        this.LastTests = null;
        this.LastBuildOk = null;
        notify();
    }

    public void Cancel() => this._cts?.Cancel();

    public void Echo(string text, LineTone tone = LineTone.Dim)
    {
        this.Append(new TerminalLine(text, tone));
        notify();
    }

    /// <summary>Ejecuta un comando y devuelve su código de salida. Varios comandos seguidos se encadenan con &&.</summary>
    public async Task<int> RunAsync(string title, string workingDirectory, IReadOnlyDictionary<string, string>? environment, params (string File, string[] Args)[] commands)
    {
        if (this.Running)
        {
            return -1;
        }

        this.Running = true;
        this.Title = title;
        this._cts = new CancellationTokenSource();
        int exit = 0;
        notify();
        try
        {
            foreach (var (file, args) in commands)
            {
                exit = await this.RunOneAsync(file, args, workingDirectory, environment, this._cts.Token);
                if (exit != 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            this.Append(new TerminalLine("^C", LineTone.Warn));
            exit = -2;
        }
        catch (Exception ex)
        {
            this.Append(new TerminalLine(ex.Message, LineTone.Bad));
            exit = -3;
        }
        finally
        {
            this.Running = false;
            this.LastExitCode = exit;
            this.FinishedAt = DateTimeOffset.Now;
            notify();
        }

        return exit;
    }

    private async Task<int> RunOneAsync(string file, string[] args, string workingDirectory, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        bool isBuild = file == "dotnet" && args.FirstOrDefault() == "build";
        bool isTest = file == "dotnet" && args.FirstOrDefault() == "test";
        this.Append(new TerminalLine($"$ {file} {string.Join(' ', args.Select(Quote))}", LineTone.Command));
        notify();

        var startInfo = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value.Length == 0)
                {
                    startInfo.Environment.Remove(key); // vacío = sin la variable (p. ej. LC_ALL en español)
                }
                else
                {
                    startInfo.Environment[key] = value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => this.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => this.OnLine(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        process.WaitForExit(); // vacía los últimos eventos de salida
        if (isBuild)
        {
            this.LastBuildOk = process.ExitCode == 0;
        }

        if (isTest)
        {
            this.LastTests = this.ParseTests();
        }

        this.Append(new TerminalLine($"exit {process.ExitCode}", process.ExitCode == 0 ? LineTone.Good : LineTone.Bad));
        return process.ExitCode;
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        this.Append(new TerminalLine(line, Classify(line)));
        notify();
    }

    private TestSummary? ParseTests()
    {
        foreach (TerminalLine line in this.Lines.Reverse())
        {
            Match m = SummaryRegex().Match(line.Text);
            if (m.Success)
            {
                int failed = int.Parse(m.Groups["failed"].Value);
                int passed = int.Parse(m.Groups["passed"].Value);
                int total = int.Parse(m.Groups["total"].Value);
                return new TestSummary(total, passed, failed);
            }
        }

        return null;
    }

    private static LineTone Classify(string line) => line switch
    {
        _ when GoodRegex().IsMatch(line) => LineTone.Good,
        _ when FailRegex().IsMatch(line) => LineTone.Bad,
        _ when line.Contains("warning", StringComparison.OrdinalIgnoreCase) || line.Contains("advertencia", StringComparison.OrdinalIgnoreCase) => LineTone.Warn,
        _ when line.TrimStart().StartsWith("at ", StringComparison.Ordinal) || line.TrimStart().StartsWith("en ", StringComparison.Ordinal) => LineTone.Dim,
        _ => LineTone.Plain,
    };

    private void Append(TerminalLine line)
    {
        lock (this._gate)
        {
            this._lines.Add(line);
            if (this._lines.Count > MaxLines)
            {
                this._lines.RemoveRange(0, this._lines.Count - MaxLines);
            }
        }
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    // "Failed!  - Failed:     8, Passed:    16, Skipped:     0, Total:    24" · "Con error! - Con error:     8, Superado:    16, Omitido: 0, Total: 24"
    [GeneratedRegex(@"(?:Failed|Con error):\s+(?<failed>\d+),\s+(?:Passed|Superado):\s+(?<passed>\d+),\s+(?:Skipped|Omitido):\s+\d+,\s+Total:\s+(?<total>\d+)")]
    private static partial Regex SummaryRegex();

    [GeneratedRegex(@"\berror\b|\[FAIL\]|^\s*(Failed|Con error)\b|Failed!|Con error!|Assert\.|Expected:|Actual:|Esperado:|Real:|Build FAILED|ERROR", RegexOptions.IgnoreCase)]
    private static partial Regex FailRegex();

    [GeneratedRegex(@"Passed!|Correctas!|Build succeeded|Compilación correcta|^\s*Passed\b|✔|\b0 Error|0 Errores")]
    private static partial Regex GoodRegex();
}
