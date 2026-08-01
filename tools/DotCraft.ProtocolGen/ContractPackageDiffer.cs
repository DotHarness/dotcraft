using System.Text.Json.Nodes;

namespace DotCraft.ProtocolGen;

public enum ContractChangeClassification
{
    Breaking,
    Additive,
    MetadataOnly
}

public sealed record ContractChange(ContractChangeClassification Classification, string Path, string Message);

public static class ContractPackageDiffer
{
    public static IReadOnlyList<ContractChange> Compare(string previousManifest, string currentManifest)
    {
        var previous = JsonNode.Parse(previousManifest)?.AsObject() ?? throw new ProtocolGenerationException("DPG020", "Previous manifest is invalid.");
        var current = JsonNode.Parse(currentManifest)?.AsObject() ?? throw new ProtocolGenerationException("DPG020", "Current manifest is invalid.");
        var changes = new List<ContractChange>();

        CompareMethods(previous, current, changes);
        CompareTypes(previous, current, changes);
        return changes.OrderBy(static change => change.Path, StringComparer.Ordinal)
            .ThenBy(static change => change.Classification)
            .ToArray();
    }

    private static void CompareMethods(JsonObject previous, JsonObject current, ICollection<ContractChange> changes)
    {
        var before = Index(previous["methods"]!.AsArray(), "name");
        var after = Index(current["methods"]!.AsArray(), "name");
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var currentMethod))
            {
                changes.Add(new(ContractChangeClassification.Breaking, $"methods/{pair.Key}", "Method was removed."));
                continue;
            }

            var previousMethod = pair.Value;
            foreach (var shapeField in new[] { "kind", "direction", "paramsType", "resultType" })
            {
                if (!JsonNode.DeepEquals(previousMethod[shapeField], currentMethod[shapeField]))
                    changes.Add(new(ContractChangeClassification.Breaking, $"methods/{pair.Key}/{shapeField}", $"Method {shapeField} changed."));
            }
            foreach (var metadataField in new[] { "module", "capability", "scope", "notificationOptOut", "errors", "since", "specRef", "stability" })
            {
                if (!JsonNode.DeepEquals(previousMethod[metadataField], currentMethod[metadataField]))
                    changes.Add(new(ContractChangeClassification.MetadataOnly, $"methods/{pair.Key}/{metadataField}", $"Method {metadataField} changed."));
            }
        }
        foreach (var method in after.Keys.Except(before.Keys, StringComparer.Ordinal))
            changes.Add(new(ContractChangeClassification.Additive, $"methods/{method}", "Method was added."));
    }

    private static void CompareTypes(JsonObject previous, JsonObject current, ICollection<ContractChange> changes)
    {
        var before = Index(previous["types"]!.AsArray(), "id");
        var after = Index(current["types"]!.AsArray(), "id");
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var currentType))
            {
                changes.Add(new(ContractChangeClassification.Breaking, $"types/{pair.Key}", "Type was removed."));
                continue;
            }

            var previousType = pair.Value;
            if (!JsonNode.DeepEquals(previousType["kind"], currentType["kind"]))
                changes.Add(new(ContractChangeClassification.Breaking, $"types/{pair.Key}/kind", "Type kind changed."));

            var previousFields = Index(previousType["fields"]!.AsArray(), "name");
            var currentFields = Index(currentType["fields"]!.AsArray(), "name");
            foreach (var field in previousFields)
            {
                var path = $"types/{pair.Key}/fields/{field.Key}";
                if (!currentFields.TryGetValue(field.Key, out var currentField))
                {
                    changes.Add(new(ContractChangeClassification.Breaking, path, "Field was removed."));
                    continue;
                }
                if (!JsonNode.DeepEquals(field.Value["type"], currentField["type"]) ||
                    !JsonNode.DeepEquals(field.Value["const"], currentField["const"]))
                    changes.Add(new(ContractChangeClassification.Breaking, path, "Field type or discriminator changed."));
                foreach (var bound in new[] { "minimum", "maximum" })
                {
                    if (BoundTightened(field.Value, currentField, bound))
                        changes.Add(new(ContractChangeClassification.Breaking, $"{path}/{bound}", $"Field {bound} was tightened."));
                    else if (BoundRelaxed(field.Value, currentField, bound))
                        changes.Add(new(ContractChangeClassification.Additive, $"{path}/{bound}", $"Field {bound} was relaxed."));
                }
                if (field.Value["required"]!.GetValue<bool>() == false && currentField["required"]!.GetValue<bool>())
                    changes.Add(new(ContractChangeClassification.Breaking, path, "Optional field became required."));
                if (field.Value["nullable"]!.GetValue<bool>() && !currentField["nullable"]!.GetValue<bool>())
                    changes.Add(new(ContractChangeClassification.Breaking, path, "Nullable field became non-nullable."));
            }
            foreach (var field in currentFields.Keys.Except(previousFields.Keys, StringComparer.Ordinal))
            {
                var added = currentFields[field];
                changes.Add(new(
                    added["required"]!.GetValue<bool>() ? ContractChangeClassification.Breaking : ContractChangeClassification.Additive,
                    $"types/{pair.Key}/fields/{field}",
                    added["required"]!.GetValue<bool>() ? "Required field was added." : "Optional field was added."));
            }

            if (!JsonNode.DeepEquals(previousType["variants"], currentType["variants"]))
                changes.Add(new(ContractChangeClassification.Breaking, $"types/{pair.Key}/variants", "Union variants changed."));
        }
        foreach (var type in after.Keys.Except(before.Keys, StringComparer.Ordinal))
            changes.Add(new(ContractChangeClassification.Additive, $"types/{type}", "Type was added."));
    }

    private static bool BoundTightened(JsonObject previous, JsonObject current, string name)
    {
        var before = previous[name]?.GetValue<long>();
        var after = current[name]?.GetValue<long>();
        if (!after.HasValue)
            return false;
        if (!before.HasValue)
            return true;
        return name == "minimum" ? after.Value > before.Value : after.Value < before.Value;
    }

    private static bool BoundRelaxed(JsonObject previous, JsonObject current, string name)
    {
        var before = previous[name]?.GetValue<long>();
        var after = current[name]?.GetValue<long>();
        if (!before.HasValue)
            return false;
        if (!after.HasValue)
            return true;
        return name == "minimum" ? after.Value < before.Value : after.Value > before.Value;
    }

    private static Dictionary<string, JsonObject> Index(JsonArray values, string key) =>
        values.Select(static node => node!.AsObject())
            .ToDictionary(value => value[key]!.GetValue<string>(), StringComparer.Ordinal);
}
