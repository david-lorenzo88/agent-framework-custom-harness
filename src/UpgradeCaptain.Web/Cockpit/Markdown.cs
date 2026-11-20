using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>
/// Markdown suficiente para las respuestas del modelo: títulos, listas, tablas, bloques de código, citas y párrafos,
/// con el formato en línea de <see cref="Inline"/>. Todo se codifica como HTML antes de dar formato.
/// </summary>
public static partial class Markdown
{
    public static MarkupString ToHtml(string text)
    {
        var html = new StringBuilder();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                string language = trimmed[3..].Trim();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[i]);
                    i++;
                }

                i++;
                string body = language is "csharp" or "cs" or "c#"
                    ? string.Join('\n', code.ToString().TrimEnd('\n').Split('\n').Select(l => Highlight.CSharp(l).Value))
                    : WebUtility.HtmlEncode(code.ToString().TrimEnd('\n'));
                html.Append("<pre class=\"md-code\"><code>").Append(body).Append("</code></pre>");
                continue;
            }

            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (HeadingRegex().Match(trimmed) is { Success: true } heading)
            {
                int level = Math.Min(heading.Groups[1].Value.Length + 2, 6);
                html.Append($"<h{level}>").Append(Inline.Render(heading.Groups[2].Value).Value).Append($"</h{level}>");
                i++;
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && SeparatorRegex().IsMatch(lines[i + 1].Trim()))
            {
                html.Append("<table class=\"md-table\"><thead><tr>");
                foreach (string cell in Cells(trimmed))
                {
                    html.Append("<th>").Append(Inline.Render(cell).Value).Append("</th>");
                }

                html.Append("</tr></thead><tbody>");
                i += 2;
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    html.Append("<tr>");
                    foreach (string cell in Cells(lines[i].Trim()))
                    {
                        html.Append("<td>").Append(Inline.Render(cell).Value).Append("</td>");
                    }

                    html.Append("</tr>");
                    i++;
                }

                html.Append("</tbody></table>");
                continue;
            }

            if (ListRegex().IsMatch(line))
            {
                bool ordered = char.IsDigit(trimmed[0]);
                html.Append(ordered ? "<ol>" : "<ul>");
                while (i < lines.Length && (ListRegex().IsMatch(lines[i]) || (lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim().Length > 0)))
                {
                    Match item = ListRegex().Match(lines[i]);
                    string content = item.Success ? lines[i][item.Length..] : lines[i].Trim();
                    bool nested = item.Success && item.Groups[1].Value.Length >= 2;
                    html.Append(nested ? "<li class=\"nested\">" : "<li>").Append(Inline.Render(content).Value).Append("</li>");
                    i++;
                }

                html.Append(ordered ? "</ol>" : "</ul>");
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                html.Append("<blockquote>");
                while (i < lines.Length && lines[i].Trim().StartsWith('>'))
                {
                    html.Append(Inline.Render(lines[i].Trim().TrimStart('>').Trim()).Value).Append("<br/>");
                    i++;
                }

                html.Append("</blockquote>");
                continue;
            }

            var paragraph = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0 && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal)
                   && !HeadingRegex().IsMatch(lines[i].Trim()) && !ListRegex().IsMatch(lines[i]) && !lines[i].Trim().StartsWith('|'))
            {
                paragraph.Add(Inline.Render(lines[i].Trim()).Value);
                i++;
            }

            if (paragraph.Count == 0)
            {
                html.Append("<p>").Append(Inline.Render(trimmed).Value).Append("</p>");
                i++;
                continue;
            }

            html.Append("<p>").Append(string.Join("<br/>", paragraph)).Append("</p>");
        }

        return new MarkupString(html.ToString());
    }

    private static IEnumerable<string> Cells(string row) => row.Trim().Trim('|').Split('|').Select(c => c.Trim());

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^(\s*)(?:[-*•]|\d+[.)])\s+")]
    private static partial Regex ListRegex();
}
