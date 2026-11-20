using System.Text.Json;

namespace UpgradeCaptain.Web.Cockpit;

/// <summary>Cómo se enseña cada tool en el panel: un icono, una familia y una línea con lo importante.</summary>
public static class ToolText
{
    public static string Family(string tool) => tool switch
    {
        "list_projects" or "list_source_files" or "read_source" or "get_advisories" or "get_migration_notes" => "repo",
        "run_shell" => "shell",
        "execute_code" => "codeact",
        "load_skill" or "read_skill_resource" or "run_skill_script" => "skill",
        _ when tool.StartsWith("todos_", StringComparison.Ordinal) => "todo",
        _ when tool.StartsWith("mode_", StringComparison.Ordinal) => "mode",
        _ when tool.StartsWith("file_memory_", StringComparison.Ordinal) => "memory",
        _ when tool.StartsWith("file_access_", StringComparison.Ordinal) => IsWrite(tool) ? "write" : "file",
        _ => "tool",
    };

    public static string Icon(string tool) => Family(tool) switch
    {
        "repo" => "◆",
        "shell" => "❯",
        "codeact" => "λ",
        "skill" => "✦",
        "todo" => "☑",
        "mode" => "◐",
        "memory" => "◎",
        "write" => "✎",
        "file" => "▤",
        _ => "•",
    };

    public static bool IsWrite(string tool) =>
        tool is "file_access_write" or "file_access_replace" or "file_access_replace_lines" or "file_access_delete";

    /// <summary>Una línea: <c>read_source · src/…/FareCalculator.cs</c>, <c>run_shell · dotnet test</c>…</summary>
    public static string Summarize(string tool, IDictionary<string, object?>? arguments)
    {
        string? Arg(string name) => arguments is not null && arguments.TryGetValue(name, out object? value) && value is not null
            ? value is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : value.ToString()
            : null;

        string? detail = tool switch
        {
            "read_source" => Arg("path"),
            "list_source_files" => Arg("project") is { Length: > 0 } p ? p : "*",
            "get_advisories" => Arg("package"),
            "get_migration_notes" => $"{Arg("package")} {Arg("fromVersion")} → {Arg("toVersion")}",
            "run_shell" => Arg("command"),
            "load_skill" => Arg("skillName") ?? Arg("name"),
            "read_skill_resource" => $"{Arg("skillName")} · {Arg("resourceName") ?? Arg("resource")}",
            "run_skill_script" => $"{Arg("skillName")} · {Arg("scriptName")} {Compact(arguments, "arguments")}",
            "todos_add" => Compact(arguments, "todos") ?? Arg("title"),
            "todos_complete" or "todos_remove" => Compact(arguments, "ids") ?? Compact(arguments, "todos"),
            "mode_set" => Arg("mode"),
            "execute_code" => FirstLine(Arg("code")),
            _ when tool.StartsWith("file_", StringComparison.Ordinal) => Arg("fileName") ?? Arg("path") ?? Arg("pattern"),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(detail) ? tool : $"{tool} · {detail.Trim()}";
    }

    /// <summary>Argumentos como JSON legible (para el detalle desplegable).</summary>
    public static string Pretty(IDictionary<string, object?>? arguments) =>
        arguments is null || arguments.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>El resultado de una tool, en texto y recortado para el panel.</summary>
    public static string ResultText(object? result, int max = 6000)
    {
        string text = result switch
        {
            null => string.Empty,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
            JsonElement json => JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            _ => JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        };
        return text.Length <= max ? text : text[..max] + "\n…";
    }

    private static string? Compact(IDictionary<string, object?>? arguments, string name)
    {
        if (arguments is null || !arguments.TryGetValue(name, out object? value) || value is null)
        {
            return null;
        }

        string json = value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value);
        json = json.Replace("\n", " ").Replace("  ", " ");
        return json.Length <= 120 ? json : json[..117] + "…";
    }

    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
}
