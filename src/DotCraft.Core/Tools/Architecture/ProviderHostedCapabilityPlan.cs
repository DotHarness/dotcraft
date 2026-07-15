namespace DotCraft.Tools;

/// <summary>Describes capabilities selected for the active provider during tool planning.</summary>
public sealed record ProviderHostedCapabilityPlan(
    bool ImageGenerationEnabled = false,
    int MaxReferenceImages = 0)
{
    internal DeferredToolSearchPlan? DeferredToolSearch { get; init; }
}

internal sealed record DeferredToolSearchPlan(
    DeferredToolLoadingMode Mode,
    string Strategy,
    string ProviderProtocol,
    int MaxSearchResults,
    DotCraft.Tracing.TraceCollector? TraceCollector);
