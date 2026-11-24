using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

public sealed record TokenUsageRequestTiming(double FirstOutputLatencyMs, double GenerationMs)
{
    private const string Key = "dotcraft.llmRequestTiming";

    /// <summary>Attaches provider timing, or suppresses host timing when the provider passes null.</summary>
    public static void Attach(UsageContent usage, TokenUsageRequestTiming? timing)
    {
        usage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        usage.AdditionalProperties[Key] = timing;
    }

    public static bool IsAttached(UsageContent usage) => usage.AdditionalProperties?.ContainsKey(Key) == true;

    public static TokenUsageRequestTiming? TryGet(UsageContent usage) =>
        usage.AdditionalProperties?.TryGetValue(Key, out var value) == true
            ? value as TokenUsageRequestTiming
            : null;
}
