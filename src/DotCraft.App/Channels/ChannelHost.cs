using System.Diagnostics;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Workspaces;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Modules;
using DotCraft.Processes;
using DotCraft.Logging;
using DotCraft.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Agents;

namespace DotCraft.Channels;

/// <summary>
/// Exposes one external adapter connection as a channel; subprocess modes also own the adapter process and its restarts.
/// </summary>
public sealed partial class ChannelHost : IChannelService, IAdapterChannelToolRuntime
{
    private sealed record AdapterToolBinding(
        IAppServerTransport Transport,
        AppServerConnection Connection);

    private const int MaxLogLines = 200;
    private readonly ChannelEntry _config;
    private readonly string _hostWorkspacePath;
    private readonly ChannelDeliveryDependencies _delivery;
    private readonly ChannelAdapterRequestHandlerFactory _requestHandlerFactory;
    private readonly Func<ProcessStartInfo, ManagedChildProcess> _managedChildProcessFactory;
    private readonly IAppConfigMonitor? _appConfigMonitor;
    private readonly IThreadAgentRefreshService? _threadAgentRefreshService;
    private readonly WireRuntimeAdditionalContextProvider? _wireRuntimeAdditionalContextProvider;
    private readonly IContextPageManager? _contextPageManager;
    private readonly ILogger<ChannelHost> _logger;

    private IAppServerTransport? _transport;
    private AppServerConnection? _connection;
    private AdapterToolBinding? _toolBinding;
    private AppServerConnection? _lastToolConnection;

    private ChannelPluginLaunch? _channelPluginLaunch;
    private readonly Lock _adapterProcessLock = new();
    private ManagedChildProcess? _adapterProcess;
    private Task? _adapterTerminationTask;
    private TaskCompletionSource? _runCompletion;
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    private TaskCompletionSource<(IAppServerTransport Transport, AppServerConnection Connection)>?
        _wsAttachTcs;

    private int _consecutiveFailures;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly int _maxConsecutiveFailures;

    private Timer? _heartbeatTimer;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(5);

    private volatile bool _stopped;
    private volatile bool _permanentlyFailed;
    private readonly Queue<string> _recentLogLines = new();
    private readonly object _recentLogLock = new();

    public ChannelHost(
        ChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        DotCraftPaths paths,
        ChatClientRegistry chatClientRegistry,
        ModelProviderRegistry modelProviderRegistry,
        PathBlacklist? pathBlacklist = null,
        IApprovalService? approvalService = null,
        Func<string, object>? deliveryDependenciesFactory = null,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null,
        IEnumerable<IThreadOriginPresentationProvider>? originPresentationProviders = null,
        ILoggerFactory? loggerFactory = null,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
        IContextPageManager? contextPageManager = null,
        DotCraft.Contributions.IContributionView? contributions = null,
        DotCraft.Commands.Core.CommandRegistry? commandRegistry = null)
        : this(
            config,
            sessionService,
            serverVersion,
            moduleRegistry,
            paths,
            chatClientRegistry,
            modelProviderRegistry,
            pathBlacklist,
            approvalService,
            deliveryDependenciesFactory,
            ManagedChildProcess.Start,
            streamDebugLogger,
            appConfigMonitor,
            protocolExtensions,
            appBindingService,
            originPresentationProviders,
            loggerFactory,
            wireRuntimeAdditionalContextProvider: wireRuntimeAdditionalContextProvider,
            contextPageManager: contextPageManager,
            contributions: contributions,
            commandRegistry: commandRegistry)
    {
    }

    internal ChannelHost(
        ChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        DotCraftPaths paths,
        ChatClientRegistry chatClientRegistry,
        ModelProviderRegistry modelProviderRegistry,
        Func<string, object>? deliveryDependenciesFactory,
        Func<ProcessStartInfo, ManagedChildProcess> managedChildProcessFactory,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null,
        IEnumerable<IThreadOriginPresentationProvider>? originPresentationProviders = null,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        int maxConsecutiveFailures = 5,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
        IContextPageManager? contextPageManager = null,
        DotCraft.Contributions.IContributionView? contributions = null,
        DotCraft.Commands.Core.CommandRegistry? commandRegistry = null)
        : this(
            config,
            sessionService,
            serverVersion,
            moduleRegistry,
            paths,
            chatClientRegistry,
            modelProviderRegistry,
            pathBlacklist: null,
            approvalService: null,
            deliveryDependenciesFactory,
            managedChildProcessFactory,
            streamDebugLogger,
            appConfigMonitor,
            protocolExtensions,
            appBindingService,
            originPresentationProviders,
            loggerFactory,
            initialBackoff,
            maxBackoff,
            maxConsecutiveFailures,
            wireRuntimeAdditionalContextProvider,
            contextPageManager,
            contributions,
            commandRegistry)
    {
    }

