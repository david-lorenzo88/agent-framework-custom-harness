namespace UpgradeCaptain.Core;

/// <summary>
/// Carga <c>.env</c> de la raíz de upgrade-captain en las variables de entorno del proceso.
/// No sobrescribe variables que ya existan. Debe llamarse ANTES de crear DefaultAzureCredential,
/// que lee AZURE_TOKEN_CREDENTIALS al construirse.
/// </summary>
public static class DotEnv
{
    public static void Load(string? path = null)
    {
        path ??= Path.Combine(DemoPaths.Current.ProjectRoot, ".env");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith('#') || eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>Lee una variable obligatoria con un mensaje de error claro.</summary>
    public static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Falta la variable {name}. Copia .env.example a .env y rellénala.");
}
