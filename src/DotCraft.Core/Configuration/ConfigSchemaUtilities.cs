using System.Collections;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

/// <summary>
/// Shared helpers for generated configuration schema metadata.
/// </summary>
public static class ConfigSchemaUtilities
{
    /// <summary>
    /// Derives sensitive-field paths from a generated configuration schema.
    /// </summary>
    public static string[][] BuildSensitivePaths(IEnumerable<ConfigSchemaSection> schema)
    {
        var paths = new List<string[]>();
        foreach (var section in schema)
        {
            var itemFields = section.ItemFields;
            if (itemFields is { Count: > 0 })
            {
                foreach (var field in itemFields.Where(f => f.Sensitive))
                    AddSensitivePath(paths, section, field);

                continue;
            }

            foreach (var field in section.Fields.Where(f => f.Sensitive))
                AddSensitivePath(paths, section, field);
        }

        return paths.ToArray();
    }

    /// <summary>
    /// Replaces every non-empty value at <paramref name="sensitivePaths"/> with <c>***</c>, in place.
    /// Config files may use either casing, so each path segment is matched case-insensitively.
    /// </summary>
    public static void MaskSensitiveValues(JsonObject root, string[][] sensitivePaths)
    {
        foreach (var path in sensitivePaths)
            MaskAtPath(root, path, 0);
    }

    /// <summary>
    /// Collects every field key the schema marks sensitive, in any section.
    /// </summary>
    public static IReadOnlySet<string> BuildSensitiveKeys(IEnumerable<ConfigSchemaSection> schema)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in schema)
        {
            foreach (var field in section.Fields.Where(f => f.Sensitive))
                keys.Add(field.Key);

            if (section.ItemFields is { } itemFields)
            {
                foreach (var field in itemFields.Where(f => f.Sensitive))
                    keys.Add(field.Key);
            }
        }

        return keys;
    }

    private static readonly string[] SensitiveKeySuffixes = ["ApiKey", "Token", "Secret", "Password"];

    private static readonly HashSet<string> SensitiveMapKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Headers",
        "EnvironmentVariables",
        "Env"
    };

    /// <summary>
    /// Masks every non-empty value under a sensitive key with <c>***</c>, at any depth and inside maps and lists.
    /// Keys ending in ApiKey, Token, Secret, or Password, the Authorization key, and all values of Headers,
    /// EnvironmentVariables, and Env maps count as sensitive regardless of <paramref name="sensitiveKeys"/>.
    /// </summary>
    public static void MaskSensitiveKeys(JsonNode? node, IReadOnlySet<string> sensitiveKeys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToArray())
                {
                    if (value is JsonValue scalar)
                    {
                        if (IsSensitiveKey(key, sensitiveKeys) && scalar.ToString().Length > 0)
                            obj[key] = "***";
                    }
                    else if (SensitiveMapKeys.Contains(key) && value is JsonObject map)
                    {
                        MaskAllScalars(map);
                    }
                    else
                    {
                        MaskSensitiveKeys(value, sensitiveKeys);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                    MaskSensitiveKeys(item, sensitiveKeys);
                break;
        }
    }

    private static bool IsSensitiveKey(string key, IReadOnlySet<string> sensitiveKeys) =>
        sensitiveKeys.Contains(key)
        || key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
        || SensitiveKeySuffixes.Any(suffix => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static void MaskAllScalars(JsonObject map)
    {
        foreach (var (key, value) in map.ToArray())
        {
            if (value is JsonValue scalar && scalar.ToString().Length > 0)
                map[key] = "***";
        }
    }

    /// <summary>
    /// Normalizes string defaults to match the Dashboard schema convention.
    /// </summary>
    public static object? NormalizeStringDefault(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Normalizes enum defaults to their string representation.
    /// </summary>
    public static object? NormalizeEnumDefault<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => value.ToString();

    /// <summary>
    /// Normalizes collection defaults, hiding empty collections.
    /// </summary>
    public static object? NormalizeCollectionDefault(IEnumerable? value)
    {
        if (value == null)
            return null;

        if (value is ICollection { Count: 0 })
            return null;

        var enumerator = value.GetEnumerator();
        try
        {
            return enumerator.MoveNext() ? value : null;
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static void MaskAtPath(JsonObject obj, string[] path, int depth)
    {
        var actualKey = FindCaseInsensitiveKey(obj, path[depth]);
        if (actualKey == null)
            return;

        if (depth == path.Length - 1)
        {
            if (obj[actualKey] is JsonValue value && value.ToString().Length > 0)
                obj[actualKey] = "***";
        }
        else if (obj[actualKey] is JsonObject nested)
        {
            MaskAtPath(nested, path, depth + 1);
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string key) =>
        obj.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Key;

    private static void AddSensitivePath(
        List<string[]> paths,
        ConfigSchemaSection section,
        ConfigSchemaField field)
    {
        if (section.RootKey != null)
        {
            paths.Add([section.RootKey, field.Key]);
        }
        else if (section.Path is { Length: > 0 })
        {
            var path = new string[section.Path.Length + 1];
            section.Path.CopyTo(path, 0);
            path[^1] = field.Key;
            paths.Add(path);
        }
        else
        {
            paths.Add([field.Key]);
        }
    }
}
