using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed record ProviderContextUsage(long OccupiedTokens, long? InputTokens = null)
{
    private const string Key = "dotcraft.providerContextUsage";

    /// <summary>Attaches measured occupancy or marks it unknown when usage is aggregate-only.</summary>
    public static void Attach(UsageContent usage, ProviderContextUsage? context)
    {
        usage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        usage.AdditionalProperties[Key] = context;
    }

    public static bool IsAttached(UsageContent usage) => usage.AdditionalProperties?.ContainsKey(Key) == true;

    public static ProviderContextUsage? TryGet(UsageContent usage) =>
        usage.AdditionalProperties?.TryGetValue(Key, out var value) == true
            ? value as ProviderContextUsage
            : null;
}
