using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

internal sealed record TokenUsageRequestTiming(double FirstOutputLatencyMs, double GenerationMs)
{
    private const string Key = "dotcraft.llmRequestTiming";

    public static void Attach(UsageContent usage, TokenUsageRequestTiming timing)
    {
        usage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        usage.AdditionalProperties[Key] = timing;
    }

    public static TokenUsageRequestTiming? TryGet(UsageContent usage) =>
        usage.AdditionalProperties?.TryGetValue(Key, out var value) == true
            ? value as TokenUsageRequestTiming
            : null;
}
