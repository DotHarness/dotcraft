using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>Describes capabilities selected for the active provider during tool planning.</summary>
public sealed record ProviderHostedCapabilityPlan
{
    internal DeferredToolSearchPlan? DeferredToolSearch { get; init; }
}

internal sealed record DeferredToolSearchPlan(
    DeferredToolLoadingMode Mode,
    string Strategy,
    string ProviderProtocol,
    int MaxSearchResults,
    Tracing.TraceCollector? TraceCollector);

public static class ProviderHostedCapabilityPlanner
{
    internal static ProviderHostedCapabilityPlan Build(AgentRuntimeContext context)
    {
        var deferredMode = DeferredToolLoadingPlanner.ResolveMode(
            context.Config.Tools.DeferredLoading,
            context.EffectiveProviderProtocol);
        return new ProviderHostedCapabilityPlan
        {
            DeferredToolSearch = deferredMode == DeferredToolLoadingMode.Off
                ? null
                : new DeferredToolSearchPlan(
                    deferredMode,
                    context.Config.Tools.DeferredLoading.Strategy.ToString(),
                    Configuration.ModelProviderProtocols.Normalize(context.EffectiveProviderProtocol),
                    context.Config.Tools.DeferredLoading.MaxSearchResults,
                    context.TraceCollector)
        };
    }
}
