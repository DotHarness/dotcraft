using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;

namespace DotCraft.Protocol.AppServer;

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

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var mergedConfig = AppConfig.LoadWithGlobalFallback(configPath, workspaceConfig.EffectiveGlobalConfigPath);
        appConfigMonitor.Current.ProviderId = mergedConfig.ProviderId;
        appConfigMonitor.Current.Model = mergedConfig.Model;
        appConfigMonitor.Current.NetworkTimeoutSeconds = mergedConfig.NetworkTimeoutSeconds;
        appConfigMonitor.Current.Providers = mergedConfig.Providers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
    }

    public void RefreshCurrentPermissionsConfig(string? defaultApprovalPolicy)
    {
        if (appConfigMonitor == null)
            return;

        appConfigMonitor.Current.Permissions = new AppConfig.PermissionsConfig
        {
            DefaultApprovalPolicy = ToApprovalPolicy(defaultApprovalPolicy)
        };
    }

    public void RefreshCurrentMemoryConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var mergedConfig = LoadMergedWorkspaceConfig(useGlobalFallback: true);
        appConfigMonitor.Current.Memory = new MemoryConfig
        {
            AutoConsolidateEnabled = mergedConfig.Memory.AutoConsolidateEnabled,
            ConsolidateEveryNTurns = mergedConfig.Memory.ConsolidateEveryNTurns
        };
    }

    public void RefreshCurrentDreamsConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var mergedConfig = LoadMergedWorkspaceConfig(useGlobalFallback: false);
        appConfigMonitor.Current.Dreams = new DreamsConfig
        {
            Enabled = mergedConfig.Dreams.Enabled,
            Interval = mergedConfig.Dreams.Interval,
            StartupDelay = mergedConfig.Dreams.StartupDelay,
            ThreadLookbackCount = mergedConfig.Dreams.ThreadLookbackCount,
            AutoApply = mergedConfig.Dreams.AutoApply,
            HistoryTailChars = mergedConfig.Dreams.HistoryTailChars,
            MinCompletedTurnsSinceLastRun = mergedConfig.Dreams.MinCompletedTurnsSinceLastRun
        };
    }

    public void RefreshCurrentLspConfig(bool? toolsLspEnabled)
    {
        if (appConfigMonitor == null)
            return;

        if (toolsLspEnabled.HasValue)
        {
            appConfigMonitor.Current.Tools.Lsp.Enabled = toolsLspEnabled.Value;
            return;
        }

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var mergedConfig = LoadMergedWorkspaceConfig(useGlobalFallback: true);
            appConfigMonitor.Current.Tools.Lsp = mergedConfig.Tools.Lsp;
            return;
        }

        appConfigMonitor.Current.Tools.Lsp.Enabled = false;
    }

    public void RefreshCurrentReasoningConfig()
    {
        if (appConfigMonitor == null)
            return;

        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            var mergedConfig = LoadMergedWorkspaceConfig(useGlobalFallback: true);
            appConfigMonitor.Current.Reasoning = CloneReasoningConfig(mergedConfig.Reasoning);
            return;
        }

        appConfigMonitor.Current.Reasoning = new AppConfig.ReasoningConfig();
    }

    public void RefreshCurrentSubAgentConfig()
    {
        if (appConfigMonitor == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            return;

        var mergedConfig = LoadMergedWorkspaceConfig(useGlobalFallback: true);
        appConfigMonitor.Current.SubAgent = new AppConfig.SubAgentConfig
        {
            DisabledProfiles = [.. mergedConfig.SubAgent.DisabledProfiles],
            EnableExternalCliSessionResume = mergedConfig.SubAgent.EnableExternalCliSessionResume,
            Model = mergedConfig.SubAgent.Model,
            MinWaitTimeoutMs = mergedConfig.SubAgent.MinWaitTimeoutMs,
            DefaultWaitTimeoutMs = mergedConfig.SubAgent.DefaultWaitTimeoutMs,
            MaxWaitTimeoutMs = mergedConfig.SubAgent.MaxWaitTimeoutMs,
            MaxDepth = mergedConfig.SubAgent.MaxDepth,
            Roles = [.. mergedConfig.SubAgent.Roles.Select(role => role.Clone())]
        };
        appConfigMonitor.Current.SubAgentProfiles = mergedConfig.SubAgentProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Clone())
            .ToList();
    }

    public void InvalidateThreadAgents()
    {
        if (sessionService is IThreadAgentRefreshService refreshService)
            refreshService.InvalidateThreadAgents();
    }

    private AppConfig LoadMergedWorkspaceConfig(bool useGlobalFallback)
    {
        var configPath = Path.Combine(workspaceCraftPath!, "config.json");
        return useGlobalFallback
            ? AppConfig.LoadWithGlobalFallback(configPath, workspaceConfig.EffectiveGlobalConfigPath)
            : AppConfig.LoadWithGlobalFallback(configPath);
    }

    private static ApprovalPolicy ToApprovalPolicy(string? rawPolicy)
    {
        return rawPolicy switch
        {
            "autoApprove" => ApprovalPolicy.AutoApprove,
            _ => ApprovalPolicy.Default
        };
    }

    private static AppConfig.ReasoningConfig CloneReasoningConfig(AppConfig.ReasoningConfig source) => new()
    {
        Enabled = source.Enabled,
        Effort = source.Effort,
        Output = source.Output
    };
}
