using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

internal static class TokenUsageRequestMetadata
{
    public const string RequestIndexKey = "dotcraft.llmRequestIndex";

    public static void MarkRequestStart(ChatResponseUpdate update, int requestIndex)
    {
        update.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        update.AdditionalProperties[RequestIndexKey] = requestIndex;
    }

    public static int? TryGetRequestIndex(ChatResponseUpdate update)
    {
        if (update.AdditionalProperties == null)
            return null;

        if (!update.AdditionalProperties.TryGetValue(RequestIndexKey, out var value))
            return null;

        return TryConvertInt(value);
    }

    private static int? TryConvertInt(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int i:
                return i;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                return (int)l;
            case string s when int.TryParse(s, out var parsed):
                return parsed;
            default:
                try
                {
                    return Convert.ToInt32(value);
                }
                catch
                {
                    return null;
                }
        }
    }
}
