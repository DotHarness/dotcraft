using DotCraft.Agents;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class InitializeRequestHandler(
    AppServerConnection connection,
    AppServerConnectionServices services,
    IReadOnlyList<IAppServerCapabilityContributor> capabilityContributors,
    SkillVariantContext skillVariants,
    AppServerThreadWireProjector threadProjector) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.Initialize, HandleInitializeAsync);
    }

    private Task<AppServerTypedResult<Contract.InitializeResult>> HandleInitializeAsync(
        AppServerTypedRequest<Contract.InitializeParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var clientInfo = InitializeContractMapper.ToConnectionInfo(request.Params.ClientInfo);
        var clientCapabilities = InitializeContractMapper.ToConnectionCapabilities(request.Params.Capabilities);
        if (clientCapabilities?.AppBindingVersion is { } requestedAppBindingVersion
            && requestedAppBindingVersion != AppBinding.AppBindingContract.Version)
        {
            throw AppServerErrors.AppBindingUpgradeRequired();
        }
        if (!connection.TryMarkInitialized(clientInfo, clientCapabilities))
            throw AppServerErrors.AlreadyInitialized();

        var workspaceCraftPath = services.WorkspaceCraftPath;
        var capabilities = new ServerCapabilitySnapshot
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
            PluginMarketplaces = !string.IsNullOrWhiteSpace(workspaceCraftPath),
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
            McpApps = services.McpClientManager != null && clientCapabilities?.McpApps == true,
            InlineVisualizations = services.InlineVisualizationAssetStore != null
                && services.InlineVisualizationRuntimeRegistry != null
                && clientCapabilities?.InlineVisualizations == true,
            McpElicitation = services.McpClientManager != null && clientCapabilities?.McpElicitation == true,
            McpServerOrigins = services.McpClientManager != null,
            ExternalChannelManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            AgentProfileManagement = true,
            ToolCatalog = true,
            SubAgentManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            AuthOpenAiOAuth = HasOpenAIService<IProviderAuthentication>(services.ModelProviderRegistry),
            AuthOpenAiUsage = HasOpenAIService<IProviderUsageReader>(services.ModelProviderRegistry),
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

        var result = new Contract.InitializeResult
        {
            ServerInfo = new Contract.ServerInfo
            {
                Name = "dotcraft",
                Version = services.ServerVersion,
                ProtocolVersion = "1"
            },
            Capabilities = InitializeContractMapper.ToContract(capabilities),
            DashboardUrl = services.DashboardUrl
        };
        return Task.FromResult(AppServerTypedResult<Contract.InitializeResult>.FromResult(result));
    }

    private static bool HasOpenAIService<TService>(ModelProviderRegistry? registry)
        where TService : class =>
        registry != null
        && registry.TryResolve(ModelProviderProtocols.OpenAIResponses, out var provider)
        && provider?.GetService(typeof(TService)) is TService;
}
