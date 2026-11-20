using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Un texto en los dos idiomas de la demo, juntos para que no se desincronicen.</summary>
public sealed record Bi(string Es, string En)
{
    public string this[DemoLanguage language] => language == DemoLanguage.En ? this.En : this.Es;

    /// <summary>El mismo texto en los dos idiomas (nombres de tipos, comandos…).</summary>
    public static Bi Same(string text) => new(text, text);
}

/// <summary>Formato en línea para el contenido: <c>`código`</c>, <c>**negrita**</c>, <c>*cursiva*</c> y <c>[texto](url)</c>.</summary>
public static partial class Inline
{
    public static MarkupString Render(string text)
    {
        string html = WebUtility.HtmlEncode(text);
        html = CodeRegex().Replace(html, "<code>$1</code>");
        html = BoldRegex().Replace(html, "<strong>$1</strong>");
        html = ItalicRegex().Replace(html, "<em>$1</em>");
        html = LinkRegex().Replace(html, "<a href=\"$2\" target=\"_blank\" rel=\"noopener\">$1</a>");
        return new MarkupString(html);
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<![*\w])\*([^*\s][^*]*)\*(?![*\w])")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+|/[^)\s]*)\)")]
    private static partial Regex LinkRegex();
}
