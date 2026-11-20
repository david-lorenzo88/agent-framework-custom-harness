using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using UpgradeCaptain.Core;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Una línea ya pintada de un fragmento de código.</summary>
public sealed record CodeLine(int Number, MarkupString Html, bool Emphasis, bool Comment);

/// <summary>Un fragmento listo para enseñar: el código real con los comentarios en el idioma de la charla.</summary>
public sealed record Snippet(string Id, string Source, string Kind, IReadOnlyList<CodeLine> Lines, string PlainText);

/// <summary>
/// Los fragmentos de Snippets/*.cs. Formato:
///   · primera línea <c>//@source [tag:]ruta</c>: de dónde sale el código (main o una tag de la cadena de pasos)
///   · <c>//en: …</c> y <c>//es: …</c>: comentarios en cada idioma, en su propia línea o al final de una línea de código
///   · <c>//!</c> al final del código: línea destacada
///   · <c>// …</c>: código omitido
/// El código (sin comentarios) es literal: un test comprueba que cada línea existe en el fichero original.
/// </summary>
public static partial class Snippets
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Snippets");

    public static Snippet Load(string id, DemoLanguage language)
    {
        if (id.StartsWith("skill:", StringComparison.Ordinal))
        {
            return LoadSkill(id["skill:".Length..], language);
        }

        string file = Path.Combine(Directory, id + ".cs");
        string[] raw = File.Exists(file) ? File.ReadAllLines(file) : [$"// missing snippet: {id}"];
        string source = raw.Length > 0 && raw[0].StartsWith("//@source ", StringComparison.Ordinal) ? raw[0]["//@source ".Length..].Trim() : "?";

        var lines = new List<CodeLine>();
        var plain = new StringBuilder();
        foreach (string line in raw.Skip(source == "?" ? 0 : 1))
        {
            ParsedLine parsed = Parse(line);
            string? comment = language == DemoLanguage.En ? parsed.En : parsed.Es;
            bool otherLanguageOnly = parsed.Code.Trim().Length == 0 && comment is null && (parsed.En is not null || parsed.Es is not null);
            if (otherLanguageOnly)
            {
                continue; // comentario solo en el otro idioma
            }

            string text = comment is null ? parsed.Code.TrimEnd() : AppendComment(parsed.Code, comment);
            plain.AppendLine(text);
            bool isComment = text.TrimStart().StartsWith("//", StringComparison.Ordinal);
            lines.Add(new CodeLine(lines.Count + 1, Highlight.CSharp(text), parsed.Emphasis, isComment));
        }

        return new Snippet(id, source, "csharp", lines, plain.ToString());
    }

    /// <summary>Una línea de un fragmento separada en código, comentarios por idioma y marca de destacado.</summary>
    public sealed record ParsedLine(string Code, string? En, string? Es, bool Emphasis);

    public static ParsedLine Parse(string line)
    {
        int cut = MarkerRegex().Match(line) is { Success: true } m ? m.Index : line.Length;
        string code = line[..cut];
        string rest = line[cut..];
        bool emphasis = rest.Contains("//!", StringComparison.Ordinal);
        string? en = Segment(rest, "//en:");
        string? es = Segment(rest, "//es:");
        return new ParsedLine(code, en, es, emphasis);
    }

    private static string? Segment(string rest, string marker)
    {
        int start = rest.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = rest.Length;
        foreach (string other in new[] { "//en:", "//es:", "//!" })
        {
            int next = rest.IndexOf(other, start, StringComparison.Ordinal);
            if (next >= 0 && next < end)
            {
                end = next;
            }
        }

        return rest[start..end].Trim();
    }

    private static string AppendComment(string code, string comment) =>
        code.Trim().Length == 0 ? $"{code}// {comment}" : $"{code.TrimEnd()} // {comment}";

    /// <summary>El SKILL.md real, en el idioma de la charla (skills/ o skills-en/ junto al ejecutable).</summary>
    private static Snippet LoadSkill(string skill, DemoLanguage language)
    {
        string dir = language == DemoLanguage.En ? "skills-en" : "skills";
        string file = Path.Combine(AppContext.BaseDirectory, dir, skill, "SKILL.md");
        string[] raw = File.Exists(file) ? File.ReadAllLines(file) : [$"missing: {dir}/{skill}/SKILL.md"];
        var lines = new List<CodeLine>();
        foreach (string line in raw)
        {
            // El frontmatter (name, description) es lo único que ve el modelo hasta que llama a load_skill.
            bool frontmatter = lines.Count < 4 && (line.StartsWith("name:", StringComparison.Ordinal) || line.StartsWith("description:", StringComparison.Ordinal));
            lines.Add(new CodeLine(lines.Count + 1, Highlight.Markdown(line), frontmatter, false));
        }

        return new Snippet("skill:" + skill, $"{dir}/{skill}/SKILL.md", "markdown", lines, string.Join('\n', raw));
    }

    [GeneratedRegex(@"//(en:|es:|!)")]
    private static partial Regex MarkerRegex();
}