    internal ChannelHost(
        ChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        DotCraftPaths paths,
        ChatClientRegistry chatClientRegistry,
        ModelProviderRegistry modelProviderRegistry,
        PathBlacklist? pathBlacklist,
        IApprovalService? approvalService,
        Func<string, object>? deliveryDependenciesFactory,
        Func<ProcessStartInfo, ManagedChildProcess> managedChildProcessFactory,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null,
        IEnumerable<IThreadOriginPresentationProvider>? originPresentationProviders = null,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null,
        int maxConsecutiveFailures = 5,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
        IContextPageManager? contextPageManager = null,
        DotCraft.Contributions.IContributionView? contributions = null,
        DotCraft.Commands.Core.CommandRegistry? commandRegistry = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(sessionService);
        ArgumentNullException.ThrowIfNull(serverVersion);
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(paths);
        _hostWorkspacePath = paths.WorkspacePath;
        _delivery = CreateDeliveryDependencies(
            paths.WorkspaceTempPath,
            _hostWorkspacePath,
            pathBlacklist,
            approvalService,
            deliveryDependenciesFactory);
        _managedChildProcessFactory = managedChildProcessFactory ?? throw new ArgumentNullException(nameof(managedChildProcessFactory));
        _appConfigMonitor = appConfigMonitor;
        _threadAgentRefreshService = sessionService as IThreadAgentRefreshService;
        _wireRuntimeAdditionalContextProvider = wireRuntimeAdditionalContextProvider;
        _contextPageManager = contextPageManager;
        _logger = loggerFactory?.CreateLogger<ChannelHost>() ?? NullLogger<ChannelHost>.Instance;
        _requestHandlerFactory = new ChannelAdapterRequestHandlerFactory(
            sessionService,
            serverVersion,
            moduleRegistry,
            _hostWorkspacePath,
            chatClientRegistry ?? throw new ArgumentNullException(nameof(chatClientRegistry)),
            modelProviderRegistry ?? throw new ArgumentNullException(nameof(modelProviderRegistry)),
            streamDebugLogger,
            _appConfigMonitor,
            protocolExtensions?.ToArray() ?? [],
            appBindingService,
            originPresentationProviders?.ToArray() ?? [],
            loggerFactory,
            wireRuntimeAdditionalContextProvider,
            contributions,
            commandRegistry);
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(1);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(30);
        _maxConsecutiveFailures = maxConsecutiveFailures > 0
            ? maxConsecutiveFailures
            : throw new ArgumentOutOfRangeException(nameof(maxConsecutiveFailures));
    }

    public string Name => _config.Name;

