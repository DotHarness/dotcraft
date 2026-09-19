using System.Text.Json.Nodes;

namespace DotCraft.Context.WorldState;

public sealed class WorldStateSnapshot
{
    private readonly SortedDictionary<string, JsonNode> _sections;

    private WorldStateSnapshot(SortedDictionary<string, JsonNode> sections) => _sections = sections;

    public static WorldStateSnapshot FromSections(IEnumerable<KeyValuePair<string, JsonNode>> sections)
    {
        var map = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (id, value) in sections)
            map[id] = value.DeepClone();
        return new WorldStateSnapshot(map);
    }

    public static WorldStateSnapshot FromJsonObject(JsonObject state)
    {
        var map = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (id, value) in state)
        {
            if (value != null)
                map[id] = value.DeepClone();
        }

        return new WorldStateSnapshot(map);
    }

    public bool TryGetSection(string id, out JsonNode value)
    {
        if (_sections.TryGetValue(id, out var stored))
        {
            value = stored;
            return true;
        }

        value = null!;
        return false;
    }

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject();
        foreach (var (id, value) in _sections)
            result[id] = value.DeepClone();
        return result;
    }

    /// <summary>The RFC 7386 merge patch advancing <paramref name="previous"/> here, or <c>null</c> when nothing moved.</summary>
    public JsonObject? MergePatchFrom(WorldStateSnapshot previous)
    {
        var patch = new JsonObject();
        foreach (var id in previous._sections.Keys)
        {
            if (!_sections.ContainsKey(id))
                patch[id] = null;
        }

        foreach (var (id, current) in _sections)
        {
            if (previous._sections.TryGetValue(id, out var stored))
            {
                if (TryCreateMergePatch(stored, current, out var value))
                    patch[id] = value;
            }
            else
                patch[id] = current.DeepClone();
        }

        return patch.Count == 0 ? null : patch;
    }

    public void ApplyMergePatch(JsonObject patch)
    {
        foreach (var (id, value) in patch)
        {
            if (value == null)
                _sections.Remove(id);
            else
            {
                _sections.TryGetValue(id, out var current);
                _sections[id] = ApplyMergePatchValue(current, value);
            }
        }
    }

    /// <summary>Strips null members so a snapshot compares equal to the value a merge patch restores.</summary>
    internal static JsonNode? NormalizeSnapshot(JsonNode? value)
    {
        if (value is not JsonObject values)
            return value;

        var normalized = new JsonObject();
        foreach (var (name, member) in values)
        {
            if (member == null)
                continue;
            normalized[name] = NormalizeSnapshot(member.DeepClone());
        }

        return normalized;
    }

    private static bool TryCreateMergePatch(JsonNode previous, JsonNode current, out JsonNode? patch)
    {
        if (JsonNode.DeepEquals(previous, current))
        {
            patch = null;
            return false;
        }

        if (current is not JsonObject currentValues)
        {
            patch = current.DeepClone();
            return true;
        }

        var previousValues = previous as JsonObject;
        var result = new JsonObject();
        if (previousValues != null)
        {
            foreach (var name in previousValues.Select(static member => member.Key))
            {
                if (!currentValues.ContainsKey(name))
                    result[name] = null;
            }
        }

        foreach (var (name, currentMember) in currentValues)
        {
            if (previousValues == null || !previousValues.TryGetPropertyValue(name, out var previousMember))
                result[name] = currentMember!.DeepClone();
            else if (TryCreateMergePatch(previousMember!, currentMember!, out var memberPatch))
                result[name] = memberPatch;
        }

        patch = result;
        return true;
    }

    private static JsonNode ApplyMergePatchValue(JsonNode? target, JsonNode patch)
    {
        if (patch is not JsonObject patchValues)
            return patch.DeepClone();

        var result = target is JsonObject existing ? existing.DeepClone().AsObject() : new JsonObject();
        foreach (var (name, member) in patchValues)
        {
            if (member == null)
                result.Remove(name);
            else
            {
                result.TryGetPropertyValue(name, out var current);
                result[name] = ApplyMergePatchValue(current?.DeepClone(), member);
            }
        }

        return result;
    }
}
