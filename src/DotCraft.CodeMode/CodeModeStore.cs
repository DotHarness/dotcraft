using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace DotCraft.CodeMode;

public sealed class CodeModeStore
{
    private readonly ConcurrentDictionary<string, Dictionary<string, JsonNode?>> _threads = new(StringComparer.Ordinal);

    public JsonObject Snapshot(string threadId)
    {
        var snapshot = new JsonObject();
        if (!_threads.TryGetValue(threadId, out var values))
            return snapshot;
        lock (values)
        {
            foreach (var (key, value) in values)
                snapshot[key] = value?.DeepClone();
        }
        return snapshot;
    }

    public const int MaxBytes = 1024 * 1024;

    public bool TryApply(string threadId, JsonObject set, IEnumerable<string> deleted)
    {
        var values = _threads.GetOrAdd(threadId, static _ => new Dictionary<string, JsonNode?>(StringComparer.Ordinal));
        lock (values)
        {
            var merged = new Dictionary<string, JsonNode?>(values, StringComparer.Ordinal);
            foreach (var key in deleted)
                merged.Remove(key);
            foreach (var (key, value) in set)
                merged[key] = value;
            if (merged.Sum(static pair => Size(pair.Key, pair.Value)) > MaxBytes)
                return false;
            foreach (var key in deleted)
                values.Remove(key);
            foreach (var (key, value) in set)
                values[key] = value?.DeepClone();
            return true;
        }
    }

    public static long Size(string key, string json) =>
        Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(json);

    private static long Size(string key, JsonNode? value) => Size(key, value?.ToJsonString() ?? "null");

    public void Clear(string threadId) => _threads.TryRemove(threadId, out _);
}
