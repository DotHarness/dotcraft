using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.CodeMode;

internal sealed partial class CodeModeTypeScript
{
    private const int MaxDepth = 32;

    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly JsonElement _root;
    private readonly bool _inline;
    private readonly HashSet<string> _expanding = new(StringComparer.Ordinal);

    private CodeModeTypeScript(JsonElement root, bool inline)
    {
        _root = root;
        _inline = inline;
    }

    public static string Render(JsonElement schema, int indent = 0, bool inline = false) =>
        new CodeModeTypeScript(schema, inline).Type(schema, indent, 0);

    private string Type(JsonElement schema, int indent, int depth)
    {
        if (schema.ValueKind == JsonValueKind.False)
            return "never";
        if (schema.ValueKind != JsonValueKind.Object || depth > MaxDepth)
            return "unknown";

        if (schema.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String)
            return Reference(reference.GetString()!, indent, depth);

        var parts = new List<string>();
        var core = Core(schema, indent, depth);
        if (core is not null)
            parts.Add(core);
        foreach (var keyword in (string[])["anyOf", "oneOf"])
        {
            if (Members(schema, keyword, indent, depth) is { } members)
                parts.Add(Union(members));
        }
        if (Members(schema, "allOf", indent, depth) is { } all)
            parts.AddRange(all);

        var type = parts.Count switch
        {
            0 => "unknown",
            1 => parts[0],
            _ => string.Join(" & ", parts.Select(Grouped))
        };
        return schema.TryGetProperty("nullable", out var nullable) && nullable.ValueKind == JsonValueKind.True
            ? Union([type, "null"])
            : type;
    }

    private string Reference(string reference, int indent, int depth)
    {
        if (!reference.StartsWith('#') || _expanding.Contains(reference) || !TryResolve(reference, out var target))
            return "unknown";
        _expanding.Add(reference);
        try
        {
            return Type(target, indent, depth + 1);
        }
        finally
        {
            _expanding.Remove(reference);
        }
    }

