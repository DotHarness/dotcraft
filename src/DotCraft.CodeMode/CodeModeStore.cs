using System.Collections.Concurrent;
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

    public void Apply(string threadId, JsonObject set, IEnumerable<string> deleted)
    {
        var values = _threads.GetOrAdd(threadId, static _ => new Dictionary<string, JsonNode?>(StringComparer.Ordinal));
        lock (values)
        {
            foreach (var key in deleted)
                values.Remove(key);
            foreach (var (key, value) in set)
                values[key] = value?.DeepClone();
        }
    }

    public void Clear(string threadId) => _threads.TryRemove(threadId, out _);
}
