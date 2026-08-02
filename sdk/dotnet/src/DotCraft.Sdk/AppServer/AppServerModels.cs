using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Sdk.AppServer;

/// <summary>
/// DotCraft AppServer initialize options.
/// </summary>
public class DotCraftClientOptions
{
    /// <summary>
    /// Overrides the connection entry point's reconnect default. Local and remote connections
    /// default to enabled; custom transports default to disabled.
    /// </summary>
    public bool? AutoReconnect { get; set; }

    /// <summary>
    /// Machine-readable client name.
    /// </summary>
    public string ClientName { get; set; } = "dotcraft-dotnet";

    /// <summary>
    /// Human-readable client title.
    /// </summary>
    public string? ClientTitle { get; set; }

    /// <summary>
    /// Client version string.
    /// </summary>
    public string ClientVersion { get; set; } = "0.1.0";

    /// <summary>
    /// Whether the client can answer approval requests.
    /// </summary>
    public bool ApprovalSupport { get; set; }

    /// <summary>
    /// Whether the client wants streaming delta notifications.
    /// </summary>
    public bool StreamingSupport { get; set; } = true;

    /// <summary>
    /// Whether the client can answer model request-user-input prompts.
    /// </summary>
    public bool RequestUserInputSupport { get; set; }

    /// <summary>
    /// Whether the client wants workspace config change notifications.
    /// </summary>
    public bool ConfigChange { get; set; } = true;

    /// <summary>
    /// Additional client capability fields merged into the initialize request.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExtraCapabilities { get; set; }

    /// <summary>
    /// Optional handler for server-initiated approval requests.
    /// </summary>
    public ApprovalHandler? ApprovalHandler { get; set; }

    /// <summary>
    /// Optional handler for server-initiated user-input requests.
    /// Providing a handler also advertises <c>requestUserInputSupport</c>.
    /// </summary>
    public UserInputHandler? UserInputHandler { get; set; }
}

/// <summary>
/// Options for Hub-backed local AppServer connections.
/// </summary>
public sealed class DotCraftLocalClientOptions : DotCraftClientOptions
{
    /// <summary>
    /// Optional dotcraft executable or dll path used when starting Hub.
    /// </summary>
    public string? Executable { get; set; }

    /// <summary>
    /// Optional override for the Hub lock file path.
    /// </summary>
    public string? HubLockPath { get; set; }

    /// <summary>
    /// Optional user profile directory used to resolve Hub and default Chat workspace paths.
    /// </summary>
    public string? UserProfilePath { get; set; }

    /// <summary>
    /// Hub startup timeout.
    /// </summary>
    public TimeSpan HubStartupTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Result of the AppServer initialize handshake.
/// </summary>
public sealed record AppServerInitializeResult(
    AppServerServerInfo ServerInfo,
    AppServerServerCapabilities Capabilities,
    JsonElement Raw);

/// <summary>
/// Server identity returned by AppServer initialize.
/// </summary>
public sealed record AppServerServerInfo(
    string Name,
    string Version,
    string ProtocolVersion,
    IReadOnlyList<string> Extensions);

/// <summary>
/// Server capability flags returned by AppServer initialize.
/// </summary>
public sealed record AppServerServerCapabilities(
    bool ThreadManagement,
    bool ThreadSubscriptions,
    bool DynamicToolRebind,
    bool RuntimeAdditionalContext,
    int AppBindingVersion,
    bool ModelCatalogManagement,
    JsonElement Raw,
    bool ConfigOverride = false,
    bool ProviderManagement = false);

/// <summary>
/// Raw AppServer notification.
/// </summary>
public sealed record AppServerNotification(string Method, JsonElement Params);

/// <summary>
/// Session identity for thread operations.
/// </summary>
public sealed record SessionIdentity(
    string ChannelName,
    string UserId,
    string? WorkspacePath = null,
    string? ChannelContext = null);

/// <summary>
/// AppServer thread/start request.
/// </summary>
public sealed record DotCraftThreadStartRequest(
    SessionIdentity Identity,
    string? DisplayName = null,
    string HistoryMode = "server",
    object? Config = null,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools = null,
    IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext = null);

/// <summary>
/// AppServer thread/resume request options.
/// </summary>
public sealed record DotCraftThreadResumeRequest(
    string ThreadId,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools = null,
    IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext = null);

/// <summary>
/// Thread discovery scope for <c>thread/list</c>.
/// </summary>
public enum DotCraftThreadListScope
{
    /// <summary>Apply the connected session identity and optional cross-channel rules.</summary>
    Identity,

