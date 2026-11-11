using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class AppServerRuntimeConfigRefresher(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    WorkspaceConfigEditor workspaceConfig)
{
    public void RefreshCurrentSubAgentConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var mergedConfig = LoadMergedWorkspaceConfig();
        appConfigMonitor.Current.SubAgent = new AppConfig.SubAgentConfig
        {
            DisabledProfiles = [.. mergedConfig.SubAgent.DisabledProfiles],
            EnableExternalCliSessionResume = mergedConfig.SubAgent.EnableExternalCliSessionResume,
            ProviderPreferences = mergedConfig.SubAgent.ProviderPreferences.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase),
            Models = [.. mergedConfig.SubAgent.Models.Select(choice => new SubAgentModelChoice
            {
                ProviderId = choice.ProviderId,
                Model = choice.Model
            })],
            MaxDepth = mergedConfig.SubAgent.MaxDepth,
            MaxConcurrentSubAgents = mergedConfig.SubAgent.MaxConcurrentSubAgents,
            Roles = [.. mergedConfig.SubAgent.Roles.Select(role => role.Clone())]
        };
        appConfigMonitor.Current.SubAgentProfiles = mergedConfig.SubAgentProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Clone())
            .ToList();
    }

    public bool RefreshSubAgentProfilesChangedOnDisk()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return false;

        var onDisk = LoadMergedWorkspaceConfig().SubAgentProfiles;
        if (ProfileFingerprint(onDisk) == ProfileFingerprint(appConfigMonitor.Current.SubAgentProfiles))
            return false;

        RefreshCurrentSubAgentConfig();
        InvalidateThreadAgents();
        return true;
    }

    private static string ProfileFingerprint(IEnumerable<SubAgentProfile> profiles) =>
        string.Join('\n', profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => $"{profile.Name}={JsonSerializer.Serialize(profile)}"));

    public void RefreshCurrentHooksConfig()
    {
        if (appConfigMonitor == null)
            return;

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var mergedConfig = LoadMergedWorkspaceConfig();
            appConfigMonitor.Current.Hooks = mergedConfig.Hooks;
            return;
        }

        appConfigMonitor.Current.Hooks = new AppConfig.HooksConfig();
    }

    public Task RefreshMcpRuntimesAsync(CancellationToken ct) =>
        sessionService is IThreadMcpRuntimeService mcpRuntime
            ? mcpRuntime.RefreshMcpRuntimesAsync(ct)
            : Task.CompletedTask;

    public void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }

    private AppConfig LoadMergedWorkspaceConfig() =>
        AppConfig.LoadWithGlobalFallback(
            Path.Combine(workspaceCraftPath!, "config.json"),
            workspaceConfig.EffectiveGlobalConfigPath);
}
