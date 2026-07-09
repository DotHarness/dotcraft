using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Plugins;

internal enum ApprovalTargetArgumentState
{
    Present,
    MissingOptional,
    MissingRequired
}

internal static class ApprovalArgumentResolver
{
    public static ApprovalTargetArgumentState ResolveTargetArgument(
        JsonObject argsObject,
        JsonObject? inputSchema,
        string argumentName,
        out string value)
    {
        if (TryReadStringArgument(argsObject, argumentName, out value))
            return ApprovalTargetArgumentState.Present;

        return IsDeclaredOptionalArgument(inputSchema, argumentName)
            ? ApprovalTargetArgumentState.MissingOptional
            : ApprovalTargetArgumentState.MissingRequired;
    }

    public static bool TryReadStringArgument(JsonObject argsObject, string argumentName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(argumentName)
            || !argsObject.TryGetPropertyValue(argumentName, out var node)
            || node == null
            || node.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        value = node.GetValue<string>() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsDeclaredOptionalArgument(JsonObject? inputSchema, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(argumentName)
            || inputSchema == null
            || !string.Equals(inputSchema["type"]?.GetValue<string>(), "object", StringComparison.Ordinal)
            || inputSchema["properties"] is not JsonObject properties
            || !properties.ContainsKey(argumentName))
        {
            return false;
        }

        if (inputSchema["required"] is JsonArray required)
        {
            foreach (var node in required)
            {
                if (node?.GetValueKind() == JsonValueKind.String
                    && string.Equals(node.GetValue<string>(), argumentName, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
