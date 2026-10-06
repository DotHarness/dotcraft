using DotCraft.Configuration;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>
/// Refreshes process-local runtime config caches after AppServer config mutations.
/// </summary>
internal sealed class AppServerRuntimeConfigRefresher(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    WorkspaceConfigEditor workspaceConfig)
{
    public void RefreshCurrentLlmConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var mergedConfig = workspaceConfig.LoadCurrentMergedConfig();
        appConfigMonitor.Current.ProviderId = mergedConfig.ProviderId;
        appConfigMonitor.Current.ProviderPreferences = mergedConfig.ProviderPreferences.ToDictionary(
            pair => pair.Key,
            pair => ModelPreferenceRules.Clone(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        appConfigMonitor.Current.NetworkTimeoutSeconds = mergedConfig.NetworkTimeoutSeconds;
        appConfigMonitor.Current.Providers = mergedConfig.Providers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

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
                pair => ModelPreferenceRules.Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            MinWaitTimeoutMs = mergedConfig.SubAgent.MinWaitTimeoutMs,
            DefaultWaitTimeoutMs = mergedConfig.SubAgent.DefaultWaitTimeoutMs,
            MaxWaitTimeoutMs = mergedConfig.SubAgent.MaxWaitTimeoutMs,
            MaxDepth = mergedConfig.SubAgent.MaxDepth,
            MaxConcurrentSubAgents = mergedConfig.SubAgent.MaxConcurrentSubAgents,
            Roles = [.. mergedConfig.SubAgent.Roles.Select(role => role.Clone())]
        };
        appConfigMonitor.Current.SubAgentProfiles = mergedConfig.SubAgentProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Clone())
            .ToList();
    }

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
