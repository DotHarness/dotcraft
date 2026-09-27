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
    Ultra,
    Max
}

/// <summary>Maps DotCraft model reasoning efforts to provider-native efforts.</summary>
public static class ModelReasoningEffortExtensions
{
    /// <summary>Returns the provider effort represented by this product effort.</summary>
    public static ProviderReasoningEffort ToProviderEffort(this ModelReasoningEffort effort) => effort switch
    {
        ModelReasoningEffort.None => ProviderReasoningEffort.None,
        ModelReasoningEffort.Low => ProviderReasoningEffort.Low,
        ModelReasoningEffort.Medium => ProviderReasoningEffort.Medium,
        ModelReasoningEffort.High => ProviderReasoningEffort.High,
        ModelReasoningEffort.ExtraHigh => ProviderReasoningEffort.ExtraHigh,
        ModelReasoningEffort.Max or ModelReasoningEffort.Ultra => ProviderReasoningEffort.Max,
        _ => ProviderReasoningEffort.Medium
    };

    /// <summary>Converts a provider effort into the equivalent ordinary DotCraft model effort.</summary>
    public static ModelReasoningEffort ToModelReasoningEffort(this ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.None => ModelReasoningEffort.None,
        ProviderReasoningEffort.Low => ModelReasoningEffort.Low,
        ProviderReasoningEffort.Medium => ModelReasoningEffort.Medium,
        ProviderReasoningEffort.High => ModelReasoningEffort.High,
        ProviderReasoningEffort.ExtraHigh => ModelReasoningEffort.ExtraHigh,
        ProviderReasoningEffort.Max => ModelReasoningEffort.Max,
        _ => ModelReasoningEffort.Medium
    };
}
