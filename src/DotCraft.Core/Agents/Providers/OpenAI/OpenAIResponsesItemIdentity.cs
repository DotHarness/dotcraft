using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesItemIdentityDiagnostics
{
    public int EligibleCount { get; private set; }

    public int PresentCount { get; private set; }

    public int GeneratedCount { get; private set; }

    public int MissingCount => EligibleCount - PresentCount;

    public int InvalidSourceCount { get; private set; }

    internal void Record(bool generated, bool invalidSource)
    {
        EligibleCount++;
        PresentCount++;
        if (generated)
            GeneratedCount++;
        if (invalidSource)
            InvalidSourceCount++;
    }

    internal static OpenAIResponsesItemIdentityDiagnostics FromInput(JsonArray input)
    {
        var diagnostics = new OpenAIResponsesItemIdentityDiagnostics();
        foreach (var item in input.OfType<JsonObject>())
        {
            if (!IsIdentityEligible(item))
                continue;

            var id = item["id"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
            diagnostics.EligibleCount++;
            if (OpenAIResponsesItemIdentity.IsValid(id))
                diagnostics.PresentCount++;
            else if (!string.IsNullOrWhiteSpace(id))
                diagnostics.InvalidSourceCount++;
        }

        return diagnostics;
    }

    private static bool IsIdentityEligible(JsonObject item)
    {
        var type = item["type"] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
        return type is "message"
            or "reasoning"
            or "function_call"
            or "function_call_output"
            or "tool_search_call"
            or "tool_search_output"
            or "image_generation_call";
    }
}

internal static class OpenAIResponsesItemIdentity
{
    internal const string MetadataKey = "openai.responses.item_id";

    public static void PreserveProviderItemId(AIContent content, string providerItemId)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!IsValid(providerItemId))
            return;

        var updatedProperties = content.AdditionalProperties == null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(content.AdditionalProperties);
        updatedProperties[MetadataKey] = providerItemId.Trim();
        content.AdditionalProperties = updatedProperties;
    }

    public static string Assign(
        ChatMessage message,
        JsonObject item,
        string prefix,
        int itemOrdinal,
        int messageItemOrdinal,
        OpenAIResponsesItemIdentityDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(message);
        var metadataKey = messageItemOrdinal == 0
            ? MetadataKey
            : $"{MetadataKey}.{messageItemOrdinal}";
        return Assign(
            message.AdditionalProperties,
            properties => message.AdditionalProperties = properties,
            metadataKey,
            messageItemOrdinal == 0 ? message.MessageId : null,
            item,
            prefix,
            itemOrdinal,
            diagnostics);
    }

    public static string Assign(
        AIContent content,
        JsonObject item,
        string prefix,
        int itemOrdinal,
        OpenAIResponsesItemIdentityDiagnostics diagnostics,
        string? providerItemId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Assign(
            content.AdditionalProperties,
            properties => content.AdditionalProperties = properties,
            MetadataKey,
            providerItemId,
            item,
            prefix,
            itemOrdinal,
            diagnostics);
    }

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var separator = id.IndexOf('_');
        return separator > 0 && separator < id.Length - 1;
    }

    private static string Assign(
        AdditionalPropertiesDictionary? properties,
        Action<AdditionalPropertiesDictionary> setProperties,
        string metadataKey,
        string? preferredItemId,
        JsonObject item,
        string prefix,
        int itemOrdinal,
        OpenAIResponsesItemIdentityDiagnostics diagnostics)
    {
        var invalidSource = false;
        var existing = ReadString(properties, metadataKey);
        var legacy = ReadString(properties, "id");
        var candidate = FirstValid(preferredItemId, existing, legacy);
        invalidSource =
            IsPresentButInvalid(preferredItemId) ||
            IsPresentButInvalid(existing) ||
            IsPresentButInvalid(legacy);

        var generated = candidate == null;
        candidate ??= Generate(prefix, itemOrdinal, item);

        var updatedProperties = properties == null
            ? new AdditionalPropertiesDictionary()
            : new AdditionalPropertiesDictionary(properties);
        updatedProperties[metadataKey] = candidate;
        setProperties(updatedProperties);
        item["id"] = candidate;
        diagnostics.Record(generated, invalidSource);
        return candidate;
    }

    private static string Generate(string prefix, int itemOrdinal, JsonObject item)
    {
        var canonical = item.ToJsonString();
        var payload = Encoding.UTF8.GetBytes($"{prefix}\n{itemOrdinal}\n{canonical}");
        var suffix = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..32];
        return $"{prefix}_{suffix}";
    }

    private static string? FirstValid(params string?[] candidates) =>
        candidates.FirstOrDefault(IsValid)?.Trim();

    private static bool IsPresentButInvalid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !IsValid(value);

    private static string? ReadString(AdditionalPropertiesDictionary? properties, string name)
    {
        if (properties == null || !properties.TryGetValue(name, out var value))
            return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) => text,
            _ => null
        };
    }
}
