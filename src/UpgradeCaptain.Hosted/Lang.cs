using UpgradeCaptain.Core;

namespace UpgradeCaptain.Hosted;

/// <summary>Mensajes del Hosted Agent en el idioma de la imagen (UPGRADE_CAPTAIN_LANG, que fija el Dockerfile).</summary>
internal static class Lang
{
    public static string Pick(string es, string en) => DemoLanguages.Current.Pick(es, en);
}
