using System.Text.Json.Nodes;

namespace DotCraft.AppBinding;

/// <summary>Conservatively detects capability expansion; ambiguity always requires confirmation.</summary>
internal static class AppBindingCapabilityDiffer
{
    public static List<AppBindingCapabilityChange> FindExpansions(
        IReadOnlyList<AppBindingToolCapability> approved,
        IReadOnlyList<AppBindingToolCapability> candidate)
    {
        var oldById = approved.ToDictionary(Key, StringComparer.Ordinal);
        var changes = new List<AppBindingCapabilityChange>();
        foreach (var next in candidate)
        {
            var id = Key(next);
            if (!oldById.TryGetValue(id, out var previous))
            {
                changes.Add(Change("toolAdded", id, "A new tool becomes available."));
                continue;
            }
            if (!IsProvableSubset(next.InputSchema, previous.InputSchema))
                changes.Add(Change("inputExpanded", id, "The accepted input domain may be broader."));
            if (next.Visibility.Except(previous.Visibility, StringComparer.Ordinal).Any())
                changes.Add(Change("visibilityExpanded", id, "Tool visibility expands."));
            if (RiskExpanded(previous.Annotations, next.Annotations))
                changes.Add(Change("riskRelaxed", id, "Approval or risk constraints are relaxed."));
            if (UiExpanded(previous.Ui, next.Ui))
                changes.Add(Change("presentationExpanded", id, "UI association or browser authority expands."));
        }
        return changes;
    }

    private static bool IsProvableSubset(JsonObject next, JsonObject previous)
    {
        if (JsonNode.DeepEquals(next, previous)) return true;
        if (next["$ref"] != null || previous["$ref"] != null || next["anyOf"] != null
            || previous["anyOf"] != null || next["oneOf"] != null || previous["oneOf"] != null
            || next["not"] != null || previous["not"] != null) return false;
        var oldRequired = StringSet(previous["required"]);
        var newRequired = StringSet(next["required"]);
        if (!newRequired.IsSupersetOf(oldRequired)) return false;
        if (!TryAdditionalProperties(previous["additionalProperties"], out var oldAdditional)
            || !TryAdditionalProperties(next["additionalProperties"], out var newAdditional))
            return false;
        if (!oldAdditional && newAdditional) return false;
        if (!UnmodeledConstraintsEqual(next, previous,
                "properties", "required", "additionalProperties", "title", "description"))
            return false;
        var oldProps = previous["properties"] as JsonObject;
        var newProps = next["properties"] as JsonObject;
        if (newProps != null)
        {
            foreach (var property in newProps)
            {
                if (oldProps?[property.Key] is not JsonObject oldSchema)
                {
                    // Every newly declared optional property expands the accepted input surface.
                    return false;
                }
                if (property.Value is not JsonObject newSchema || !ScalarSubset(newSchema, oldSchema)) return false;
            }
        }
        return true;
    }

    private static bool ScalarSubset(JsonObject next, JsonObject previous)
    {
        if (JsonNode.DeepEquals(next, previous)) return true;
        var oldTypes = Values(previous["type"]); var newTypes = Values(next["type"]);
        if (oldTypes.Count > 0 && (newTypes.Count == 0 || !newTypes.IsSubsetOf(oldTypes))) return false;
        var oldEnum = Values(previous["enum"]); var newEnum = Values(next["enum"]);
        if (oldEnum.Count > 0 && (newEnum.Count == 0 || !newEnum.IsSubsetOf(oldEnum))) return false;
        if (!BoundNarrows(next, previous, "minimum", lower: true)
            || !BoundNarrows(next, previous, "maximum", lower: false)) return false;
        return UnmodeledConstraintsEqual(next, previous,
            "type", "enum", "minimum", "maximum", "title", "description");
    }

    private static bool UnmodeledConstraintsEqual(
        JsonObject next,
        JsonObject previous,
        params string[] modeledKeys)
    {
        var modeled = modeledKeys.ToHashSet(StringComparer.Ordinal);
        var keys = next.Select(pair => pair.Key)
            .Concat(previous.Select(pair => pair.Key))
            .Where(key => !modeled.Contains(key))
            .Distinct(StringComparer.Ordinal);
        return keys.All(key => JsonNode.DeepEquals(next[key], previous[key]));
    }

    private static bool BoundNarrows(JsonObject next, JsonObject previous, string key, bool lower)
    {
        if (!TryNumber(previous[key], out var oldValue) || !TryNumber(next[key], out var newValue))
            return false;
        if (!oldValue.HasValue) return true;
        return newValue.HasValue && (lower ? newValue >= oldValue : newValue <= oldValue);
    }

    private static bool RiskExpanded(JsonObject previous, JsonObject next)
    {
        static bool Flag(JsonObject value, string key, bool fallback) =>
            value[key] is JsonValue node && node.TryGetValue<bool>(out var result) ? result : fallback;
        return Flag(previous, "requiresApproval", true) && !Flag(next, "requiresApproval", true)
               || !Flag(previous, "destructive", false) && Flag(next, "destructive", false)
               || !Flag(previous, "openWorld", false) && Flag(next, "openWorld", false);
    }

    private static bool UiExpanded(AppBindingUiCapability? previous, AppBindingUiCapability? next) =>
        next != null && (previous == null
            || !string.Equals(previous.ResourceUri, next.ResourceUri, StringComparison.Ordinal)
            || next.ConnectDomains.Except(previous.ConnectDomains, StringComparer.OrdinalIgnoreCase).Any()
            || next.ResourceDomains.Except(previous.ResourceDomains, StringComparer.OrdinalIgnoreCase).Any()
            || next.Permissions.Except(previous.Permissions, StringComparer.Ordinal).Any());

    private static HashSet<string> StringSet(JsonNode? node) => Values(node);

    private static bool TryAdditionalProperties(JsonNode? node, out bool value)
    {
        if (node == null) { value = true; return true; }
        if (node is JsonValue json && json.TryGetValue<bool>(out value)) return true;
        value = false;
        return false;
    }

    private static bool TryNumber(JsonNode? node, out double? value)
    {
        if (node == null) { value = null; return true; }
        if (node is JsonValue json && json.TryGetValue<double>(out var number))
        {
            value = number;
            return true;
        }
        value = null;
        return false;
    }
    private static HashSet<string> Values(JsonNode? node)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (node is JsonArray array)
            foreach (var item in array) if (item != null) set.Add(item.ToJsonString());
        else if (node != null) set.Add(node.ToJsonString());
        return set;
    }
    private static string Key(AppBindingToolCapability tool) => $"{tool.Namespace}.{tool.Name}";
    private static AppBindingCapabilityChange Change(string kind, string tool, string detail) =>
        new() { Kind = kind, Tool = tool, Detail = detail };
}