    private bool TryResolve(string reference, out JsonElement target)
    {
        target = _root;
        foreach (var segment in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(segment).Replace("~1", "/").Replace("~0", "~");
            if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(name, out var child))
                target = child;
            else if (target.ValueKind == JsonValueKind.Array && int.TryParse(name, out var index)
                     && index >= 0 && index < target.GetArrayLength())
                target = target[index];
            else
                return false;
        }
        return true;
    }

    private string? Core(JsonElement schema, int indent, int depth)
    {
        if (schema.TryGetProperty("const", out var constant))
            return Literal(constant);
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
            return values.GetArrayLength() == 0 ? "never" : Union(values.EnumerateArray().Select(Literal).ToList());

        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
                return Named(type.GetString()!, schema, indent, depth);
            if (type.ValueKind == JsonValueKind.Array)
            {
                return Union(type.EnumerateArray()
                    .Where(static name => name.ValueKind == JsonValueKind.String)
                    .Select(name => Named(name.GetString()!, schema, indent, depth))
                    .ToList());
            }
        }
        if (schema.TryGetProperty("properties", out _) || schema.TryGetProperty("additionalProperties", out _))
            return Object(schema, indent, depth);
        if (schema.TryGetProperty("items", out _) || schema.TryGetProperty("prefixItems", out _))
            return Array(schema, indent, depth);
        return null;
    }

    private string Named(string name, JsonElement schema, int indent, int depth) => name switch
    {
        "string" => "string",
        "number" or "integer" => "number",
        "boolean" => "boolean",
        "null" => "null",
        "array" => Array(schema, indent, depth),
        "object" => Object(schema, indent, depth),
        _ => "unknown"
    };

    private List<string>? Members(JsonElement schema, string keyword, int indent, int depth)
    {
        if (!schema.TryGetProperty(keyword, out var members) || members.ValueKind != JsonValueKind.Array)
            return null;
        var rendered = members.EnumerateArray()
            .Select(member => Type(member, indent, depth + 1))
            .ToList();
        return rendered.Count == 0 || rendered.All(static member => member == "unknown") ? null : rendered;
    }

    private string Array(JsonElement schema, int indent, int depth)
    {
        var tuple = schema.TryGetProperty("prefixItems", out var prefix) && prefix.ValueKind == JsonValueKind.Array
            ? prefix
            : schema.TryGetProperty("items", out var legacy) && legacy.ValueKind == JsonValueKind.Array
                ? legacy
                : default;
        if (tuple.ValueKind == JsonValueKind.Array)
            return "[" + string.Join(", ", tuple.EnumerateArray().Select(item => Type(item, indent, depth + 1))) + "]";
        return schema.TryGetProperty("items", out var items)
            ? Grouped(Type(items, indent, depth + 1)) + "[]"
            : "unknown[]";
    }

    private string Object(JsonElement schema, int indent, int depth)
    {
        var required = schema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray()
                .Where(static name => name.ValueKind == JsonValueKind.String)
                .Select(static name => name.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        var members = new List<(string? Comment, string Line)>();
        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                var comment = property.Value.ValueKind == JsonValueKind.Object
                              && property.Value.TryGetProperty("description", out var description)
                              && description.ValueKind == JsonValueKind.String
                    ? description.GetString()
                    : null;
                var optional = required.Contains(property.Name) ? "" : "?";
                members.Add((comment, $"{PropertyName(property.Name)}{optional}: {Type(property.Value, indent + 2, depth + 1)};"));
            }
        }

        var additional = schema.TryGetProperty("additionalProperties", out var extra) ? extra : default;
        if (additional.ValueKind is JsonValueKind.Object or JsonValueKind.True)
            members.Add((null, $"[key: string]: {Type(additional, indent + 2, depth + 1)};"));

        if (members.Count == 0)
            return additional.ValueKind == JsonValueKind.False ? "{}" : "Record<string, unknown>";
        if (_inline)
            return "{ " + string.Join(" ", members.Select(static member => member.Line)).TrimEnd(';') + " }";

        var pad = new string(' ', indent + 2);
        var builder = new StringBuilder("{\n");
        foreach (var (comment, line) in members)
        {
            if (!string.IsNullOrWhiteSpace(comment))
            {
                foreach (var commentLine in comment.Split('\n'))
                {
                    var text = commentLine.TrimEnd('\r').Trim();
                    if (text.Length > 0)
                        builder.Append(pad).Append("// ").Append(text).Append('\n');
                }
            }
            builder.Append(pad).Append(line).Append('\n');
        }
        return builder.Append(new string(' ', indent)).Append('}').ToString();
    }

    private static string Literal(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString(), StringOptions),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => "unknown"
    };

    private static string Union(IReadOnlyList<string> members)
    {
        var distinct = members.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Contains("unknown"))
            return "unknown";
        return distinct.Count == 1 ? distinct[0] : string.Join(" | ", distinct.Select(Grouped));
    }

    private static string Grouped(string type) => HasTopLevelOperator(type) ? $"({type})" : type;

    private static bool HasTopLevelOperator(string type)
    {
        var depth = 0;
        var quoted = false;
        for (var index = 0; index < type.Length; index++)
        {
            var character = type[index];
            if (quoted)
            {
                if (character == '\\')
                    index++;
                else if (character == '"')
                    quoted = false;
                continue;
            }
            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case '{' or '[' or '(' or '<':
                    depth++;
                    break;
                case '}' or ']' or ')' or '>':
                    depth--;
                    break;
                case '|' or '&' when depth == 0:
                    return true;
            }
        }
        return false;
    }

    private static string PropertyName(string name) =>
        IdentifierPattern().IsMatch(name) ? name : JsonSerializer.Serialize(name, StringOptions);

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex IdentifierPattern();
}
