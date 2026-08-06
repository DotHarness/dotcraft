using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public static class ProviderFunctionCallMetadata
{
    public const string NamespaceKey = "dotcraft.function_namespace";

    public static bool TryGetNamespace(FunctionCallContent call, out string toolNamespace)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (TryRead(call.AdditionalProperties, NamespaceKey, out toolNamespace)
            || TryRead(call.AdditionalProperties, "namespace", out toolNamespace))
            return true;
        if (call.RawRepresentation is JsonElement { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty("namespace", out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            toolNamespace = value.GetString()!.Trim();
            return true;
        }
        toolNamespace = string.Empty;
        return false;
    }

    private static bool TryRead(
        AdditionalPropertiesDictionary? properties,
        string key,
        out string value)
    {
        value = string.Empty;
        if (properties == null || !properties.TryGetValue(key, out var raw))
            return false;
        var text = raw switch
        {
            string item => item,
            JsonElement { ValueKind: JsonValueKind.String } item => item.GetString(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text.Trim();
        return true;
    }
}
