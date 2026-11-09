using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace DotCraft.Mcp;

internal static class McpSettingsExtension
{
    public const string CapabilityName = "dotcraft/settings";

    public static (string ReadTool, string UpdateTool)? Read(ServerCapabilities? capabilities)
    {
        if (capabilities is null)
            return null;
        var declaration = Find(capabilities.Extensions) ?? Find(capabilities.Experimental);
        if (declaration is not { ValueKind: JsonValueKind.Object } value)
            return null;
        var readTool = ReadName(value, "readTool");
        var updateTool = ReadName(value, "updateTool");
        if (readTool is null || updateTool is null || string.Equals(readTool, updateTool, StringComparison.Ordinal))
            return null;
        return (readTool, updateTool);
    }

    private static JsonElement? Find<TValue>(IDictionary<string, TValue>? values) =>
        values is not null && values.TryGetValue(CapabilityName, out var value) && value is not null
            ? JsonSerializer.SerializeToElement<object>(value)
            : null;

    private static string? ReadName(JsonElement value, string property) =>
        value.TryGetProperty(property, out var name)
        && name.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()
            : null;
}
