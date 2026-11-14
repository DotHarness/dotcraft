using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed class WorkspaceRoutes
{
    private static readonly ConcurrentDictionary<string, WorkspaceRoutes> Stores = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _path;
    private readonly Dictionary<string, string> _external;
    private readonly Dictionary<string, string> _source;
    private readonly object _gate = new();

    private WorkspaceRoutes(string path)
    {
        _path = path;
        _external = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!
            : new(StringComparer.Ordinal);
        _source = _external.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    }

    public static WorkspaceRoutes Open(string stateDirectory, string workspace)
    {
        var key = OperatingSystem.IsWindows() ? workspace.ToUpperInvariant() : workspace;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var path = Path.Combine(Path.GetFullPath(stateDirectory), "workspaces", hash + ".json");
        return Stores.GetOrAdd(path, p => new WorkspaceRoutes(p));
    }

    public string External(string kind, string thread, string? turn = null, string? item = null)
    {
        var key = JsonSerializer.Serialize(new[] { kind, thread, turn, item });
        lock (_gate)
        {
            if (_external.TryGetValue(key, out var id)) return id;
            id = Guid.CreateVersion7().ToString();
            _external.Add(key, id);
            _source.Add(id, key);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_external));
            File.Move(temporary, _path, true);
            return id;
        }
    }

    public bool Contains(string id, string kind)
    {
        lock (_gate)
            return _source.TryGetValue(id, out var key) && JsonSerializer.Deserialize<string?[]>(key)![0] == kind;
    }

    public string Resolve(string id, string kind, string? thread = null, string? turn = null)
    {
        lock (_gate)
        {
            if (!_source.TryGetValue(id, out var key))
                throw new ArgumentException("Unknown gateway identifier.");
            var parts = JsonSerializer.Deserialize<string?[]>(key)!;
            if (parts[0] != kind || (thread is not null && parts[1] != thread) ||
                (turn is not null && parts[2] != turn))
                throw new ArgumentException("Identifier does not belong to the requested scope.");
            return parts[kind == "thread" ? 1 : kind == "turn" ? 2 : 3]!;
        }
    }
}

internal static class WorkspaceJson
{
    public static JsonElement Get(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    public static string? Text(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    public static string Required(this JsonElement element, string name) =>
        element.Text(name) ?? throw new ArgumentException($"'{name}' is required.");
    public static bool Flag(this JsonElement element, string name) => element.Get(name).ValueKind == JsonValueKind.True;
    public static IEnumerable<JsonElement> Rows(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
    public static object? Value(this JsonElement element) =>
        element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : element;
    public static long? Seconds(this JsonElement element, string name) =>
        element.Text(name) is { } value ? DateTimeOffset.Parse(value).ToUnixTimeSeconds() : null;
    public static long? Milliseconds(this JsonElement element, string name) =>
        element.Text(name) is { } value ? DateTimeOffset.Parse(value).ToUnixTimeMilliseconds() : null;
    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
    public static void Reject(this JsonElement p, params string[] names)
    {
        foreach (var name in names)
            if (p.Get(name).ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new ArgumentException($"'{name}' is not supported by this gateway.");
    }
    public static int Limit(this JsonElement p, int fallback = 20)
    {
        var limit = p.Get("limit").ValueKind == JsonValueKind.Number ? p.Get("limit").GetInt32() : fallback;
        return limit is > 0 and <= 100 ? limit : throw new ArgumentException("'limit' must be between 1 and 100.");
    }
}
