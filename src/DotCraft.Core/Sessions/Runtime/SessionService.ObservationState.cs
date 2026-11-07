namespace DotCraft.Sessions;

public sealed partial class SessionService : IDisposable
{
    private async Task PrepareObservationStateAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!runtime.Broker.HasPlan(runtime.Generation))
        {
            var plan = agentFactory.PlanStore is { } store
                ? await store.LoadStructuredPlanAsync(runtime.Thread.Id) : null;
            ct.ThrowIfCancellationRequested();
            runtime.Broker.SeedPlanIfMissing(plan, runtime.Generation);
        }
        if (!runtime.Broker.HasContextUsage(runtime.Generation))
            runtime.Broker.SeedContextUsageIfMissing(TryGetContextUsageSnapshot(runtime.Thread.Id), runtime.Generation);
    }

    public void Dispose() => _runtimeRegistry.Dispose();
}
