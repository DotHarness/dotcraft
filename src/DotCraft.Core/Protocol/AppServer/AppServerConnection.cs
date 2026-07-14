using System.Collections.Concurrent;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Tracks per-connection state for an AppServer client.
/// One instance exists per active transport connection.
/// </summary>
public sealed class AppServerConnection
{
    private volatile bool _isInitialized;
    private volatile bool _isClientReady;
    private volatile bool _isClosed;
    private readonly TaskCompletionSource _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppServerClientInfo? _clientInfo;
    private AppServerClientCapabilities? _clientCapabilities;
    private HashSet<string>? _optOutMethods;

    // Active passive thread subscriptions: threadId → CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new();

    // Logical interactive requests already delivered on this connection.
    private readonly ConcurrentDictionary<InteractiveRequestKey, byte> _interactiveRequests = new();
    private readonly ConcurrentDictionary<McpAppItemKey, byte> _mcpAppItems = new();

    // -------------------------------------------------------------------------
    // Initialization state
    // -------------------------------------------------------------------------

    /// <summary>
    /// True after a successful <c>initialize</c> request has been processed.
    /// Methods other than <c>initialize</c> are rejected until this is true.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// True after the client sends the <c>initialized</c> notification.
    /// The server may begin proactively sending notifications only after this is true.
    /// </summary>
    public bool IsClientReady => _isClientReady;

    /// <summary>
    /// True after the underlying transport has closed and connection-scoped
    /// subscriptions/capabilities are no longer available.
    /// </summary>
    public bool IsClosed => _isClosed;

    /// <summary>
    /// Completes when the underlying transport closes.
    /// </summary>
    public Task Closed => _closedTcs.Task;

    /// <summary>Client identity from the <c>initialize</c> params.</summary>
    public AppServerClientInfo? ClientInfo => _clientInfo;

    /// <summary>Client-declared capabilities from the <c>initialize</c> params.</summary>
    public AppServerClientCapabilities? ClientCapabilities => _clientCapabilities;

    /// <summary>True when the client declared Interactive Tool UI support during <c>initialize</c>.</summary>
    public bool SupportsInteractiveToolUi => _clientCapabilities?.InteractiveToolUi == true;

    /// <summary>True when the client declared stable MCP Apps host support.</summary>
    public bool SupportsMcpApps => _clientCapabilities?.McpApps == true;

    // -------------------------------------------------------------------------
    // Channel adapter state (external-channel-adapter.md §5.1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The canonical channel name if this connection is an external channel adapter.
    /// Null for regular AppServer clients (CLI, VS Code extension, etc.).
    /// </summary>
    public string? ChannelAdapterName { get; private set; }

    /// <summary>
    /// True if this connection represents an external channel adapter.
    /// </summary>
    public bool IsChannelAdapter => ChannelAdapterName != null;

    /// <summary>
    /// Structured delivery descriptor declared by the adapter during initialize, if any.
    /// </summary>
    public ChannelDeliveryCapabilities? DeliveryCapabilities { get; private set; }

    /// <summary>
    /// Raw channel tool descriptors declared by the adapter during initialize.
    /// Registration diagnostics are resolved separately after the connection is attached.
    /// </summary>
    public IReadOnlyList<ChannelToolDescriptor> DeclaredChannelTools { get; private set; } = [];

    /// <summary>
    /// Validated channel tool descriptors that are currently available for runtime injection.
    /// </summary>
    public IReadOnlyList<ChannelToolDescriptor> RegisteredChannelTools { get; private set; } = [];

    /// <summary>
    /// Diagnostics produced while validating or registering channel tool descriptors.
    /// </summary>
    public IReadOnlyList<ChannelToolRegistrationDiagnostic> ChannelToolDiagnostics { get; private set; } = [];

    /// <summary>
    /// True once descriptor validation has been finalized for this connection.
    /// </summary>
    public bool ChannelToolRegistrationFinalized { get; private set; }

    /// <summary>
    /// Whether the adapter supports <c>ext/channel/send</c>.
    /// </summary>
    public bool SupportsStructuredDelivery => DeliveryCapabilities?.StructuredDelivery == true;

    // -------------------------------------------------------------------------
    // ACP extension state (appserver-protocol.md §11.2)
    // -------------------------------------------------------------------------

    /// <summary>
    /// ACP tool proxy capabilities from <c>initialize</c>, or null when not declared.
    /// </summary>
    public AcpExtensionCapability? AcpExtensions => _clientCapabilities?.AcpExtensions;

    /// <summary>
    /// True when the client sent a non-null <c>acpExtensions</c> object.
    /// </summary>
    public bool HasAcpExtensions => AcpExtensions != null;

