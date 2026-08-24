using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;

namespace DotCraft.Oratorio.Integrations;

public sealed class DotCraftStatusService(
    IOptionsMonitor<DotCraftOptions> options,
    IDotCraftAppServerProcessManager processManager)
{
    public async Task<DotCraftStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var value = options.CurrentValue;
        var workspacePath = ResolveStatusWorkspacePath(value);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new DotCraftStatusResponse(
                false,
                false,
                "unavailable",
                value.HubDiscoveryEnabled,
                "",
                value.AppServerUrl,
                string.IsNullOrWhiteSpace(value.AppServerUrl) ? "hub" : "configuration",
                string.IsNullOrWhiteSpace(value.ApprovalPolicy) ? "interrupt" : value.ApprovalPolicy,
                value.RunTimeoutSeconds,
                value.ManagedWorktreesEnabled,
                string.IsNullOrWhiteSpace(value.WorktreeRoot) ? "<repositoryWorkspace>/.craft/oratorio/worktrees" : value.WorktreeRoot,
                value.EffectiveGlobalMaxActiveRuns,
                value.EffectiveMaxActiveRunsPerRepository,
                value.EffectiveMaxActiveRunsPerSource,
                "workspaceMappingMissing",
                "No repository workspace is configured.");
        }

        var probe = await processManager.ProbeAsync(workspacePath, ct);
        var connected = probe.Connected;
        var endpoint = probe.Endpoint?.Url ?? value.AppServerUrl;
        var configured = probe.Endpoint is not null || !string.IsNullOrWhiteSpace(value.AppServerUrl);
        var health = connected ? "connected" : configured ? "configured" : "unavailable";
        var message = probe.Message ?? (connected
            ? "DotCraft AppServer is reachable."
            : configured
                ? "DotCraft AppServer is configured but not reachable."
                : "DotCraft AppServer is not configured.");

        return new DotCraftStatusResponse(
            configured,
            connected,
            health,
            value.HubDiscoveryEnabled,
            workspacePath,
            endpoint,
            probe.Endpoint?.Source ?? "configuration",
            string.IsNullOrWhiteSpace(value.ApprovalPolicy) ? "interrupt" : value.ApprovalPolicy,
            value.RunTimeoutSeconds,
            value.ManagedWorktreesEnabled,
            string.IsNullOrWhiteSpace(value.WorktreeRoot) ? "<repositoryWorkspace>/.craft/oratorio/worktrees" : value.WorktreeRoot,
            value.EffectiveGlobalMaxActiveRuns,
            value.EffectiveMaxActiveRunsPerRepository,
            value.EffectiveMaxActiveRunsPerSource,
            probe.Reason,
            message);
    }

    public async Task<DotCraftStatusResponse> StartAppServerAsync(CancellationToken ct)
    {
        var value = options.CurrentValue;
        var workspacePaths = ResolveStatusWorkspacePaths(value);
        if (workspacePaths.Count == 0)
        {
            throw new InvalidOperationException("No repository workspace is configured.");
        }

        foreach (var workspacePath in workspacePaths)
        {
            await processManager.EnsureAvailableAsync(workspacePath, ct);
        }

        return await GetStatusAsync(ct);
    }

    private static string ResolveStatusWorkspacePath(DotCraftOptions value) =>
        ResolveStatusWorkspacePaths(value).FirstOrDefault() ?? "";

    private static IReadOnlyList<string> ResolveStatusWorkspacePaths(DotCraftOptions value) =>
        value.RepositoryWorkspaceRoutes.Select(route => route.WorkspacePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
