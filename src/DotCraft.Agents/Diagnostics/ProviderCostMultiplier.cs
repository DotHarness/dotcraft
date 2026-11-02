using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

internal static class ProviderCostMultiplier
{
    private const string Key = "dotcraft.costMultiplier";

    public static void Attach(ChatResponseUpdate update, decimal multiplier) =>
        (update.AdditionalProperties ??= new AdditionalPropertiesDictionary())[Key] = multiplier;

    public static decimal? TryGet(AdditionalPropertiesDictionary? properties) =>
        properties?.TryGetValue(Key, out var value) == true && value is decimal multiplier ? multiplier : null;
}