    /// <summary>Client can receive <c>ext/acp/fs/readTextFile</c>.</summary>
    public bool SupportsAcpFsRead => AcpExtensions?.FsReadTextFile == true;

    /// <summary>Client can receive <c>ext/acp/fs/writeTextFile</c>.</summary>
    public bool SupportsAcpFsWrite => AcpExtensions?.FsWriteTextFile == true;

    /// <summary>Client can receive <c>ext/acp/terminal/*</c>.</summary>
    public bool SupportsAcpTerminal => AcpExtensions?.TerminalCreate == true;

    /// <summary>Custom extension families (e.g. <c>_unity</c>).</summary>
    public IReadOnlyList<string> AcpCustomExtensions => AcpExtensions?.Extensions ?? [];

    // -------------------------------------------------------------------------
    // Node REPL + browser extension state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Node REPL runtime capabilities from <c>initialize</c>, or null when not declared.
    /// </summary>
    public NodeReplCapability? NodeRepl => _clientCapabilities?.NodeRepl;

    /// <summary>
    /// True when the client sent a Node REPL capability object.
    /// </summary>
    public bool HasNodeRepl => NodeRepl != null;

    /// <summary>
    /// Browser IAB capabilities from <c>initialize</c>, or null when not declared.
    /// </summary>
    public BrowserUseCapability? BrowserUse => _clientCapabilities?.BrowserUse;

    /// <summary>
    /// True when the client sent a browser IAB capability object.
    /// </summary>
    public bool HasBrowserUse => BrowserUse != null;

    /// <summary>
    /// Browser automation backends declared by the client. Falls back to the legacy
    /// single <c>browserUse.backend</c> field when <c>browserUse.backends</c> is omitted.
    /// </summary>
    public IReadOnlyList<string> BrowserUseBackends
    {
        get
        {
            var browserUse = BrowserUse;
            if (browserUse == null)
                return [];

            var backends = new List<string>();
            if (browserUse.Backends is { Count: > 0 })
            {
                backends.AddRange(browserUse.Backends.Where(backend => !string.IsNullOrWhiteSpace(backend)));
            }

            if (!string.IsNullOrWhiteSpace(browserUse.Backend)
                && !backends.Contains(browserUse.Backend, StringComparer.OrdinalIgnoreCase))
            {
                backends.Insert(0, browserUse.Backend);
            }

            return backends;
        }
    }

    /// <summary>
    /// Marks the connection as initialized and stores the client's identity and capabilities.
    /// Returns <c>false</c> if already initialized (caller should reject with AlreadyInitialized).
    /// </summary>
    public bool TryMarkInitialized(AppServerClientInfo info, AppServerClientCapabilities? caps)
    {
        if (_isInitialized)
            return false;

        _clientInfo = info;
        _clientCapabilities = caps;
        _optOutMethods = caps?.OptOutNotificationMethods is { Count: > 0 }
            ? new HashSet<string>(caps.OptOutNotificationMethods, StringComparer.Ordinal)
            : null;

        // Extract channel adapter capability (external-channel-adapter.md §5.1)
        if (caps?.ChannelAdapter is { } ca)
        {
            ChannelAdapterName = ca.ChannelName;
            DeliveryCapabilities = ca.DeliveryCapabilities;
            DeclaredChannelTools = ca.ChannelTools?.ToArray() ?? [];
            RegisteredChannelTools = [];
            ChannelToolDiagnostics = [];
            ChannelToolRegistrationFinalized = false;
        }

        _isInitialized = true;
        return true;
    }

    /// <summary>
    /// Replaces the registered channel tool snapshot and associated diagnostics after host-level validation.
    /// </summary>
    public void SetChannelToolRegistration(
        IReadOnlyList<ChannelToolDescriptor>? registeredTools,
        IReadOnlyList<ChannelToolRegistrationDiagnostic>? diagnostics)
    {
        RegisteredChannelTools = registeredTools?.ToArray() ?? [];
        ChannelToolDiagnostics = diagnostics?.ToArray() ?? [];
        ChannelToolRegistrationFinalized = true;
    }

    /// <summary>
    /// Marks the connection as ready to receive server-initiated notifications.
    /// Called when the client sends the <c>initialized</c> notification.
    /// </summary>
    public void MarkClientReady() => _isClientReady = true;

    /// <summary>
    /// Marks the connection as closed. Safe to call more than once.
    /// </summary>
    public void MarkClosed()
    {
        _isClosed = true;
        _mcpAppItems.Clear();
        _closedTcs.TrySetResult();
    }

