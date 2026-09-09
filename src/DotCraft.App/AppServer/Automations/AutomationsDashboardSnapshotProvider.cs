using DotCraft.Modules;

namespace DotCraft.Automations.DashBoard;

/// <summary>Exposes unified automation definitions to the dashboard.</summary>
public sealed class AutomationsDashboardSnapshotProvider(AutomationService service) : IOrchestratorSnapshotProvider
{
    public string Name => "automations";

    public object GetSnapshot()
    {
        var definitions = service.ListAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new
        {
            automations = definitions,
            countsByStatus = definitions.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.Count()),
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    public void TriggerRefresh() { }
}