/// <summary>Resaltado sencillo en el servidor (sin JavaScript de terceros): C# y Markdown.</summary>
public static partial class Highlight
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "case", "catch", "class", "const", "continue", "default",
        "double", "else", "enum", "false", "finally", "for", "foreach", "if", "in", "init", "int", "internal", "is", "long",
        "namespace", "new", "not", "null", "object", "out", "override", "private", "protected", "public", "readonly", "record",
        "required", "return", "sealed", "static", "string", "switch", "this", "throw", "true", "try", "using", "var", "void",
        "when", "while", "yield", "and", "or", "get", "set", "decimal", "char", "params", "with",
    ];

    public static MarkupString CSharp(string line)
    {
        var sb = new StringBuilder();
        foreach (Match token in CSharpRegex().Matches(line))
        {
            string text = WebUtility.HtmlEncode(token.Value);
            string? css = token.Groups switch
            {
                var g when g["comment"].Success => "tok-comment",
                var g when g["string"].Success => "tok-string",
                var g when g["number"].Success => "tok-number",
                var g when g["word"].Success && Keywords.Contains(token.Value) => "tok-keyword",
                var g when g["word"].Success && char.IsUpper(token.Value[0]) => "tok-type",
                var g when g["call"].Success => "tok-call",
                _ => null,
            };
            sb.Append(css is null ? text : $"<span class=\"{css}\">{text}</span>");
        }

        return new MarkupString(sb.Length == 0 ? " " : sb.ToString());
    }

    public static MarkupString Markdown(string line)
    {
        string html = WebUtility.HtmlEncode(line);
        if (line.StartsWith('#'))
        {
            return new MarkupString($"<span class=\"tok-keyword\">{html}</span>");
        }

        if (line.StartsWith("name:", StringComparison.Ordinal) || line.StartsWith("description:", StringComparison.Ordinal))
        {
            int colon = html.IndexOf(':');
            return new MarkupString($"<span class=\"tok-type\">{html[..(colon + 1)]}</span><span class=\"tok-string\">{html[(colon + 1)..]}</span>");
        }

        if (line == "---" || line.StartsWith("```", StringComparison.Ordinal))
        {
            return new MarkupString($"<span class=\"tok-comment\">{html}</span>");
        }

        html = InlineCodeRegex().Replace(html, "<span class=\"tok-string\">`$1`</span>");
        html = BoldRegex().Replace(html, "<span class=\"tok-call\">**$1**</span>");
        return new MarkupString(html.Length == 0 ? " " : html);
    }

    // comentario | cadena (normal, interpolada o verbatim) | número | identificador seguido de "(" | identificador | resto
    [GeneratedRegex(@"(?<comment>//.*$)|(?<string>\$?@?""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)')|(?<number>\b\d[\d_.]*[mMdDfFlL]?\b)|(?<call>\b[a-z_][A-Za-z0-9_]*(?=\())|(?<word>\b[A-Za-z_][A-Za-z0-9_]*\b)|(?<other>\s+|.)")]
    private static partial Regex CSharpRegex();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();
}
