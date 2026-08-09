using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Client identity sent during initialization.</summary>
public sealed class ClientInfo : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }
}

/// <summary>External-channel client capability.</summary>
public sealed class ChannelAdapterCapability : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    public required string ChannelName { get; init; }

    [JsonPropertyName("deliverySupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeliverySupport { get; init; }

    [JsonPropertyName("deliveryCapabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDeliveryCapabilities? DeliveryCapabilities { get; init; }

    [JsonPropertyName("channelTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ChannelToolDescriptor>? ChannelTools { get; init; }
}

/// <summary>ACP extension support declared by a client.</summary>
public sealed class AcpExtensionCapability : ExtensibleJsonObject
{
    [JsonPropertyName("fsReadTextFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FsReadTextFile { get; init; }

    [JsonPropertyName("fsWriteTextFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FsWriteTextFile { get; init; }

    [JsonPropertyName("terminalCreate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TerminalCreate { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Extensions { get; init; }
}

/// <summary>Node REPL support declared by a client.</summary>
public sealed class NodeReplCapability : ExtensibleJsonObject
{
    [JsonPropertyName("backend")]
    public required string Backend { get; init; }
}

/// <summary>Browser runtime support declared by a client.</summary>
public sealed class BrowserUseCapability : ExtensibleJsonObject
{
    [JsonPropertyName("backend")]
    public required string Backend { get; init; }

    [JsonPropertyName("backends")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Backends { get; init; }

    [JsonPropertyName("protocolVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProtocolVersion { get; init; }

    [JsonPropertyName("supportsCancel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsCancel { get; init; }

    [JsonPropertyName("browserSessionProtocolVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BrowserSessionProtocolVersion { get; init; }

    [JsonPropertyName("supportsCommandCancel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsCommandCancel { get; init; }

    [JsonPropertyName("maxBrowserResultBytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxBrowserResultBytes { get; init; }

    [JsonPropertyName("defaultCommandTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DefaultCommandTimeoutMs { get; init; }

    [JsonPropertyName("maxCommandTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCommandTimeoutMs { get; init; }

    [JsonPropertyName("supportsTypedFinalize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsTypedFinalize { get; init; }

    [JsonPropertyName("supportsChromeDiagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsChromeDiagnostics { get; init; }
}

/// <summary>Capabilities declared by an AppServer client.</summary>
public sealed class ClientCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("appBindingVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AppBindingVersion { get; init; }

    [JsonPropertyName("approvalSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ApprovalSupport { get; init; }

    [JsonPropertyName("requestUserInputSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequestUserInputSupport { get; init; }

    [JsonPropertyName("streamingSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StreamingSupport { get; init; }

    [JsonPropertyName("commandExecutionStreaming")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CommandExecutionStreaming { get; init; }

    [JsonPropertyName("toolExecutionLifecycle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ToolExecutionLifecycle { get; init; }

    [JsonPropertyName("backgroundTerminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? BackgroundTerminals { get; init; }

    [JsonPropertyName("optOutNotificationMethods")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? OptOutNotificationMethods { get; init; }

    [JsonPropertyName("configChange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ConfigChange { get; init; }

    [JsonPropertyName("interactiveToolUi")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InteractiveToolUi { get; init; }

    [JsonPropertyName("mcpApps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? McpApps { get; init; }

    [JsonPropertyName("inlineVisualizations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineVisualizations { get; init; }

    [JsonPropertyName("mcpElicitation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? McpElicitation { get; init; }

    [JsonPropertyName("channelAdapter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelAdapterCapability? ChannelAdapter { get; init; }

    [JsonPropertyName("acpExtensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AcpExtensionCapability? AcpExtensions { get; init; }

    [JsonPropertyName("nodeRepl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NodeReplCapability? NodeRepl { get; init; }

    [JsonPropertyName("browserUse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserUseCapability? BrowserUse { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Parameters for the initialize request.</summary>
public sealed class InitializeParams : ExtensibleJsonObject
{
    [JsonPropertyName("clientInfo")]
    public required ClientInfo ClientInfo { get; init; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientCapabilities? Capabilities { get; init; }
}

/// <summary>AppServer implementation information.</summary>
public sealed class ServerInfo : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Extensions { get; init; }
}

/// <summary>Typed extension capabilities advertised by AppServer.</summary>
public sealed class ServerCapabilityExtensions : ExtensibleJsonObject
{
    [JsonPropertyName("teams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TeamsCapabilities? Teams { get; init; }
}

/// <summary>Capabilities advertised by AppServer.</summary>
public sealed class ServerCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("threadManagement")]
    public bool ThreadManagement { get; init; }

    [JsonPropertyName("threadSubscriptions")]
    public bool ThreadSubscriptions { get; init; }

    [JsonPropertyName("dynamicToolRebind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DynamicToolRebind { get; init; }

    [JsonPropertyName("runtimeAdditionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RuntimeAdditionalContext { get; init; }

    [JsonPropertyName("threadGoals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadGoals { get; init; }

    [JsonPropertyName("threadFork")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadFork { get; init; }

    [JsonPropertyName("gitWorktrees")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool GitWorktrees { get; init; }

    [JsonPropertyName("manualCompaction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManualCompaction { get; init; }

    [JsonPropertyName("manualMemoryConsolidation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManualMemoryConsolidation { get; init; }

    [JsonPropertyName("appBindingVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AppBindingVersion { get; init; }

    [JsonPropertyName("appThreadInputEnqueue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AppThreadInputEnqueue { get; init; }

    [JsonPropertyName("threadMaintenanceInterrupt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ThreadMaintenanceInterrupt { get; init; }

    [JsonPropertyName("approvalFlow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ApprovalFlow { get; init; }

    [JsonPropertyName("requestUserInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequestUserInput { get; init; }

    [JsonPropertyName("modeSwitch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ModeSwitch { get; init; }

    [JsonPropertyName("configOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConfigOverride { get; init; }

    [JsonPropertyName("backgroundTerminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BackgroundTerminals { get; init; }

    [JsonPropertyName("cronManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CronManagement { get; init; }

    [JsonPropertyName("heartbeatManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeartbeatManagement { get; init; }

    [JsonPropertyName("skillsManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkillsManagement { get; init; }

    [JsonPropertyName("pluginManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PluginManagement { get; init; }

    [JsonPropertyName("pluginMarketplaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PluginMarketplaces { get; init; }

    [JsonPropertyName("skillVariants")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkillVariants { get; init; }

    [JsonPropertyName("commandManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CommandManagement { get; init; }

    [JsonPropertyName("automations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Automations { get; init; }

    [JsonPropertyName("channelStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ChannelStatus { get; init; }

    [JsonPropertyName("modelCatalogManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ModelCatalogManagement { get; init; }

    [JsonPropertyName("providerManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ProviderManagement { get; init; }

    [JsonPropertyName("workspaceConfigManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WorkspaceConfigManagement { get; init; }

    [JsonPropertyName("sourceControlManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SourceControlManagement { get; init; }

    [JsonPropertyName("memoryManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MemoryManagement { get; init; }

    [JsonPropertyName("dreams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dreams { get; init; }

    [JsonPropertyName("mcpManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpManagement { get; init; }

    [JsonPropertyName("mcpRuntime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpRuntime { get; init; }

    [JsonPropertyName("mcpApps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpApps { get; init; }

    [JsonPropertyName("inlineVisualizations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool InlineVisualizations { get; init; }

    [JsonPropertyName("mcpElicitation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpElicitation { get; init; }

    [JsonPropertyName("mcpServerOrigins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpServerOrigins { get; init; }

    [JsonPropertyName("externalChannelManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ExternalChannelManagement { get; init; }

    [JsonPropertyName("toolCatalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ToolCatalog { get; init; }

    [JsonPropertyName("agentProfileManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AgentProfileManagement { get; init; }

    [JsonPropertyName("subAgentManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SubAgentManagement { get; init; }

    [JsonPropertyName("subAgentSessions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SubAgentSessions { get; init; }

    [JsonPropertyName("mcpStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool McpStatus { get; init; }

    [JsonPropertyName("hooksManagement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HooksManagement { get; init; }

    [JsonPropertyName("usageTelemetry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsageTelemetry { get; init; }

    [JsonPropertyName("authOpenAiOAuth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AuthOpenAiOAuth { get; init; }

    [JsonPropertyName("authOpenAiUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AuthOpenAiUsage { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerCapabilityExtensions? Extensions { get; init; }
}

/// <summary>Result of the initialize request.</summary>
public sealed class InitializeResult : ExtensibleJsonObject
{
    [JsonPropertyName("serverInfo")]
    public required ServerInfo ServerInfo { get; init; }

    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; init; }

    [JsonPropertyName("dashboardUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DashboardUrl { get; init; }
}

/// <summary>Identity used for thread ownership and discovery.</summary>
public sealed class SessionIdentity : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    public required string ChannelName { get; init; }

    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; init; }

    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; init; }
}

/// <summary>One runtime-owned context value attached to a thread.</summary>
public sealed class RuntimeAdditionalContextEntry : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>Base type for a runtime dynamic-tool declaration.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RuntimeDynamicToolFunction), "function")]
[JsonDerivedType(typeof(RuntimeDynamicToolNamespace), "namespace")]
public abstract class RuntimeDynamicToolDeclaration : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>Approval metadata for a runtime dynamic tool.</summary>
public sealed class ToolApprovalDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("targetArgument")]
    public required string TargetArgument { get; init; }

    [JsonPropertyName("operation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }

    [JsonPropertyName("operationArgument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationArgument { get; init; }
}

/// <summary>A callable runtime dynamic tool.</summary>
public sealed class RuntimeDynamicToolFunction : RuntimeDynamicToolDeclaration
{
    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InputSchema { get; init; }

    [JsonPropertyName("deferLoading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; init; }

    [JsonPropertyName("approval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolApprovalDescriptor? Approval { get; init; }
}

/// <summary>A namespace containing runtime dynamic tools.</summary>
public sealed class RuntimeDynamicToolNamespace : RuntimeDynamicToolDeclaration
{
    [JsonPropertyName("tools")]
    public required IReadOnlyList<RuntimeDynamicToolDeclaration> Tools { get; init; }
}

/// <summary>Parameters for creating a thread.</summary>
public sealed class ThreadStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("identity")]
    public required SessionIdentity Identity { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools { get; init; }

    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; init; }

    [JsonPropertyName("historyMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    [JsonPropertyName("spawnedFromThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnedFromThreadId { get; init; }
}

/// <summary>Parameters for resuming a thread.</summary>
public sealed class ThreadResumeParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools { get; init; }

    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; init; }
}

/// <summary>Parameters for exporting a Thread recovery snapshot.</summary>
public sealed class ThreadRecoveryExportParams : ExtensibleJsonObject
{
    /// <summary>Existing durable Thread identifier.</summary>
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

}

/// <summary>Parameters for restoring a Thread recovery snapshot.</summary>
public sealed class ThreadRecoveryRestoreParams : ExtensibleJsonObject
{
    /// <summary>Package path directly under workspace-local recovery staging.</summary>
    [JsonPropertyName("packagePath")]
    public required string PackagePath { get; init; }

    /// <summary>Original Thread identifier expected inside the package.</summary>
    [JsonPropertyName("expectedThreadId")]
    public required string ExpectedThreadId { get; init; }
}

/// <summary>Parameters for listing threads.</summary>
public sealed class ThreadListParams : ExtensibleJsonObject
{
    [JsonPropertyName("identity")]
    public required SessionIdentity Identity { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }

    [JsonPropertyName("includeArchived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeArchived { get; init; }

    [JsonPropertyName("includeSubAgents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeSubAgents { get; init; }

    [JsonPropertyName("includeInternal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeInternal { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; init; }

    [JsonPropertyName("crossChannelOrigins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CrossChannelOrigins { get; init; }

    [JsonPropertyName("query")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Query { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }
}

/// <summary>Parameters for reading a thread.</summary>
public sealed class ThreadReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }
}

/// <summary>Parameters for reading one page of Turn metadata.</summary>
public sealed class ThreadTurnsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }

    [JsonPropertyName("sortDirection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SortDirection { get; init; }
}

/// <summary>Parameters for reading one page of Items.</summary>
public sealed class ThreadItemsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; init; }

    [JsonPropertyName("sortDirection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SortDirection { get; init; }
}

/// <summary>One input part in a turn request.</summary>
public sealed class InputPart : ExtensibleJsonObject
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("argsText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArgsText { get; init; }

    [JsonPropertyName("rawText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawText { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonPropertyName("displayPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayPath { get; init; }

    /// <summary>
    /// Base64 <c>data:image/...</c> URL for an <c>image</c> input part.
    /// HTTP and HTTPS image URLs are not supported.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("fileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; init; }
}

/// <summary>Identifies the sender of a turn within its originating channel.</summary>
public sealed class SenderContext : ExtensibleJsonObject
{
    [JsonPropertyName("senderId")]
    public required string SenderId { get; init; }

    [JsonPropertyName("senderName")]
    public required string SenderName { get; init; }

    [JsonPropertyName("senderRole")]
    public required string SenderRole { get; init; }

    [JsonPropertyName("groupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; init; }
}

/// <summary>Durable actor metadata describing who initiated a turn.</summary>
public sealed class TurnInitiatorContext : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    public required string ChannelName { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("userName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserName { get; init; }

    [JsonPropertyName("userRole")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserRole { get; init; }

    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; init; }

    [JsonPropertyName("groupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; init; }
}

/// <summary>Parameters for starting a turn.</summary>
public sealed class TurnStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<InputPart> Input { get; init; }

    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; init; }

    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Messages { get; init; }

    [JsonPropertyName("sentAsGoal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; init; }
}

/// <summary>Parameters for queueing a turn.</summary>
public sealed class TurnEnqueueParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<InputPart> Input { get; init; }

    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; init; }

    [JsonPropertyName("sentAsGoal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; init; }
}

/// <summary>Parameters for interrupting a turn.</summary>
public sealed class TurnInterruptParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }
}

/// <summary>Wire snapshot of a session item.</summary>
public sealed class SessionItem : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("payloadKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadKind { get; init; }

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Payload { get; init; }

    [JsonPropertyName("mcpApp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpAppViewHint? McpApp { get; init; }
}

/// <summary>Indicates that a terminal MCP item can currently open a new MCP App view.</summary>
public sealed class McpAppViewHint : ExtensibleJsonObject
{
    [JsonPropertyName("available")]
    public required bool Available { get; init; }
}

/// <summary>Wire snapshot of a turn.</summary>
public sealed class SessionTurn : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("startedAt")]
    public required DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("tokenUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenUsageInfo? TokenUsage { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("originChannel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginChannel { get; init; }

    [JsonPropertyName("initiator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnInitiatorContext? Initiator { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionItem>? Items { get; init; }
}

/// <summary>Wire snapshot of a thread.</summary>
public sealed class SessionThread : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("workspacePath")]
    public required string WorkspacePath { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    public required IReadOnlyList<string> RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("effectiveWorkspacePath")]
    public required string EffectiveWorkspacePath { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonPropertyName("forkedFromId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForkedFromId { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentThreadId { get; init; }

    [JsonPropertyName("ephemeral")]
    public required bool Ephemeral { get; init; }

    [JsonPropertyName("worktree")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadWorktreeInfo? Worktree { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("originChannel")]
    public required string OriginChannel { get; init; }

    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    [JsonPropertyName("source")]
    public required ThreadSource Source { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("lastActiveAt")]
    public required DateTimeOffset LastActiveAt { get; init; }

    [JsonPropertyName("historyMode")]
    public required string HistoryMode { get; init; }

    [JsonPropertyName("configuration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Configuration { get; init; }

    [JsonPropertyName("metadata")]
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    [JsonPropertyName("runtime")]
    public required ThreadRuntimeState Runtime { get; init; }

    [JsonPropertyName("queuedInputs")]
    public required IReadOnlyList<QueuedTurnInput> QueuedInputs { get; init; }

    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadGoal? Goal { get; init; }

    [JsonPropertyName("appBindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ThreadAppBindingSummary>? AppBindings { get; init; }

    [JsonPropertyName("originApp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginApp? OriginApp { get; init; }

    [JsonPropertyName("originPresentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginPresentation? OriginPresentation { get; init; }

    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionTurn>? Turns { get; init; }

    [JsonPropertyName("plan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlan? Plan { get; init; }

    [JsonPropertyName("contextUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextUsageSnapshot? ContextUsage { get; init; }
}

/// <summary>Describes why a thread exists.</summary>
public sealed class ThreadSource : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("subAgent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubAgentThreadSource? SubAgent { get; init; }

    [JsonPropertyName("spawnedFromThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnedFromThreadId { get; init; }
}

/// <summary>Details for a thread spawned as a subagent.</summary>
public sealed class SubAgentThreadSource : ExtensibleJsonObject
{
    [JsonPropertyName("parentThreadId")]
    public required string ParentThreadId { get; init; }

    [JsonPropertyName("parentTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentTurnId { get; init; }

    [JsonPropertyName("spawnCallId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnCallId { get; init; }

    [JsonPropertyName("rootThreadId")]
    public required string RootThreadId { get; init; }

    [JsonPropertyName("depth")]
    public required int Depth { get; init; }

    [JsonPropertyName("agentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentPath { get; init; }

    [JsonPropertyName("taskName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskName { get; init; }

    [JsonPropertyName("agentNickname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentNickname { get; init; }

    [JsonPropertyName("agentRole")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentRole { get; init; }

    [JsonPropertyName("profileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileName { get; init; }

    [JsonPropertyName("runtimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimeType { get; init; }

    [JsonPropertyName("supportsSendInput")]
    public required bool SupportsSendInput { get; init; }

    [JsonPropertyName("supportsResume")]
    public required bool SupportsResume { get; init; }

    [JsonPropertyName("supportsSendMessage")]
    public required bool SupportsSendMessage { get; init; }

    [JsonPropertyName("supportsFollowupTask")]
    public required bool SupportsFollowupTask { get; init; }

    [JsonPropertyName("supportsClose")]
    public required bool SupportsClose { get; init; }
}

/// <summary>Resolved App Binding origin branding for a thread.</summary>
public sealed class ThreadOriginApp : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    [JsonPropertyName("memberId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MemberId { get; init; }
}

/// <summary>Source-neutral origin presentation for a thread.</summary>
public sealed class ThreadOriginPresentation : ExtensibleJsonObject
{
    [JsonPropertyName("sourceId")]
    public required string SourceId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    [JsonPropertyName("subjectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectId { get; init; }

    [JsonPropertyName("subjectKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubjectKind { get; init; }
}

/// <summary>Persisted structured plan for a thread.</summary>
public sealed class SessionPlan : ExtensibleJsonObject
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("overview")]
    public required string Overview { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("todos")]
    public required IReadOnlyList<SessionPlanTodo> Todos { get; init; }
}

/// <summary>One task in a persisted structured plan.</summary>
public sealed class SessionPlanTodo : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>Compact thread entry returned by thread/list.</summary>
public sealed class ThreadSummary : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; init; }

    [JsonPropertyName("originChannel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginChannel { get; init; }

    [JsonPropertyName("channelContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadSource? Source { get; init; }

    [JsonPropertyName("forkedFromId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForkedFromId { get; init; }

    [JsonPropertyName("ephemeral")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ephemeral { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo?> Worktree { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("lastActiveAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastActiveAt { get; init; }

    [JsonPropertyName("turnCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TurnCount { get; init; }

    [JsonPropertyName("runtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadRuntimeState? Runtime { get; init; }

    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadGoal? Goal { get; init; }

    [JsonPropertyName("appBindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ThreadAppBindingSummary>? AppBindings { get; init; }

    [JsonPropertyName("originApp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginApp? OriginApp { get; init; }

    [JsonPropertyName("originPresentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginPresentation? OriginPresentation { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionTurn>? Turns { get; init; }
}

/// <summary>Result wrapper for thread/start.</summary>
public sealed class ThreadStartResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    public required SessionThread Thread { get; init; }
}

/// <summary>Result wrapper for thread/resume.</summary>
public sealed class ThreadResumeResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    public required SessionThread Thread { get; init; }
}

/// <summary>Descriptor for a JSON Thread recovery snapshot in local staging.</summary>
public sealed class ThreadRecoveryExportResult : ExtensibleJsonObject
{
    /// <summary>Absolute path directly under workspace-local recovery staging.</summary>
    [JsonPropertyName("packagePath")]
    public required string PackagePath { get; init; }

    /// <summary>Original Thread identifier.</summary>
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    /// <summary>Newest terminal Turn captured by the package.</summary>
    [JsonPropertyName("terminalTurnId")]
    public required string TerminalTurnId { get; init; }

    /// <summary>DotCraft-owned recovery package format version.</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; }

    /// <summary>Snapshot file length in bytes.</summary>
    [JsonPropertyName("byteLength")]
    [JsonSafeInteger]
    public long ByteLength { get; init; }

    /// <summary>Lowercase SHA-256 of the complete snapshot file.</summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }
}

/// <summary>Result identifying a restored, not-yet-resumed Thread.</summary>
public sealed class ThreadRecoveryRestoreResult : ExtensibleJsonObject
{
    /// <summary>Restored Thread identifier. Use thread/resume to obtain the authoritative snapshot.</summary>
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }
}

/// <summary>Result wrapper for thread/read.</summary>
public sealed class ThreadReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    public required SessionThread Thread { get; init; }
}

/// <summary>Result for thread/turns/list.</summary>
public sealed class ThreadTurnsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<SessionTurn> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

/// <summary>One Item page entry with its owning Turn id.</summary>
public sealed class ThreadItemListEntry : ExtensibleJsonObject
{
    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("item")]
    public required SessionItem Item { get; init; }
}

/// <summary>Result for thread/items/list.</summary>
public sealed class ThreadItemsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ThreadItemListEntry> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

/// <summary>Result wrapper for thread/list.</summary>
public sealed class ThreadListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ThreadSummary> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }

    [JsonPropertyName("totalMatched")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalMatched { get; init; }
}

/// <summary>Result wrapper for turn/start.</summary>
public sealed class TurnStartResult : ExtensibleJsonObject
{
    [JsonPropertyName("turn")]
    public required SessionTurn Turn { get; init; }
}

/// <summary>Queued turn input returned by queue operations.</summary>
public sealed class QueuedTurnInput : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("nativeInputParts")]
    public required IReadOnlyList<InputPart> NativeInputParts { get; init; }

    [JsonPropertyName("materializedInputParts")]
    public required IReadOnlyList<InputPart> MaterializedInputParts { get; init; }

    [JsonPropertyName("displayText")]
    public required string DisplayText { get; init; }

    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("readyAfterTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReadyAfterTurnId { get; init; }

    [JsonPropertyName("triggerKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerKind { get; init; }

    [JsonPropertyName("triggerLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerLabel { get; init; }

    [JsonPropertyName("triggerRefId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerRefId { get; init; }

    [JsonPropertyName("deliveryBindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryBindingId { get; init; }

    [JsonPropertyName("sentAsGoal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; init; }
}

/// <summary>Result of turn/enqueue.</summary>
public sealed class TurnEnqueueResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInput")]
    public required QueuedTurnInput QueuedInput { get; init; }

    [JsonPropertyName("queuedInputs")]
    public required IReadOnlyList<QueuedTurnInput> QueuedInputs { get; init; }
}

/// <summary>Thread lifecycle notification payload.</summary>
public sealed class ThreadNotification : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    public required SessionThread Thread { get; init; }

    [JsonPropertyName("resumedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumedBy { get; init; }
}

/// <summary>Thread deletion notification payload.</summary>
public sealed class ThreadDeletedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }
}

/// <summary>Turn lifecycle notification payload.</summary>
public sealed class TurnNotification : ExtensibleJsonObject
{
    [JsonPropertyName("turn")]
    public required SessionTurn Turn { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

/// <summary>Item lifecycle notification payload.</summary>
public sealed class ItemNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; }

    [JsonPropertyName("item")]
    public required SessionItem Item { get; init; }
}

/// <summary>Incremental item delta notification.</summary>
public sealed class ItemDeltaNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; }

    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemId { get; init; }

    [JsonPropertyName("deltaKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeltaKind { get; init; }

    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; init; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; init; }

    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
}

/// <summary>Parameters for an approval callback.</summary>
public sealed class ApprovalRequestParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public required string ItemId { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("approvalType")]
    public required string ApprovalType { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("scopeKey")]
    public required string ScopeKey { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Result of an approval callback.</summary>
public sealed class ApprovalResponseResult : ExtensibleJsonObject
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }
}

/// <summary>One selectable user-input option.</summary>
public sealed class UserInputOption : ExtensibleJsonObject
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

/// <summary>One structured user-input question.</summary>
public sealed class UserInputQuestion : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("header")]
    public required string Header { get; init; }

    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("isOther")]
    public bool IsOther { get; init; }

    [JsonPropertyName("isSecret")]
    public bool IsSecret { get; init; }

    [JsonPropertyName("options")]
    public required IReadOnlyList<UserInputOption> Options { get; init; }
}

/// <summary>Parameters for a user-input callback.</summary>
public sealed class UserInputRequestParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("itemId")]
    public required string ItemId { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyList<UserInputQuestion> Questions { get; init; }
}

/// <summary>Answer to one structured user-input question.</summary>
public sealed class UserInputAnswer : ExtensibleJsonObject
{
    [JsonPropertyName("answers")]
    public required IReadOnlyList<string> Answers { get; init; }
}

/// <summary>Result of a user-input callback.</summary>
public sealed class UserInputResponseResult : ExtensibleJsonObject
{
    [JsonPropertyName("answers")]
    public required IReadOnlyDictionary<string, UserInputAnswer> Answers { get; init; }
}

/// <summary>Parameters for a dynamic-tool callback.</summary>
public sealed class DynamicToolCallParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    public required string TurnId { get; init; }

    [JsonPropertyName("callId")]
    public required string CallId { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}

/// <summary>One content item returned by a dynamic tool.</summary>
public sealed class DynamicToolContentItem : ExtensibleJsonObject
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonPropertyName("dataBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; init; }
}

/// <summary>Result returned by a dynamic tool.</summary>
public sealed class DynamicToolCallResult : ExtensibleJsonObject
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("contentItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DynamicToolContentItem>? ContentItems { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? StructuredContent { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }
}

/// <summary>Structured AppServer error data.</summary>
public sealed class RpcErrorData : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
}

/// <summary>JSON-RPC error object.</summary>
public sealed class RpcError : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcErrorData? Data { get; init; }
}