    /// <summary>Marks a terminal MCP item as delivered live on this connection.</summary>
    public bool TryRegisterMcpAppItem(string threadId, string itemId) =>
        SupportsMcpApps
        && !_isClosed
        && _mcpAppItems.TryAdd(new McpAppItemKey(threadId, itemId), 0);

    /// <summary>Returns whether an item may open a View on this exact connection.</summary>
    public bool IsMcpAppItemEligible(string threadId, string itemId) =>
        SupportsMcpApps
        && !_isClosed
        && _mcpAppItems.ContainsKey(new McpAppItemKey(threadId, itemId));

    // -------------------------------------------------------------------------
    // Notification opt-out
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if the server should send a notification with the given method name.
    /// Clients may opt out of specific methods during initialization (Section 10 of the wire spec).
    /// </summary>
    public bool ShouldSendNotification(string method) =>
        _optOutMethods == null || !_optOutMethods.Contains(method);

    /// <summary>
    /// Returns <c>true</c> if the client declared approval support (default true when not specified).
    /// </summary>
    public bool SupportsApproval =>
        _clientCapabilities?.ApprovalSupport != false;

    /// <summary>
    /// Returns <c>true</c> if the client can handle model-initiated user input requests.
    /// Defaults to false so legacy clients are not sent an unknown server request.
    /// </summary>
    public bool SupportsRequestUserInput =>
        _clientCapabilities?.RequestUserInputSupport == true;

    /// <summary>
    /// Registers a server-to-client interactive request for this connection.
    /// Returns <c>false</c> when the same logical request was already delivered.
    /// </summary>
    public bool TryRegisterInteractiveRequest(
        string method,
        string threadId,
        string turnId,
        string requestId) =>
        _interactiveRequests.TryAdd(new InteractiveRequestKey(method, threadId, turnId, requestId), 0);

    /// <summary>
    /// Returns <c>true</c> if the client declared streaming support (default true when not specified).
    /// </summary>
    public bool SupportsStreaming =>
        _clientCapabilities?.StreamingSupport != false;

    /// <summary>
    /// Returns <c>true</c> if the client declared command execution streaming support.
    /// Defaults to false so legacy clients continue to use toolCall/toolResult rendering.
    /// </summary>
    public bool SupportsCommandExecutionStreaming =>
        _clientCapabilities?.CommandExecutionStreaming == true;

    /// <summary>
    /// Returns <c>true</c> if the client declared tool execution lifecycle support.
    /// Defaults to false so legacy clients continue to use toolCall/toolResult rendering.
    /// </summary>
    public bool SupportsToolExecutionLifecycle =>
        _clientCapabilities?.ToolExecutionLifecycle == true;

    /// <summary>
    /// Returns true when the client declared background terminal notification support.
    /// </summary>
    public bool SupportsBackgroundTerminals =>
        _clientCapabilities?.BackgroundTerminals == true;

    /// <summary>
    /// Returns <c>true</c> if the client wants workspace/configChanged notifications.
    /// Defaults to true when not specified by the client.
    /// </summary>
    public bool SupportsConfigChange =>
        _clientCapabilities?.ConfigChange != false;

    // -------------------------------------------------------------------------
    // Thread subscriptions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a passive subscription for the given thread.
    /// Returns <c>false</c> if a subscription already exists (duplicate subscribe is a no-op).
    /// </summary>
    public bool TryAddSubscription(string threadId, CancellationTokenSource cts)
    {
        if (!_subscriptions.TryAdd(threadId, cts))
            return false;

        return true;
    }

    /// <summary>
    /// Cancels and removes the passive subscription for the given thread.
    /// Returns <c>false</c> if no subscription was found.
    /// </summary>
    public bool TryCancelSubscription(string threadId)
    {
        if (!_subscriptions.TryRemove(threadId, out var cts))
            return false;

        cts.Cancel();
        cts.Dispose();
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> if the connection has an active passive subscription for the given thread.
    /// </summary>
    public bool HasSubscription(string threadId) => _subscriptions.ContainsKey(threadId);

    /// <summary>
    /// Cancels all active subscriptions. Called on connection close or dispose.
    /// </summary>
    public void CancelAllSubscriptions()
    {
        foreach (var (key, cts) in _subscriptions)
        {
            if (_subscriptions.TryRemove(key, out _))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    private readonly record struct InteractiveRequestKey(
        string Method,
        string ThreadId,
        string TurnId,
        string RequestId);

    private readonly record struct McpAppItemKey(string ThreadId, string ItemId);
}

public sealed class ChannelToolRegistrationDiagnostic
{
    public string ToolName { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
