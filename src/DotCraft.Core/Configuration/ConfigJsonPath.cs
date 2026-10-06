using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

internal static class ConfigJsonPath
{
    public static string[] Split(string keyPath)
    {
        var segments = new List<string>();
        var segment = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < keyPath.Length; i++)
        {
            var c = keyPath[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < keyPath.Length)
                    segment.Append(keyPath[++i]);
                else if (c == '"')
                    (quoted, closed) = (false, true);
                else
                    segment.Append(c);
            }
            else if (c == '.')
            {
                segments.Add(segment.ToString());
                segment.Clear();
                closed = false;
            }
            else if (closed || (c == '"' && segment.Length > 0))
            {
                throw new FormatException($"'{keyPath}' is not a valid key path.");
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else
            {
                segment.Append(c);
            }
        }

        if (quoted)
            throw new FormatException($"'{keyPath}' is not a valid key path.");
        segments.Add(segment.ToString());
        return segments.ToArray();
    }

    public static bool TryGet(JsonObject root, IReadOnlyList<string> path, out JsonNode? value)
    {
        value = null;
        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || AtomicConfigDocument.Key(obj, segment) is not { } key)
                return false;
            current = obj[key];
        }

        value = current;
        return true;
    }

    public static void Set(JsonObject root, IReadOnlyList<string> path, JsonNode? value)
    {
        var parent = GetOrCreateParent(root, path);
        parent[AtomicConfigDocument.Key(parent, path[^1]) ?? path[^1]] = value;
    }

    public static void Upsert(JsonObject root, IReadOnlyList<string> path, JsonNode value)
    {
        var parent = GetOrCreateParent(root, path);
        var key = AtomicConfigDocument.Key(parent, path[^1]) ?? path[^1];
        if (parent[key] is JsonObject existing && value is JsonObject patch)
            DeepMerge(existing, patch);
        else
            parent[key] = value;
    }

    public static void Remove(JsonObject root, IReadOnlyList<string> path)
    {
        var chain = new List<JsonObject> { root };
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (AtomicConfigDocument.Key(chain[^1], path[i]) is not { } key || chain[^1][key] is not JsonObject next)
                return;
            chain.Add(next);
        }

        if (AtomicConfigDocument.Key(chain[^1], path[^1]) is not { } leaf)
            return;
        chain[^1].Remove(leaf);
        for (var i = chain.Count - 1; i > 0 && chain[i].Count == 0; i--)
            chain[i - 1].Remove(AtomicConfigDocument.Key(chain[i - 1], path[i - 1])!);
    }

    public static string Version(JsonObject root)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteSorted(writer, root);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void DeepMerge(JsonObject target, JsonObject patch)
    {
        foreach (var (name, value) in patch.ToArray())
        {
            var key = AtomicConfigDocument.Key(target, name);
            if (value is null)
            {
                if (key != null)
                    target.Remove(key);
            }
            else if (key != null && target[key] is JsonObject existing && value is JsonObject nested)
            {
                DeepMerge(existing, nested);
            }
            else
            {
                target[key ?? name] = value.DeepClone();
            }
        }
    }

    private static JsonObject GetOrCreateParent(JsonObject root, IReadOnlyList<string> path)
    {
        var current = root;
        for (var i = 0; i < path.Count - 1; i++)
            current = AtomicConfigDocument.Object(current, path[i]);
        return current;
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteSorted(writer, value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                    WriteSorted(writer, item);
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
