namespace DotCraft.Hub;

public sealed partial class ManagedAppServerRegistry
{
    internal IEnumerable<string> DashboardWorkspacePaths() =>
        _entries.Values.Where(entry => entry.StartedInCurrentHubProcess).Select(entry => entry.CanonicalWorkspacePath);

    internal (string State, DashboardTarget? Target) GetDashboardWorkspace(string workspacePath)
    {
        if (!_entries.TryGetValue(workspacePath, out var entry)
            || entry.State != HubAppServerStates.Running
            || entry.Process is not { IsRunning: true })
            return ("stopped", null);

        return entry.DashboardTarget is { } target ? ("available", target) : ("disabled", null);
    }

    internal sealed record DashboardTarget(Uri Origin, string Token);
}