    /// <summary>List every non-internal thread in the exact workspace.</summary>
    Workspace
}

/// <summary>
/// Options for listing threads.
/// </summary>
public sealed record DotCraftThreadListOptions(
    bool IncludeArchived = false,
    DotCraftThreadListScope Scope = DotCraftThreadListScope.Identity);

/// <summary>
/// Thread-bound runtime context supplied by an AppServer client.
/// </summary>
public sealed record RuntimeAdditionalContextEntry(
    string Value,
    string Kind = "application");

/// <summary>
/// AppServer thread/read result with raw thread JSON.
/// </summary>
public sealed record DotCraftThreadReadResult(
    string ThreadId,
    JsonElement Thread,
    JsonElement? TurnPage = null,
    DotCraftModelConfiguration? ModelConfiguration = null);

/// <summary>
/// One input part for turn/start and turn/enqueue.
/// For image input, <paramref name="Url"/> must be a base64 <c>data:image/...</c> URL;
/// HTTP and HTTPS image URLs are not supported.
/// </summary>
public sealed record TurnInputPart(
    string Type,
    string? Text = null,
    string? Name = null,
    string? ArgsText = null,
    string? RawText = null,
    string? Path = null,
    string? DisplayPath = null,
    string? Url = null,
    string? MimeType = null,
    string? FileName = null);

/// <summary>
/// turn/start result.
/// </summary>
public sealed record DotCraftTurnStartResult(string? TurnId, JsonElement Raw);

/// <summary>
/// turn/enqueue result.
/// </summary>
public sealed record DotCraftTurnEnqueueResult(string? QueuedInputId, JsonElement Raw);

/// <summary>Safe provenance for an effective MCP runtime server.</summary>
public sealed record McpServerOrigin(
    string Kind,
    string? PluginId = null,
    string? PluginDisplayName = null,
    string? DeclaredName = null,
    string? ThreadId = null,
    string? BindingId = null);

/// <summary>One source-aware MCP runtime status entry.</summary>
public sealed record McpServerRuntimeStatus(
    string Name,
    JsonElement? ServerInfo,
    IReadOnlyDictionary<string, JsonElement> Tools,
    IReadOnlyList<JsonElement> Resources,
    IReadOnlyList<JsonElement> ResourceTemplates,
    string AuthStatus,
    string? DeclaredName = null,
    string? RuntimeName = null,
    McpServerOrigin? Origin = null);

public sealed record McpServerStatusListParams(
    string? ThreadId = null,
    string? Cursor = null,
    int? Limit = null,
    string? Detail = null);

public sealed record McpServerStatusListResult(
    IReadOnlyList<McpServerRuntimeStatus> Data,
    string? NextCursor = null);

public sealed record McpServerResourceReadParams(string Server, string Uri, string? ThreadId = null);
public sealed record McpServerResourceReadResult(JsonElement Contents);

public sealed record McpServerToolCallParams(
    string ThreadId,
    string Server,
    string Tool,
    IReadOnlyDictionary<string, object?>? Arguments = null,
    [property: JsonPropertyName("_meta")] JsonElement? Meta = null);

public sealed record McpServerToolCallResult(
    JsonElement Content,
    JsonElement? StructuredContent = null,
    bool IsError = false,
    [property: JsonPropertyName("_meta")] JsonElement? Meta = null);

public sealed record McpServerOAuthLoginParams(
    string Name,
    string? ThreadId = null,
    IReadOnlyList<string>? Scopes = null,
    double? TimeoutSecs = null);

public sealed record McpServerOAuthLoginResult(string AuthorizationUrl);
public sealed record McpServerReloadResult;

public sealed record McpServerStartupStatusUpdatedNotification(
    string Name,
    string Status,
    string? ThreadId = null,
    string? Error = null,
    string? FailureReason = null);

public sealed record McpServerOAuthLoginCompletedNotification(
    string Name,
    bool Success,
    string? ThreadId = null,
    string? Error = null);

public sealed record McpServerElicitationRequest(
    string ServerName,
    string Mode,
    string? ThreadId = null,
    string? TurnId = null,
    string? ElicitationId = null,
    string? Message = null,
    string? Url = null,
    JsonElement? RequestedSchema = null,
    [property: JsonPropertyName("_meta")] JsonElement? Meta = null);

public sealed record McpServerElicitationResponse(
    string Action,
    IReadOnlyDictionary<string, object?>? Content = null,
    [property: JsonPropertyName("_meta")] JsonElement? Meta = null);

/// <summary>
/// Base type for a Runtime Dynamic Tool Function/Namespace declaration.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RuntimeDynamicToolFunction), "function")]
[JsonDerivedType(typeof(RuntimeDynamicToolNamespace), "namespace")]
public abstract record RuntimeDynamicToolDeclaration(
    string Name,
    string Description);

/// <summary>
/// A Runtime Dynamic Function. A top-level function has no namespace.
/// </summary>
public sealed record RuntimeDynamicToolFunction(
    string Name,
    string Description,
    JsonElement InputSchema,
    bool DeferLoading = false,
    ToolApprovalDescriptor? Approval = null)
    : RuntimeDynamicToolDeclaration(Name, Description);

/// <summary>
/// A Runtime Dynamic Namespace containing one or more functions.
/// </summary>
public sealed record RuntimeDynamicToolNamespace(
    string Name,
    string Description,
    IReadOnlyList<RuntimeDynamicToolDeclaration> Tools)
    : RuntimeDynamicToolDeclaration(Name, Description);

/// <summary>
/// Approval metadata for a runtime dynamic tool.
/// </summary>
public sealed record ToolApprovalDescriptor(
    string Kind,
    string TargetArgument,
    string? Operation = null,
    string? OperationArgument = null);

/// <summary>
/// Runtime dynamic tool call sent from AppServer to the SDK client.
/// </summary>
public sealed record DynamicToolCall(
    string ThreadId,
    string? TurnId,
    string? CallId,
    string? Namespace,
    string Tool,
    JsonElement Arguments);

/// <summary>
/// Runtime dynamic tool result returned by the SDK client.
/// </summary>
public sealed record DynamicToolResult(
    bool Success,
    IReadOnlyList<ToolContentItem>? ContentItems = null,
    object? StructuredContent = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Text or media content returned from a runtime dynamic tool.
/// </summary>
public sealed record ToolContentItem(
    string Type,
    string? Text = null,
    string? MediaType = null,
    string? Url = null,
    string? DataBase64 = null);

internal static class JsonElementReaders
{
    public static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    public static string? ReadNestedString(JsonElement element, string container, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(container, out var nested))
        {
            return null;
        }

        return ReadString(nested, propertyName);
    }

    public static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    public static bool ReadNestedBoolean(JsonElement element, string container, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(container, out var nested))
        {
            return false;
        }

        if (nested.ValueKind != JsonValueKind.Object || !nested.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True;
    }
}
