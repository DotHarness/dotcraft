namespace DotCraft.Tools;

/// <summary>Describes capabilities executed directly by the selected model provider.</summary>
public sealed record ProviderHostedCapabilityPlan(
    bool ImageGenerationEnabled = false,
    int MaxReferenceImages = 0);
