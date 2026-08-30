using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Explicitly maps initialization contracts to connection state and capability snapshots.</summary>
internal static class InitializeContractMapper
{
    public static ClientConnectionInfo ToConnectionInfo(Contract.ClientInfo value) => new()
    {
        Name = value.Name,
        Title = value.Title,
        Version = value.Version
    };

    public static ClientConnectionCapabilities? ToConnectionCapabilities(Contract.ClientCapabilities? value)
    {
        if (value is null)
            return null;

        return new ClientConnectionCapabilities
        {
            AppBindingVersion = value.AppBindingVersion,
            ApprovalSupport = value.ApprovalSupport,
            RequestUserInputSupport = value.RequestUserInputSupport,
            StreamingSupport = value.StreamingSupport,
            CommandExecutionStreaming = value.CommandExecutionStreaming,
            ToolExecutionLifecycle = value.ToolExecutionLifecycle,
            BackgroundTerminals = value.BackgroundTerminals,
            OptOutNotificationMethods = value.OptOutNotificationMethods?.ToList(),
            ConfigChange = value.ConfigChange,
            InteractiveToolUi = value.InteractiveToolUi,
            McpApps = value.McpApps,
            InlineVisualizations = value.InlineVisualizations,
            McpElicitation = value.McpElicitation,
            ChannelAdapter = ToChannelAdapter(value.ChannelAdapter),
            AcpExtensions = ToAcp(value.AcpExtensions),
            NodeRepl = value.NodeRepl is null ? null : new NodeReplClientCapability { Backend = value.NodeRepl.Backend },
            BrowserUse = ToBrowserUse(value.BrowserUse)
        };
    }

    public static Contract.ServerCapabilities ToContract(ServerCapabilitySnapshot value) => new()
    {
        ThreadManagement = value.ThreadManagement,
        ThreadSubscriptions = value.ThreadSubscriptions,
        ThreadGoals = value.ThreadGoals,
        ThreadFork = value.ThreadFork,
        GitWorktrees = value.GitWorktrees,
        ManualCompaction = value.ManualCompaction,
        ManualMemoryConsolidation = value.ManualMemoryConsolidation,
        DynamicToolRebind = value.DynamicToolRebind,
        RuntimeAdditionalContext = value.RuntimeAdditionalContext,
        AppBindingVersion = value.AppBindingVersion,
        AppThreadInputEnqueue = value.AppThreadInputEnqueue,
        ThreadMaintenanceInterrupt = value.ThreadMaintenanceInterrupt,
        ApprovalFlow = value.ApprovalFlow,
        RequestUserInput = value.RequestUserInput,
        ModeSwitch = value.ModeSwitch,
        ConfigOverride = value.ConfigOverride,
        BackgroundTerminals = value.BackgroundTerminals,
        CronManagement = value.CronManagement,
        HeartbeatManagement = value.HeartbeatManagement,
        SkillsManagement = value.SkillsManagement,
        PluginManagement = value.PluginManagement,
        PluginConfiguration = value.PluginConfiguration,
        PluginMarketplaces = value.PluginMarketplaces,
        SkillVariants = value.SkillVariants,
        CommandManagement = value.CommandManagement,
        Automations = value.Automations,
        ChannelStatus = value.ChannelStatus,
        ModelCatalogManagement = value.ModelCatalogManagement,
        ProviderManagement = value.ProviderManagement,
        WorkspaceConfigManagement = value.WorkspaceConfigManagement,
        SourceControlManagement = value.SourceControlManagement,
        MemoryManagement = value.MemoryManagement,
        Dreams = value.Dreams,
        McpManagement = value.McpManagement,
        McpRuntime = value.McpRuntime,
        McpApps = value.McpApps,
        InlineVisualizations = value.InlineVisualizations,
        McpElicitation = value.McpElicitation,
        McpServerOrigins = value.McpServerOrigins,
        ExternalChannelManagement = value.ExternalChannelManagement,
        ToolCatalog = value.ToolCatalog,
        AgentProfileManagement = value.AgentProfileManagement,
        SubAgentManagement = value.SubAgentManagement,
        SubAgentSessions = value.SubAgentSessions,
        McpStatus = value.McpStatus,
        HooksManagement = value.HooksManagement,
        UsageTelemetry = value.UsageTelemetry,
        AuthOpenAiOAuth = value.AuthOpenAiOAuth,
        AuthOpenAiUsage = value.AuthOpenAiUsage,
        Extensions = ToContractExtensions(value.Extensions)
    };

