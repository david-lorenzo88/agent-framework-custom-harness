namespace UpgradeCaptain.Core;

/// <summary>
/// Idioma de la demo: el del agente (instrucciones, tools, skills, mensajes del harness) y el de los datos
/// (avisos, notas de versión). Español por defecto, como la charla original; la cabina web y
/// UPGRADE_CAPTAIN_LANG=en lo cambian a inglés.
/// </summary>
public enum DemoLanguage
{
    Es,
    En,
}

public static class DemoLanguages
{
    /// <summary>El idioma activo: UPGRADE_CAPTAIN_LANG=en|es (por defecto, español).</summary>
    public static DemoLanguage Current => Parse(Environment.GetEnvironmentVariable("UPGRADE_CAPTAIN_LANG"));

    public static DemoLanguage Parse(string? value) =>
        value?.Trim().ToLowerInvariant() is "en" or "english" or "en-us" or "en-gb" ? DemoLanguage.En : DemoLanguage.Es;

    /// <summary>El texto en el idioma pedido. Los dos textos van juntos en el código para que no se desincronicen.</summary>
    public static string Pick(this DemoLanguage language, string es, string en) => language == DemoLanguage.En ? en : es;

    /// <summary>"es" o "en".</summary>
    public static string Code(this DemoLanguage language) => language == DemoLanguage.En ? "en" : "es";
}