    internal bool HasRunningProcess
    {
        get
        {
            ManagedChildProcess? managed;
            lock (_adapterProcessLock)
                managed = _adapterProcess;
            try { return !_stopped && managed is not null && !managed.Process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { return false; }
        }
    }

    internal void SetChannelPluginLaunch(ChannelPluginLaunch launch) => _channelPluginLaunch = launch;

    public bool IsAdapterConnected => !_stopped && !_permanentlyFailed
        && _connection is { IsClientReady: true };

    public string RuntimeState => _permanentlyFailed
        ? ChannelRuntimeStates.Failed
        : _stopped
            ? ChannelRuntimeStates.Stopped
            : IsAdapterConnected
                ? ChannelRuntimeStates.Running
                : ChannelRuntimeStates.Starting;

    public string? FailureCode => _permanentlyFailed
        ? ChannelFailureCodes.ChannelStartFailed
        : null;

    public AppServerConnection? AdapterConnection => _connection;

    AppServerConnection? IAdapterChannelToolRuntime.ChannelToolConnection =>
        Volatile.Read(ref _toolBinding)?.Connection;

    AppServerConnection? IAdapterChannelToolRuntime.LastChannelToolConnection =>
        Volatile.Read(ref _lastToolConnection);


    /// <summary>Adapters present approvals themselves.</summary>
    public IApprovalService? ApprovalService => null;

    public string DisplayName => _connection?.ChannelDisplayName is { Length: > 0 } displayName ? displayName : Name;

    public ChannelDeliveryCapabilitySnapshot? GetDeliveryCapabilities()
        => _connection is { IsClientReady: true } connection ? connection.DeliveryCapabilities : null;

    public ChannelChatDescriptor? GetThreadChat(string threadId)
        => _connection is { IsClosed: false } connection && connection.TryGetThreadChat(threadId, out var chat) ? chat : null;

    public IReadOnlyList<ChannelToolSpec> GetChannelTools()
        => _connection is { IsClientReady: true } connection
            ? connection.RegisteredChannelTools
            : [];

    /// <summary>Ready only after the adapter's handshake, so a Turn never starts without its channel tools.</summary>
    public bool IsReady => IsAdapterConnected;

    public IReadOnlyList<string> GetRecentLogs(int? tail = null)
    {
        lock (_recentLogLock)
        {
            var lines = _recentLogLines.ToList();
            if (tail is > 0 && tail.Value < lines.Count)
                return lines.Skip(lines.Count - tail.Value).ToArray();
            return lines;
        }
    }

    /// <summary>Runs connection cycles until stopped or cancelled.</summary>
    public async Task<ChannelDeliveryResult> DeliverAsync(
        string target,
        ChannelDeliveryMessage message,
        object? metadata = null,
        string? deliveryId = null,
        CancellationToken cancellationToken = default)
    {
        if (_stopped || _permanentlyFailed || _transport == null || _connection is not { IsClientReady: true } connection)
        {
            return new ChannelDeliveryResult
            {
                Delivered = false,
                ErrorCode = "AdapterDeliveryFailed",
                ErrorMessage = "Adapter is not connected."
            };
        }

        var result = await _delivery.MessageDispatcher.DeliverAsync(
            _transport,
            connection,
            Name,
            target,
            message,
            metadata,
            deliveryId ?? Guid.NewGuid().ToString("N"),
            cancellationToken);

        if (!result.Delivered)
        {
            _logger.LogWarning(
                "External channel delivery to {Target} failed: {ErrorCode} {ErrorMessage}",
                target,
                result.ErrorCode ?? "AdapterDeliveryFailed",
                result.ErrorMessage);
        }

        return result;
    }

    public Task<ChannelHistoryResult> ReadHistoryAsync(
        ChannelHistoryRequest request,
        CancellationToken cancellationToken = default)
        => _stopped || _permanentlyFailed || _transport is not { } transport || _connection is not { IsClientReady: true }
            ? Task.FromResult(ChannelHistoryResult.Fail("AdapterDisconnected", "Adapter is not connected."))
            : ChannelHistoryReader.ReadAsync(transport, request, cancellationToken);

    public async Task<ChannelToolInvocationResult> ExecuteToolAsync(
        ChannelToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = Volatile.Read(ref _toolBinding);
        if (binding == null)
        {
            return AdapterDisconnected();
        }

        return await ExecuteToolAsync(binding.Connection, request, cancellationToken);
    }

    async Task<ChannelToolInvocationResult> IAdapterChannelToolRuntime.ExecuteToolAsync(
        AppServerConnection expectedConnection,
        ChannelToolInvocationRequest request,
        CancellationToken cancellationToken)
        => await ExecuteToolAsync(expectedConnection, request, cancellationToken);

    private async Task<ChannelToolInvocationResult> ExecuteToolAsync(
        AppServerConnection expectedConnection,
        ChannelToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        var binding = Volatile.Read(ref _toolBinding);
        if (_stopped || _permanentlyFailed
            || binding == null
            || !ReferenceEquals(binding.Connection, expectedConnection))
        {
            return AdapterDisconnected();
        }

        try
        {
            var response = await binding.Transport.RequestAsync(
                Contract.AppServerRpc.ExtChannelToolCall,
                ChannelWireMapper.ToContract(request),
                cancellationToken,
                TimeSpan.FromSeconds(20));
            return ParseToolResult(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ChannelToolInvocationResult
            {
                Success = false,
                ErrorCode = "AdapterToolCallFailed",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(string key, CancellationToken cancellationToken)
    {
        var binding = Volatile.Read(ref _toolBinding);
        if (_stopped || _permanentlyFailed || binding == null)
            return null;

        try
        {
            var response = await binding.Transport.RequestAsync(
                Contract.AppServerRpc.ExtChannelConfigOptionAvailability,
                new Contract.ExtChannelConfigOptionAvailabilityParams { Key = key },
                cancellationToken,
                TimeSpan.FromSeconds(15));
            return response.Result?.Available;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External channel option availability failed");
            return null;
        }
    }

    private static ChannelToolInvocationResult AdapterDisconnected() => new()
    {
        Success = false,
        ErrorCode = "AdapterDisconnected",
        ErrorMessage = "Adapter is not connected."
    };

    /// <summary>Hands over a WebSocket adapter that already completed <c>initialize</c>.</summary>
    private static readonly SemaphoreSlim RequestGate = new(32, 32);

    private async Task RunMessageLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                AppServerIncomingMessage? msg;
                try
                {
                    msg = await transport.ReadMessageAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (msg == null)
                    break; // EOF — adapter disconnected

                if (msg.IsNotification)
                {
                    HandleNotification(msg, transport, connection, handler);
                    continue;
                }

                if (!msg.IsRequest)
                    continue;

                if (!await RequestGate.WaitAsync(0, ct))
                {
                    var overloadErr = AppServerErrors.ServerOverloaded().ToError();
                    await transport.WriteMessageAsync(
                        AppServerRequestHandler.BuildErrorResponse(msg.Id, overloadErr), ct);
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessRequestAsync(transport, handler, connection, msg, ct);
                    }
                    finally
                    {
                        RequestGate.Release();
                    }
                }, ct);
            }
        }
        finally
        {
            StopHeartbeatTimer();
            connection.MarkClosed();
            connection.CancelAllSubscriptions();
            if (_wireRuntimeAdditionalContextProvider != null)
            {
                foreach (var threadId in _wireRuntimeAdditionalContextProvider.UnbindTransport(transport))
                    _contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.RuntimeAdditionalContext());
            }
            ClearCurrentSession(connection);
        }
    }

