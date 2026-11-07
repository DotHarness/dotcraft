namespace DotCraft.Sessions;

public sealed partial class SessionService : IDisposable
{
    private async Task PrepareObservationPlanAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        if (runtime.Broker.HasPlan(runtime.Generation))
            return;
        ct.ThrowIfCancellationRequested();
        var plan = agentFactory.PlanStore is { } store
            ? await store.LoadStructuredPlanAsync(runtime.Thread.Id) : null;
        ct.ThrowIfCancellationRequested();
        runtime.Broker.SeedPlanIfMissing(plan, runtime.Generation);
    }

    public void Dispose() => _runtimeRegistry.Dispose();
}
