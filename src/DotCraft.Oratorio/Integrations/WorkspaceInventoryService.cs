using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Integrations;

/// <summary>
/// Builds the operator-visible DotCraft workspace inventory from configured routing policy.
/// </summary>
public sealed class WorkspaceInventoryService(
    IOptionsMonitor<DotCraftOptions> options,
    IDotCraftAppServerProcessManager processManager,
    IClock clock)
{
    /// <summary>
    /// Returns whether the current configuration routes to one or multiple workspace paths.
    /// </summary>
    public string GetWorkspaceMode()
    {
        var workspaces = ListConfiguredWorkspaces(options.CurrentValue);
        return workspaces.Count > 1 ? "multi" : "single";
    }

    /// <summary>
    /// Lists configured workspace paths with AppServer endpoint health for each path.
    /// </summary>
    public async Task<DotCraftWorkspacesResponse> GetWorkspacesAsync(CancellationToken ct)
    {
        var value = options.CurrentValue;
        var configured = ListConfiguredWorkspaces(value);
        var probes = await Task.WhenAll(configured.Select(entry => ProbeWorkspaceAsync(value, entry, ct)));
        var workspaces = probes
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DotCraftWorkspacesResponse(
            clock.UtcNow,
            new DotCraftWorkspacesSummary(
                workspaces.Count,
                workspaces.Count(x => x.Connected),
                workspaces.Count(x => x.HubManaged),
                ""),
            workspaces);
    }

    private async Task<DotCraftWorkspaceDto> ProbeWorkspaceAsync(DotCraftOptions value, WorkspaceEntry entry, CancellationToken ct)
    {
        var probe = await processManager.ProbeAsync(entry.Path, ct);
        var connected = probe.Connected;
        var endpoint = probe.Endpoint?.Url ?? "";
        var configured = probe.Endpoint is not null;
        var health = connected ? "connected" : configured ? "configured" : "unavailable";
        var endpointSource = probe.Endpoint?.Source ?? "hub";
        var hubManaged = string.Equals(endpointSource, "hub", StringComparison.OrdinalIgnoreCase);

        return new DotCraftWorkspaceDto(
            entry.Path,
            entry.Label,
            entry.IsDefault,
            entry.Repositories,
            configured,
            connected,
            health,
            endpoint,
            endpointSource,
            hubManaged,
            probe.Reason,
            probe.Message);
    }

    private static IReadOnlyList<WorkspaceEntry> ListConfiguredWorkspaces(DotCraftOptions value)
    {
        var entries = new Dictionary<string, WorkspaceEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in value.RepositoryWorkspaceRoutes.OrderBy(x => x.Project, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(mapping.WorkspacePath))
            {
                continue;
            }

            var path = NormalizeWorkspacePath(mapping.WorkspacePath);
            var repository = mapping.Project.Trim();
            if (entries.TryGetValue(path, out var existing))
            {
                entries[path] = existing with { Repositories = [.. existing.Repositories, repository] };
            }
            else
            {
                entries[path] = new WorkspaceEntry(path, BuildLabel(path), IsDefault: false, [repository]);
            }
        }

        return entries.Values.ToList();
    }

    private static string NormalizeWorkspacePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

    private static string BuildLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Workspace";
        }

        var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(label))
        {
            label = path;
        }

        return label;
    }

    private sealed record WorkspaceEntry(
        string Path,
        string Label,
        bool IsDefault,
        IReadOnlyList<string> Repositories);
}