    private async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerConnection connection,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousConnection = AppServerRequestContext.CurrentConnection;
        var previousCapabilities = SessionClientCapabilitiesScope.Current;
        AppServerRequestContext.CurrentTransport = transport;
        AppServerRequestContext.CurrentConnection = connection;
        SessionClientCapabilitiesScope.Current = new SessionClientCapabilities(
            connection.SupportsCommandExecutionStreaming,
            connection.SupportsToolExecutionLifecycle);
        try
        {
            object? result;
            try
            {
                result = await handler.HandleRequestAsync(msg, ct);
            }
            catch (AppServerException ex)
            {
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                var internalErr = AppServerErrors.InternalError(ex.Message).ToError();
                _logger.LogError(ex, "Unhandled external channel request failure");
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
                return;
            }

            // A null result means the handler already responded inline.
            if (result != null)
            {
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }
        }
        finally
        {
            AppServerRequestContext.CurrentTransport = previousTransport;
            AppServerRequestContext.CurrentConnection = previousConnection;
            SessionClientCapabilitiesScope.Current = previousCapabilities;
        }
    }

    private void HandleNotification(
        AppServerIncomingMessage msg,
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler)
    {
        if (handler.HandleNotification(msg) && msg.Method == DotCraft.Protocol.AppServer.AppServerRpc.Initialized.Name)
        {
            PublishToolBinding(transport, connection);
            StartHeartbeatTimer();
        }
    }

    private void PublishToolBinding(IAppServerTransport transport, AppServerConnection connection)
    {
        var next = new AdapterToolBinding(transport, connection);
        var previous = Interlocked.Exchange(ref _toolBinding, next);
        Volatile.Write(ref _lastToolConnection, connection);
        if (!ReferenceEquals(previous?.Connection, connection))
            _threadAgentRefreshService?.InvalidateThreadAgents();
    }

    private void ClearCurrentSession(AppServerConnection? connection)
    {
        if (connection == null)
            return;

        while (true)
        {
            var binding = Volatile.Read(ref _toolBinding);
            if (binding == null || !ReferenceEquals(binding.Connection, connection))
                break;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _toolBinding, null, binding), binding))
            {
                _threadAgentRefreshService?.InvalidateThreadAgents();
                break;
            }
        }

        if (ReferenceEquals(_connection, connection))
        {
            _connection = null;
            _transport = null;
        }
    }

    private static ChannelDeliveryDependencies CreateDeliveryDependencies(
        string workspaceTempPath,
        string hostWorkspacePath,
        PathBlacklist? pathBlacklist,
        IApprovalService? approvalService,
        Func<string, object>? deliveryDependenciesFactory)
    {
        if (deliveryDependenciesFactory?.Invoke(hostWorkspacePath) is ChannelDeliveryDependencies provided)
            return provided;

        var artifactStore = new InMemoryChannelMediaArtifactStore();
        var fileAccessGuard = new FileAccessGuard(
            hostWorkspacePath,
            requireApprovalOutsideWorkspace: true,
            approvalService,
            pathBlacklist);
        var resolver = new ChannelMediaResolver(artifactStore, workspaceTempPath, fileAccessGuard);
        var dispatcher = new ChannelMessageDispatcher(resolver, artifactStore);
        return new ChannelDeliveryDependencies(artifactStore, resolver, dispatcher);
    }

    private static ChannelToolInvocationResult ParseToolResult(
        AppServerTypedClientResponse<Contract.ExtChannelToolCallResult> response)
    {
        if (response.Result is not { } result)
        {
            return new ChannelToolInvocationResult
            {
                Success = false,
                ErrorCode = "AdapterProtocolViolation",
                ErrorMessage = "Adapter returned an invalid tool response payload."
            };
        }

        var parsed = ChannelWireMapper.FromContract(result);

        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorCode))
            parsed.ErrorCode = "AdapterToolCallFailed";
        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorMessage))
            parsed.ErrorMessage = "Adapter reported a failed tool call.";

        return parsed;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_adapterProcessLock)
            _disposed = true;

        await StopAsync();

        CancellationTokenSource? cts;
        lock (_adapterProcessLock)
        {
            cts = _runCts;
            _runCts = null;
        }
        cts?.Dispose();
    }
}
