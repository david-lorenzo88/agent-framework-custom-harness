using System.Security.Cryptography;
using System.Text;

namespace UpgradeCaptain.Harness;

/// <summary>Un test que falla: nombre completo, primera parte del mensaje y, si se conoce, fichero:línea.</summary>
public sealed record FailedTest(string Name, string Message, string? Location = null);

/// <summary>
/// Lo que el harness ha comprobado por su cuenta: build, tests y paquetes vulnerables.
/// Es la definición de "terminado" del step-6, independiente de lo que diga el modelo.
/// </summary>
public sealed record VerificationResult
{
    public required bool BuildSucceeded { get; init; }

    /// <summary>Hasta 10 errores de compilación, como <c>fichero:línea CÓDIGO mensaje</c>.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    public required bool TestsPassed { get; init; }

    public int TestsTotal { get; init; }

    public int TestsFailed { get; init; }

    /// <summary>Hasta 10 tests fallidos.</summary>
    public required IReadOnlyList<FailedTest> FailedTests { get; init; }

    /// <summary>Paquetes resueltos que siguen afectados por un aviso, p. ej. <c>Teide.Geo 1.8.0 (TEIDE-2026-0042)</c>.</summary>
    public required IReadOnlyList<string> VulnerablePackages { get; init; }

    /// <summary>Versión resuelta de los paquetes vigilados (id → versión).</summary>
    public IReadOnlyDictionary<string, string> ResolvedVersions { get; init; } = new Dictionary<string, string>();

    public required TimeSpan Duration { get; init; }

    public DateTimeOffset VerifiedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Verde = compila, pasan todos los tests y no queda ningún paquete vulnerable.</summary>
    public bool IsGreen => this.BuildSucceeded && this.TestsPassed && this.VulnerablePackages.Count == 0;

    /// <summary>Huella del conjunto de fallos, para detectar que el agente está atascado en el mismo error.</summary>
    public string Fingerprint
    {
        get
        {
            string material = string.Join('\n', this.Errors.Order(StringComparer.Ordinal))
                + "\n--\n" + string.Join('\n', this.FailedTests.Select(t => t.Name).Order(StringComparer.Ordinal))
                + "\n--\n" + string.Join('\n', this.VulnerablePackages.Order(StringComparer.Ordinal));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..12];
        }
    }

    /// <summary>Resumen de una línea para la consola, el estado del repo y el log de auditoría.</summary>
    public string Summary =>
        $"build {(this.BuildSucceeded ? "✅" : $"❌ ({this.Errors.Count} errores)")} · " +
        (this.BuildSucceeded ? $"tests {this.TestsTotal - this.TestsFailed}/{this.TestsTotal}" : "tests sin ejecutar") +
        (this.VulnerablePackages.Count > 0 ? $" · vulnerable: {string.Join(", ", this.VulnerablePackages)}" : string.Empty);

    /// <summary>
    /// Feedback para el modelo cuando NO está en verde: en español, accionable, con fichero y línea,
    /// y como mucho <paramref name="maxChars"/> caracteres.
    /// </summary>
    public string ToFeedback(int maxChars = 2000)
    {
        const string Header = "🔎 Verificación del harness (dotnet build + dotnet test, ejecutados por el harness, no por ti): ❌ NO está en verde.\n";
        const string Trailer = "Arregla la causa en el código de producción (no cambies los tests para que pasen) y vuelve a terminar. "
            + "La tarea no está terminada hasta que esta verificación esté en verde.";

        var lines = new List<string>();
        if (!this.BuildSucceeded)
        {
            lines.Add($"Build: ❌ {this.Errors.Count} error(es):");
            lines.AddRange(this.Errors.Select(e => $"  - {e}"));
        }
        else if (this.TestsPassed)
        {
            lines.Add($"Build: ✅. Tests: ✅ {this.TestsTotal}/{this.TestsTotal}.");
        }
        else
        {
            lines.Add($"Build: ✅. Tests: {this.TestsFailed} de {this.TestsTotal} fallan:");
            lines.AddRange(this.FailedTests.Select(t => $"  - {t.Name}{(t.Location is null ? string.Empty : $" ({t.Location})")}: {t.Message}"));
        }

        lines.AddRange(this.VulnerablePackages.Select(p => $"Paquete vulnerable: {p}. Hay que actualizarlo a una versión corregida."));

        // El cierre (qué hacer) cabe SIEMPRE: si no caben todos los detalles, se recortan los últimos.
        var sb = new StringBuilder(Header);
        int budget = maxChars - Header.Length - Trailer.Length - 40;
        int omitted = 0;
        foreach (string line in lines)
        {
            if (sb.Length + line.Length + 1 - Header.Length > budget)
            {
                omitted++;
                continue;
            }

            sb.AppendLine(line);
        }

        if (omitted > 0)
        {
            sb.AppendLine($"  … y {omitted} línea(s) más.");
        }

        return sb.Append(Trailer).ToString();
    }
}
