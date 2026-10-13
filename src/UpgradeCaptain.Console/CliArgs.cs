namespace UpgradeCaptain.Console;

/// <summary>
/// Flags de la consola (PLAN.md §4). Se definen todos desde el step-1 para que el diff de cada paso
/// muestre solo lo que cambia en el harness, no el parseo de argumentos.
/// </summary>
/// <param name="Context">--context normal|tiny · ventana de contexto que ve la compactación.</param>
/// <param name="Surface">--surface direct|codeact · cómo ve el modelo las tools (step-4).</param>
/// <param name="As">--as &lt;handle&gt; · quién pide el trabajo (step-6). Por defecto el operador.</param>
/// <param name="State">--state clean|after-mechanical-fix · estado del repo de la demo (step-6).</param>
/// <param name="Prompt">--prompt &lt;texto&gt; · ejecución sin consola, una sola vez (lo usa scripts/measure-surfaces.sh).</param>
/// <param name="Rest">Lo que queda: subcomandos como <c>run --job</c> o <c>approvals ...</c> (step-6).</param>
public sealed record CliArgs(string Context, string Surface, string As, string? State, string? Prompt, IReadOnlyList<string> Rest)
{
    /// <summary>Handle del operador en el escenario.</summary>
    public const string Operator = "@david-lorenzo";

    public static CliArgs Parse(string[] args)
    {
        string context = "normal", surface = "direct", requester = Operator;
        string? state = null, prompt = null;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : Fail($"Falta el valor de {args[i]}");
            switch (args[i])
            {
                case "--context": context = Next(); break;
                case "--surface": surface = Next(); break;
                case "--as": requester = Next(); break;
                case "--state": state = Next(); break;
                case "--prompt": prompt = Next(); break;
                default: rest.Add(args[i]); break;
            }
        }

        if (context is not ("normal" or "tiny")) { Fail("--context debe ser normal o tiny"); }
        if (surface is not ("direct" or "codeact")) { Fail("--surface debe ser direct o codeact"); }
        if (state is not (null or "clean" or "after-mechanical-fix")) { Fail("--state debe ser clean o after-mechanical-fix"); }

        return new CliArgs(context, surface, requester, state, prompt, rest);
    }

    /// <summary>Error de uso: una línea clara y código de salida 2, sin traza de pila.</summary>
    private static string Fail(string message)
    {
        System.Console.Error.WriteLine($"✘ {message}");
        Environment.Exit(2);
        return string.Empty;
    }

    /// <summary>
    /// Ventana de contexto para la compactación del harness.
    /// normal: la real de gpt-5.4 (1.050.000 / 128.000).
    /// tiny: tan pequeña que leer unos 10 ficheros del repo ya dispara la compactación
    /// (los resultados de tools antiguos se colapsan al pasar del 50 % del presupuesto de entrada).
    /// </summary>
    public (int MaxContextWindowTokens, int MaxOutputTokens) ContextWindow => this.Context == "tiny"
        ? (16_000, 8_000)
        : (1_050_000, 128_000);
}
