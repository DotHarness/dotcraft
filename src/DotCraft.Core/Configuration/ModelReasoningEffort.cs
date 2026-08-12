using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

/// <summary>
/// DotCraft-owned model reasoning effort persisted in workspace and thread configuration.
/// </summary>
public enum ModelReasoningEffort
{
    None,
    Low,
    Medium,
    High,
    ExtraHigh,
    Ultra
}

/// <summary>Maps DotCraft model reasoning efforts to provider-facing MEAI values.</summary>
public static class ModelReasoningEffortExtensions
{
    /// <summary>Returns the provider effort represented by this product effort.</summary>
    public static ReasoningEffort ToProviderEffort(this ModelReasoningEffort effort) => effort switch
    {
        ModelReasoningEffort.None => ReasoningEffort.None,
        ModelReasoningEffort.Low => ReasoningEffort.Low,
        ModelReasoningEffort.Medium => ReasoningEffort.Medium,
        ModelReasoningEffort.High => ReasoningEffort.High,
        ModelReasoningEffort.ExtraHigh or ModelReasoningEffort.Ultra => ReasoningEffort.ExtraHigh,
        _ => ReasoningEffort.Medium
    };

    /// <summary>Converts a provider effort into the equivalent ordinary DotCraft model effort.</summary>
    public static ModelReasoningEffort ToModelReasoningEffort(this ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.None => ModelReasoningEffort.None,
        ReasoningEffort.Low => ModelReasoningEffort.Low,
        ReasoningEffort.Medium => ModelReasoningEffort.Medium,
        ReasoningEffort.High => ModelReasoningEffort.High,
        ReasoningEffort.ExtraHigh => ModelReasoningEffort.ExtraHigh,
        _ => ModelReasoningEffort.Medium
    };
}