    private static Contract.ServerCapabilityExtensions? ToContractExtensions(
        IReadOnlyDictionary<string, object>? extensions)
    {
        if (extensions is null)
            return null;

        Contract.TeamsCapabilities? teams = null;
        Dictionary<string, JsonElement>? extensionData = null;
        foreach (var (key, value) in extensions)
        {
            if (string.Equals(key, "teams", StringComparison.OrdinalIgnoreCase)
                && value is Contract.TeamsCapabilities typedTeams)
            {
                teams = typedTeams;
                continue;
            }

            extensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            extensionData[key] = JsonSerializer.SerializeToElement(
                value,
                Protocol.AppServerContractJson.Options);
        }

        return new Contract.ServerCapabilityExtensions
        {
            Teams = teams,
            ExtensionData = extensionData
        };
    }

    private static ChannelAdapterRuntimeCapability? ToChannelAdapter(Contract.ChannelAdapterCapability? value)
    {
        if (value is null)
            return null;
        return new ChannelAdapterRuntimeCapability
        {
            ChannelName = value.ChannelName,
            DeliveryCapabilities = ToDeliveryCapabilities(value.DeliveryCapabilities),
            ChannelTools = value.ChannelTools?.Select(ToChannelTool).ToList()
        };
    }

    private static ChannelToolSpec ToChannelTool(Contract.ChannelToolDescriptor value) => new()
    {
        Name = value.Name,
        Description = value.Description,
        InputSchema = JsonNode.Parse(value.InputSchema.GetRawText()) as JsonObject,
        OutputSchema = value.OutputSchema is { } output ? JsonNode.Parse(output.GetRawText()) as JsonObject : null,
        Display = value.Display is null ? null : new ChannelToolDisplaySpec
        {
            Title = value.Display.Title,
            Subtitle = value.Display.Subtitle,
            Icon = value.Display.Icon
        },
        Approval = value.Approval is null ? null : new ChannelToolApprovalSpec
        {
            Kind = value.Approval.Kind,
            TargetArgument = value.Approval.TargetArgument,
            Operation = value.Approval.Operation,
            OperationArgument = value.Approval.OperationArgument
        },
        RequiresChatContext = value.RequiresChatContext,
        DeferLoading = value.DeferLoading
    };

    private static ChannelDeliveryCapabilitySnapshot? ToDeliveryCapabilities(Contract.ChannelDeliveryCapabilities? value) =>
        value is null
            ? null
            : new ChannelDeliveryCapabilitySnapshot
            {
                StructuredDelivery = value.StructuredDelivery,
                Media = value.Media is null
                    ? null
                    : new ChannelMediaCapabilitySnapshot
                    {
                        File = ToMediaConstraints(value.Media.File),
                        Audio = ToMediaConstraints(value.Media.Audio),
                        Image = ToMediaConstraints(value.Media.Image),
                        Video = ToMediaConstraints(value.Media.Video)
                    }
            };

    private static ChannelMediaConstraintSnapshot? ToMediaConstraints(Contract.ChannelMediaConstraints? value) =>
        value is null
            ? null
            : new ChannelMediaConstraintSnapshot
            {
                MaxBytes = value.MaxBytes,
                AllowedMimeTypes = value.AllowedMimeTypes?.ToList(),
                AllowedExtensions = value.AllowedExtensions?.ToList(),
                SupportsHostPath = value.SupportsHostPath,
                SupportsUrl = value.SupportsUrl,
                SupportsBase64 = value.SupportsBase64,
                SupportsCaption = value.SupportsCaption
            };

    private static AcpClientCapability? ToAcp(Contract.AcpExtensionCapability? value) =>
        value is null
            ? null
            : new AcpClientCapability
            {
                FsReadTextFile = value.FsReadTextFile,
                FsWriteTextFile = value.FsWriteTextFile,
                TerminalCreate = value.TerminalCreate,
                Extensions = value.Extensions?.ToList()
            };

    private static BrowserUseClientCapability? ToBrowserUse(Contract.BrowserUseCapability? value) =>
        value is null
            ? null
            : new BrowserUseClientCapability
            {
                Backend = value.Backend,
                Backends = value.Backends?.ToList(),
                ProtocolVersion = value.ProtocolVersion,
                SupportsCancel = value.SupportsCancel,
                BrowserSessionProtocolVersion = value.BrowserSessionProtocolVersion,
                SupportsCommandCancel = value.SupportsCommandCancel,
                MaxBrowserResultBytes = value.MaxBrowserResultBytes,
                DefaultCommandTimeoutMs = value.DefaultCommandTimeoutMs,
                MaxCommandTimeoutMs = value.MaxCommandTimeoutMs,
                SupportsTypedFinalize = value.SupportsTypedFinalize,
                SupportsChromeDiagnostics = value.SupportsChromeDiagnostics
            };
}
