using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

public enum ProviderReasoningEffort
{
    None,
    Low,
    Medium,
    High,
    ExtraHigh,
    Max
}

public static class ProviderReasoningOptions
{
    private const string EffortKey = "dotcraft.reasoningEffort";

    public static ReasoningEffort? ToMeaiEffort(this ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.None => ReasoningEffort.None,
        ProviderReasoningEffort.Low => ReasoningEffort.Low,
        ProviderReasoningEffort.Medium => ReasoningEffort.Medium,
        ProviderReasoningEffort.High => ReasoningEffort.High,
        ProviderReasoningEffort.ExtraHigh => ReasoningEffort.ExtraHigh,
        ProviderReasoningEffort.Max => null,
        _ => throw new ArgumentOutOfRangeException(nameof(effort))
    };

    public static string ToToken(this ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.ExtraHigh => "xhigh",
        _ => effort.ToString().ToLowerInvariant()
    };

    public static void Apply(ChatOptions options, bool enabled, ProviderReasoningEffort effort, ReasoningOutput output)
    {
        options.AdditionalProperties?.Remove(EffortKey);
        options.Reasoning = enabled ? new ReasoningOptions { Effort = effort.ToMeaiEffort(), Output = output } : null;
        if (enabled && effort == ProviderReasoningEffort.Max)
        {
            options.AdditionalProperties ??= new();
            options.AdditionalProperties[EffortKey] = "max";
        }
    }

    public static ChatOptions? ApplyDefaults(ChatOptions? options, ProviderPipelineOptions? pipeline)
    {
        if (options?.Reasoning != null || pipeline is not { ReasoningEnabled: true })
            return options;
        var prepared = options?.Clone() ?? new ChatOptions();
        if (prepared.AdditionalProperties != null)
            prepared.AdditionalProperties = new(prepared.AdditionalProperties);
        if (!Enum.TryParse<ProviderReasoningEffort>(pipeline.ReasoningEffort, true, out var effort))
            effort = ProviderReasoningEffort.Medium;
        if (!Enum.TryParse<ReasoningOutput>(pipeline.ReasoningOutput, true, out var output))
            output = ReasoningOutput.Full;
        Apply(prepared, true, effort, output);
        return prepared;
    }

    public static ProviderReasoningEffort? Resolve(ChatOptions? options)
    {
        // An explicit MEAI effort overrides inherited extension metadata, including None.
        if (options?.Reasoning?.Effort is { } effort)
            return Enum.Parse<ProviderReasoningEffort>(effort.ToString());
        if (options?.Reasoning != null
            && options.AdditionalProperties?.TryGetValue(EffortKey, out var value) == true
            && string.Equals(value?.ToString(), "max", StringComparison.OrdinalIgnoreCase))
            return ProviderReasoningEffort.Max;
        return null;
    }

    public static ChatOptions WithoutMetadata(ChatOptions options)
    {
        var copy = options.Clone();
        if (options.AdditionalProperties != null)
        {
            copy.AdditionalProperties = new(options.AdditionalProperties);
            copy.AdditionalProperties.Remove(EffortKey);
        }
        return copy;
    }
}
