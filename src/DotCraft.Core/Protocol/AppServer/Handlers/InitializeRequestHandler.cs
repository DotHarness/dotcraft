namespace DotCraft.Protocol.AppServer;

internal sealed class InitializeRequestHandler(
    AppServerConnection connection,
    AppServerConnectionServices services,
    IReadOnlyList<IAppServerCapabilityContributor> capabilityContributors,
    SkillVariantContext skillVariants,
    AppServerThreadWireProjector threadProjector) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.Initialize, HandleInitializeAsync);
    }

    private Task<object?> HandleInitializeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<AppServerInitializeParams>(msg);
        if (p.Capabilities?.AppBindingVersion is { } requestedAppBindingVersion
            && requestedAppBindingVersion != DotCraft.AppBinding.AppBindingContract.Version)
        {
            throw AppServerErrors.AppBindingUpgradeRequired();
        }
        if (!connection.TryMarkInitialized(p.ClientInfo, p.Capabilities))
            throw AppServerErrors.AlreadyInitialized();

        var workspaceCraftPath = services.WorkspaceCraftPath;
        var capabilities = new AppServerServerCapabilities
        {
            ThreadManagement = true,
            ThreadSubscriptions = true,
            ThreadGoals = threadProjector.GoalsCapabilityEnabled(),
            ThreadFork = true,
            GitWorktrees = true,
            ManualCompaction = true,
            ManualMemoryConsolidation = services.MemoryStore != null,
            DynamicToolRebind = services.WireDynamicToolProxy != null,
            RuntimeAdditionalContext = services.WireRuntimeAdditionalContextProvider != null,
            ThreadMaintenanceInterrupt = true,
            ApprovalFlow = true,
            RequestUserInput = true,
            ModeSwitch = true,
            ConfigOverride = true,
            BackgroundTerminals = services.BackgroundTerminalService != null,
            CronManagement = services.CronService != null,
            HeartbeatManagement = services.HeartbeatService != null,
            SkillsManagement = services.SkillsLoader != null,
            PluginManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            SkillVariants = services.SkillsLoader != null && skillVariants.IsVariantModeEnabled(),
            CommandManagement = true,
            Automations = services.AutomationsHandler != null,
            ChannelStatus = services.ChannelStatusProvider != null,
            ProviderManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            ModelCatalogManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            WorkspaceConfigManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            SourceControlManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            MemoryManagement = services.MemoryStore != null,
            Dreams = services.DreamsService != null && !string.IsNullOrWhiteSpace(workspaceCraftPath),
            McpManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath) && services.McpClientManager != null,
            McpRuntime = services.McpClientManager != null,
            McpApps = services.McpClientManager != null && p.Capabilities?.McpApps == true,
            InlineVisualizations = services.InlineVisualizationAssetStore != null
                && services.InlineVisualizationRuntimeRegistry != null
                && p.Capabilities?.InlineVisualizations == true,
            McpElicitation = services.McpClientManager != null && p.Capabilities?.McpElicitation == true,
            McpServerOrigins = services.McpClientManager != null,
            ExternalChannelManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            AgentProfileManagement = true,
            ToolCatalog = true,
            SubAgentManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            AuthOpenAiOAuth = services.OpenAIAuthService != null,
            AuthOpenAiUsage = services.OpenAIUsageService != null,
            SubAgentSessions = true,
            McpStatus = services.McpClientManager != null,
            HooksManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            UsageTelemetry = services.TraceStore != null
        };

        var capabilityBuilder = new AppServerCapabilityBuilder(capabilities, workspaceCraftPath);
        foreach (var contributor in capabilityContributors)
            contributor.ContributeCapabilities(capabilityBuilder);
        if (services.WelcomeSuggestionService != null)
            capabilityBuilder.SetExtension("welcomeSuggestions", true);

        return Task.FromResult<object?>(new AppServerInitializeResult
        {
            ServerInfo = new AppServerServerInfo
            {
                Name = "dotcraft",
                Version = services.ServerVersion,
                ProtocolVersion = "1"
            },
            Capabilities = capabilities,
            DashboardUrl = services.DashboardUrl
        });
    }
}
