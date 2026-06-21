using System.Diagnostics;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Modules;
using DotCraft.Processes;
using DotCraft.Protocol.AppServer;
using DotCraft.Protocol;
using DotCraft.Logging;
using DotCraft.Security;
using Spectre.Console;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Bridge component that wraps a Wire Protocol connection to an external adapter process,
/// exposing it as an <see cref="IChannelService"/> to GatewayHost.
/// <para>
/// For subprocess mode, manages the adapter process lifecycle (spawn, monitor, restart with backoff).
/// For WebSocket mode, waits for the adapter to connect and attach its transport via
/// <see cref="AttachTransport"/>. For managed WebSocket mode, manages the adapter process
/// lifecycle while the adapter connects back over the AppServer WebSocket endpoint.
/// </para>
/// </summary>
public sealed class ExternalChannelHost : IChannelService, IChannelToolRegistrationSource
{
    private const int MaxLogLines = 200;
    private const string DotCraftNodeBinEnv = "DOTCRAFT_NODE_BIN";
    private const string DotCraftNodeRunAsNodeEnv = "DOTCRAFT_NODE_RUN_AS_NODE";
    private const string DotCraftModulesDirEnv = "DOTCRAFT_MODULES_DIR";
    private const string DotCraftChannelTransportEnv = "DOTCRAFT_CHANNEL_TRANSPORT";
    private const string DotCraftChannelWebSocketUrlEnv = "DOTCRAFT_CHANNEL_WS_URL";
    private const string DotCraftChannelWebSocketTokenEnv = "DOTCRAFT_CHANNEL_WS_TOKEN";

    private readonly ExternalChannelEntry _config;
    private readonly ISessionService _sessionService;
    private readonly string _serverVersion;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly string _hostWorkspacePath;
    private readonly string _workspaceCraftPath;
    private readonly ExternalChannelDeliveryDependencies _delivery;
    private readonly Func<ProcessStartInfo, ManagedChildProcess> _managedChildProcessFactory;
    private readonly SessionStreamDebugLogger? _streamDebugLogger;
    private readonly IAppConfigMonitor? _appConfigMonitor;
    private readonly IReadOnlyList<IAppServerProtocolExtension> _protocolExtensions;
    private readonly AppBindingService? _appBindingService;

    // Current transport/connection/handler — replaced on restart or reconnect
    private IAppServerTransport? _transport;
    private AppServerConnection? _connection;
    private AppServerRequestHandler? _handler;

    // Subprocess management
    private ManagedChildProcess? _adapterProcess;
    private CancellationTokenSource? _runCts;

    // WebSocket mode: signaled when an adapter attaches via AppServerHost
    private TaskCompletionSource<(IAppServerTransport Transport, AppServerConnection Connection)>?
        _wsAttachTcs;

    // Restart backoff
    private int _consecutiveFailures;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private const int MaxConsecutiveFailures = 5;

    // Heartbeat
    private Timer? _heartbeatTimer;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(5);

    // State
    private volatile bool _stopped;
    private volatile bool _permanentlyFailed;
    private readonly Queue<string> _recentLogLines = new();
    private readonly object _recentLogLock = new();

    public ExternalChannelHost(
        ExternalChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        string hostWorkspacePath,
        PathBlacklist? pathBlacklist = null,
        IApprovalService? approvalService = null,
        Func<string, object>? deliveryDependenciesFactory = null,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null)
        : this(
            config,
            sessionService,
            serverVersion,
            moduleRegistry,
            hostWorkspacePath,
            pathBlacklist,
            approvalService,
            deliveryDependenciesFactory,
            ManagedChildProcess.Start,
            streamDebugLogger,
            appConfigMonitor,
            protocolExtensions,
            appBindingService)
    {
    }

    internal ExternalChannelHost(
        ExternalChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        string hostWorkspacePath,
        Func<string, object>? deliveryDependenciesFactory,
        Func<ProcessStartInfo, ManagedChildProcess> managedChildProcessFactory,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null)
        : this(
            config,
            sessionService,
            serverVersion,
            moduleRegistry,
            hostWorkspacePath,
            pathBlacklist: null,
            approvalService: null,
            deliveryDependenciesFactory,
            managedChildProcessFactory,
            streamDebugLogger,
            appConfigMonitor,
            protocolExtensions,
            appBindingService)
    {
    }

    internal ExternalChannelHost(
        ExternalChannelEntry config,
        ISessionService sessionService,
        string serverVersion,
        ModuleRegistry moduleRegistry,
        string hostWorkspacePath,
        PathBlacklist? pathBlacklist,
        IApprovalService? approvalService,
        Func<string, object>? deliveryDependenciesFactory,
        Func<ProcessStartInfo, ManagedChildProcess> managedChildProcessFactory,
        SessionStreamDebugLogger? streamDebugLogger = null,
        IAppConfigMonitor? appConfigMonitor = null,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        AppBindingService? appBindingService = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _serverVersion = serverVersion ?? throw new ArgumentNullException(nameof(serverVersion));
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _hostWorkspacePath = hostWorkspacePath ?? throw new ArgumentNullException(nameof(hostWorkspacePath));
        _workspaceCraftPath = Path.Combine(_hostWorkspacePath, ".craft");
        _delivery = CreateDeliveryDependencies(_hostWorkspacePath, pathBlacklist, approvalService, deliveryDependenciesFactory);
        _managedChildProcessFactory = managedChildProcessFactory ?? throw new ArgumentNullException(nameof(managedChildProcessFactory));
        _streamDebugLogger = streamDebugLogger;
        _appConfigMonitor = appConfigMonitor;
        _protocolExtensions = protocolExtensions?.ToArray() ?? [];
        _appBindingService = appBindingService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IChannelService implementation
    // ─────────────────────────────────────────────────────────────────────────

    public string Name => _config.Name;

    /// <summary>
    /// Configured transport mode (subprocess vs WebSocket). Used to reject WebSocket adapter
    /// handover when the channel is subprocess-only.
    /// </summary>
    public ExternalChannelTransport Transport => _config.Transport;

    /// <summary>
    /// Whether this host may receive a WebSocket <c>/ws</c> adapter connection via
    /// <see cref="AttachTransport"/>. Plain subprocess channels use stdio only.
    /// </summary>
    public bool AcceptsWebSocketAdapterAttach =>
        _config.Transport is ExternalChannelTransport.Websocket or ExternalChannelTransport.ManagedWebsocket;

    /// <summary>
    /// Returns <c>true</c> when an adapter transport is attached and has completed the
    /// wire handshake (<c>initialized</c> notification received). Used by
    /// <see cref="IChannelStatusProvider"/> to determine the <c>running</c> flag.
    /// </summary>
    public bool IsAdapterConnected => !_stopped && !_permanentlyFailed
        && _connection is { IsClientReady: true };

    /// <summary>
    /// Current adapter connection snapshot, when attached.
    /// </summary>
    public AppServerConnection? AdapterConnection => _connection;

    AppServerConnection? IChannelToolRegistrationSource.ChannelToolRegistrationConnection => _connection;

    public HeartbeatService? HeartbeatService { get; set; }

    public CronService? CronService { get; set; }

    /// <summary>
    /// External channels handle approval end-to-end via Wire Protocol.
    /// No server-side approval service is needed.
    /// </summary>
    public IApprovalService? ApprovalService => null;

    public ChannelDeliveryCapabilities? GetDeliveryCapabilities()
        => _connection?.DeliveryCapabilities;

    public IReadOnlyList<ChannelToolDescriptor> GetChannelTools()
        => _connection is { IsClientReady: true } connection
            ? connection.RegisteredChannelTools
            : [];

    /// <summary>
    /// External channels are "ready" only after the adapter has connected and completed
    /// the <c>initialize</c> / <c>initialized</c> handshake (so <see cref="AppServerConnection.IsClientReady"/>
    /// is true and declared tools are settled). Before that, turn execution against this
    /// channel would miss channel-native tools.
    /// </summary>
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

    /// <summary>
    /// Starts the external channel adapter.
    /// For subprocess mode, spawns the process and enters the message loop.
    /// For WebSocket mode, waits for the adapter to connect and then enters the message loop.
    /// Blocks until stopped or canceled.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] Starting external channel [yellow]{Name}[/] ({_config.Transport})");

        try
        {
            while (!ct.IsCancellationRequested && !_permanentlyFailed)
            {
                try
                {
                    if (_config.Transport == ExternalChannelTransport.Subprocess)
                    {
                        await RunSubprocessCycleAsync(ct);
                    }
                    else if (_config.Transport == ExternalChannelTransport.ManagedWebsocket)
                    {
                        await RunManagedWebSocketCycleAsync(ct);
                    }
                    else
                    {
                        await RunWebSocketCycleAsync(ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;

                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _permanentlyFailed = true;
                        AnsiConsole.MarkupLine(
                            $"[red][[ExternalChannel]][/] Channel [yellow]{Name}[/] permanently failed " +
                            $"after {_consecutiveFailures} consecutive failures: {ex.Message}");
                        break;
                    }

                    var backoff = CalculateBackoff(_consecutiveFailures);
                    AnsiConsole.MarkupLine(
                        $"[yellow][[ExternalChannel]][/] Channel [yellow]{Name}[/] failed " +
                        $"(attempt {_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}. " +
                        $"Retrying in {backoff.TotalSeconds:F0}s...");

                    await Task.Delay(backoff, ct);
                }
            }
        }
        finally
        {
            _stopped = true;
            StopHeartbeatTimer();
            AnsiConsole.MarkupLine(
                $"[grey][[ExternalChannel]][/] Channel [yellow]{Name}[/] stopped");
        }
    }

    public async Task StopAsync()
    {
        _stopped = true;
        StopHeartbeatTimer();

        // Cancel the run loop
        if (_runCts is { } cts)
        {
            await cts.CancelAsync();
        }

        // Kill subprocess if running
        await TerminateSubprocessAsync();

        // Cancel WebSocket attach waiters
        _wsAttachTcs?.TrySetCanceled();

        // Clean up connection subscriptions
        _connection?.CancelAllSubscriptions();

        // Dispose transport
        if (_transport is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    public async Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_stopped || _permanentlyFailed || _transport == null || _connection is not { IsClientReady: true } connection)
        {
            return new ExtChannelSendResult
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
            cancellationToken);

        if (!result.Delivered)
        {
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Delivery to [yellow]{Name}[/] target '{target}' failed: " +
                $"{result.ErrorCode ?? "AdapterDeliveryFailed"} {result.ErrorMessage}");
        }

        return result;
    }

    public async Task<ExtChannelToolCallResult> ExecuteToolAsync(
        ExtChannelToolCallParams request,
        CancellationToken cancellationToken = default)
    {
        if (_stopped || _permanentlyFailed || _transport == null || _connection is not { IsClientReady: true })
        {
            return new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "AdapterDisconnected",
                ErrorMessage = "Adapter is not connected."
            };
        }

        try
        {
            var response = await _transport.SendClientRequestAsync(
                AppServerMethods.ExtChannelToolCall,
                request,
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
            return new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "AdapterToolCallFailed",
                ErrorMessage = ex.Message
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WebSocket mode: transport attachment
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="AppServer.AppServerHost"/> when a WebSocket client completes the
    /// <c>initialize</c> handshake with a matching <c>channelAdapter.channelName</c>.
    /// The transport and connection are handed over to this host, which takes over
    /// the message loop.
    /// </summary>
    public void AttachTransport(IAppServerTransport transport, AppServerConnection connection)
    {
        _wsAttachTcs?.TrySetResult((transport, connection));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Subprocess cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunSubprocessCycleAsync(CancellationToken ct)
    {
        // Spawn the adapter process
        var adapterProcess = SpawnAdapterProcess();
        _adapterProcess = adapterProcess;
        var process = adapterProcess.Process;

        // Create transport from process streams
        // Note: StdioTransport reads from the process's stdout (our input),
        //       and writes to the process's stdin (our output).
        await using var transport = StdioTransport.Create(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
        transport.Start();

        _transport = transport;
        _connection = new AppServerConnection();
        _handler = new AppServerRequestHandler(
            _sessionService, _connection, transport,
            new ModuleRegistryChannelListContributor(_moduleRegistry, CronService, HeartbeatService),
            new AppServerConnectionServices
            {
                ServerVersion = _serverVersion,
                CronService = CronService,
                HeartbeatService = HeartbeatService,
                WorkspaceCraftPath = _workspaceCraftPath,
                HostWorkspacePath = _hostWorkspacePath,
                StreamDebugLogger = _streamDebugLogger,
                ConfigSchema = ConfigSchemaRegistrations.GetConfigSchema(),
                AppConfigMonitor = _appConfigMonitor,
                ProtocolExtensions = _protocolExtensions,
                AppBindingService = _appBindingService,
            });

        // Forward stderr to DotCraft's diagnostic log
        _ = ForwardStderrAsync(process, ct);

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] Adapter [yellow]{Name}[/] spawned (PID {process.Id})");

        // Run the message loop
        await RunMessageLoopAsync(transport, _connection, _handler, ct);

        // Capture exit status before disposal. TerminateSubprocessAsync disposes the
        // underlying Process via ManagedChildProcess.DisposeAsync.
        var exitedBeforeTerminate = !ct.IsCancellationRequested && process.HasExited;
        int? exitCodeBeforeTerminate = null;
        if (exitedBeforeTerminate)
            exitCodeBeforeTerminate = process.ExitCode;

        // Terminate the subprocess after the message loop exits.
        // When the loop exits due to heartbeat-timeout (transport disposed), the process
        // may still be running. Kill it first to avoid hanging on WaitForExitAsync.
        await TerminateSubprocessAsync();

        // Process exited before termination — check if it was expected.
        if (exitedBeforeTerminate && exitCodeBeforeTerminate is { } exitCode)
        {
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Adapter [yellow]{Name}[/] exited with code {exitCode}");

            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Adapter process exited with code {exitCode}");
        }

        // Reset on success
        _consecutiveFailures = 0;
    }

    private ManagedChildProcess SpawnAdapterProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(_config.BuiltinModule))
        {
            ConfigureBuiltInTypeScriptAdapter(startInfo, _config.BuiltinModule);
        }
        else
        {
            startInfo.FileName = _config.Command!;

            if (_config.Args is { Count: > 0 })
            {
                foreach (var arg in _config.Args)
                    startInfo.ArgumentList.Add(arg);
            }
        }

        if (string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
            startInfo.WorkingDirectory = _hostWorkspacePath;

        if (_config.Env is { Count: > 0 })
        {
            foreach (var (key, value) in _config.Env)
                startInfo.Environment[key] = value;
        }

        ApplyChannelTransportEnvironment(startInfo);

        if (!string.IsNullOrEmpty(_config.WorkingDirectory))
            startInfo.WorkingDirectory = _config.WorkingDirectory;

        return _managedChildProcessFactory(startInfo);
    }

    private void ConfigureBuiltInTypeScriptAdapter(ProcessStartInfo startInfo, string moduleName)
    {
        if (moduleName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || moduleName.Contains(Path.DirectorySeparatorChar)
            || moduleName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException($"Invalid built-in TypeScript channel module name: {moduleName}");
        }

        var nodeBin = Environment.GetEnvironmentVariable(DotCraftNodeBinEnv);
        var modulesDir = Environment.GetEnvironmentVariable(DotCraftModulesDirEnv);
        if (string.IsNullOrWhiteSpace(nodeBin) || !File.Exists(nodeBin))
        {
            throw new InvalidOperationException(
                "Built-in TypeScript channel runtime is unavailable. Launch DotCraft Desktop once, " +
                $"or configure Hub runtime with '{DotCraftNodeBinEnv}'.");
        }

        if (string.IsNullOrWhiteSpace(modulesDir) || !Directory.Exists(modulesDir))
        {
            throw new InvalidOperationException(
                "Built-in TypeScript channel modules are unavailable. Launch DotCraft Desktop once, " +
                $"or configure Hub runtime with '{DotCraftModulesDirEnv}'.");
        }

        var moduleDir = Path.Combine(modulesDir, moduleName);
        var cliPath = Path.Combine(moduleDir, "dist", "cli.bundle.js");
        if (!File.Exists(cliPath))
            cliPath = Path.Combine(moduleDir, "dist", "cli.js");
        if (!File.Exists(cliPath))
            throw new InvalidOperationException($"Built-in TypeScript channel CLI not found: {moduleDir}");

        startInfo.FileName = nodeBin;
        startInfo.ArgumentList.Add(cliPath);
        startInfo.ArgumentList.Add("--workspace");
        startInfo.ArgumentList.Add(_hostWorkspacePath);

        if (string.Equals(
                Environment.GetEnvironmentVariable(DotCraftNodeRunAsNodeEnv),
                "1",
                StringComparison.Ordinal))
        {
            startInfo.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        }
    }

    private void ApplyChannelTransportEnvironment(ProcessStartInfo startInfo)
    {
        if (_config.Transport == ExternalChannelTransport.Subprocess)
        {
            startInfo.Environment[DotCraftChannelTransportEnv] = "stdio";
            return;
        }

        if (_config.Transport == ExternalChannelTransport.ManagedWebsocket)
        {
            var (wsUrl, token) = ResolveManagedWebSocketEndpoint();
            startInfo.Environment[DotCraftChannelTransportEnv] = "websocket";
            startInfo.Environment[DotCraftChannelWebSocketUrlEnv] = wsUrl;
            startInfo.Environment[DotCraftChannelWebSocketTokenEnv] = token ?? string.Empty;
        }
    }

    private (string WsUrl, string? Token) ResolveManagedWebSocketEndpoint()
    {
        var appServerConfig = _appConfigMonitor?.Current.GetSection<AppServerConfig>("AppServer")
            ?? throw new InvalidOperationException(
                "Managed WebSocket channels require an AppServer configuration monitor.");

        if (appServerConfig.Mode is not (AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket))
            throw new InvalidOperationException(
                "Managed WebSocket channels require AppServer WebSocket mode.");

        var wsConfig = appServerConfig.WebSocket;
        if (wsConfig.Port <= 0)
            throw new InvalidOperationException(
                "Managed WebSocket channels require a valid AppServer WebSocket port.");

        return (BuildManagedWebSocketUrl(wsConfig.Host, wsConfig.Port), wsConfig.Token);
    }

    private static string BuildManagedWebSocketUrl(string host, int port)
    {
        var connectHost = host.Trim();
        if (string.IsNullOrWhiteSpace(connectHost) || connectHost == "0.0.0.0")
            connectHost = "127.0.0.1";
        else if (connectHost == "::")
            connectHost = "::1";
        if (connectHost.Contains(':') && !connectHost.StartsWith('['))
            connectHost = $"[{connectHost}]";
        return $"ws://{connectHost}:{port}/ws";
    }

    private void AppendLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        var stamped = $"{DateTimeOffset.UtcNow:O} {line}";
        lock (_recentLogLock)
        {
            _recentLogLines.Enqueue(stamped);
            while (_recentLogLines.Count > MaxLogLines)
                _recentLogLines.Dequeue();
        }
    }

    private async Task ForwardStderrAsync(Process process, CancellationToken ct)
        => await ForwardProcessOutputAsync(process.StandardError, ct);

    private async Task ForwardProcessOutputAsync(TextReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null)
                    break;
                AppendLogLine(line);
                await Console.Error.WriteLineAsync(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* output forwarding is best-effort */ }
    }

    private async Task TerminateSubprocessAsync()
    {
        if (_adapterProcess is not { } adapterProcess)
            return;

        _adapterProcess = null;

        try
        {
            await adapterProcess.DisposeAsync();
        }
        catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WebSocket cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunWebSocketCycleAsync(CancellationToken ct)
    {
        // Wait for an adapter to connect and attach its transport
        _wsAttachTcs = new TaskCompletionSource<(IAppServerTransport, AppServerConnection)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        AnsiConsole.MarkupLine(
            $"[grey][[ExternalChannel]][/] Waiting for WebSocket adapter [yellow]{Name}[/] to connect...");

        var (transport, connection) = await _wsAttachTcs.Task.WaitAsync(ct);

        _transport = transport;
        _connection = connection;
        _handler = new AppServerRequestHandler(
            _sessionService, connection, transport,
            new ModuleRegistryChannelListContributor(_moduleRegistry, CronService, HeartbeatService),
            new AppServerConnectionServices
            {
                ServerVersion = _serverVersion,
                CronService = CronService,
                HeartbeatService = HeartbeatService,
                WorkspaceCraftPath = _workspaceCraftPath,
                HostWorkspacePath = _hostWorkspacePath,
                StreamDebugLogger = _streamDebugLogger,
                ConfigSchema = ConfigSchemaRegistrations.GetConfigSchema(),
                AppConfigMonitor = _appConfigMonitor,
                ProtocolExtensions = _protocolExtensions,
                AppBindingService = _appBindingService,
            });

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] WebSocket adapter [yellow]{Name}[/] connected " +
            $"(client: {connection.ClientInfo?.Name ?? "unknown"})");

        // The initialize handshake was already completed by AppServerHost before routing here,
        // and the 'initialized' notification has also been consumed. Start heartbeat probing
        // explicitly since it won't be triggered via HandleNotification in WebSocket mode.
        StartHeartbeatTimer();
        await RunMessageLoopAsync(transport, connection, _handler, ct);

        // Connection closed — reset for next connection
        _consecutiveFailures = 0;
    }

    private async Task RunManagedWebSocketCycleAsync(CancellationToken ct)
    {
        _wsAttachTcs = new TaskCompletionSource<(IAppServerTransport, AppServerConnection)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var adapterProcess = SpawnAdapterProcess();
        _adapterProcess = adapterProcess;
        var process = adapterProcess.Process;

        _ = ForwardProcessOutputAsync(process.StandardOutput, ct);
        _ = ForwardProcessOutputAsync(process.StandardError, ct);

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] Managed WebSocket adapter [yellow]{Name}[/] spawned (PID {process.Id})");

        try
        {
            var attachTask = _wsAttachTcs.Task;
            var exitTask = process.WaitForExitAsync(ct);
            var completed = await Task.WhenAny(attachTask, exitTask);
            if (completed == exitTask)
            {
                await exitTask;
                throw new InvalidOperationException(
                    $"Managed WebSocket adapter process exited before attach (exit code {process.ExitCode}).");
            }

            var (transport, connection) = await attachTask;

            _transport = transport;
            _connection = connection;
            _handler = new AppServerRequestHandler(
                _sessionService, connection, transport,
                new ModuleRegistryChannelListContributor(_moduleRegistry, CronService, HeartbeatService),
                new AppServerConnectionServices
                {
                    ServerVersion = _serverVersion,
                    CronService = CronService,
                    HeartbeatService = HeartbeatService,
                    WorkspaceCraftPath = _workspaceCraftPath,
                    HostWorkspacePath = _hostWorkspacePath,
                    StreamDebugLogger = _streamDebugLogger,
                    ConfigSchema = ConfigSchemaRegistrations.GetConfigSchema(),
                    AppConfigMonitor = _appConfigMonitor,
                    ProtocolExtensions = _protocolExtensions,
                    AppBindingService = _appBindingService,
                });

            AnsiConsole.MarkupLine(
                $"[green][[ExternalChannel]][/] Managed WebSocket adapter [yellow]{Name}[/] connected " +
                $"(client: {connection.ClientInfo?.Name ?? "unknown"})");

            StartHeartbeatTimer();
            var messageLoopTask = RunMessageLoopAsync(transport, connection, _handler, ct);
            var completedAfterAttach = await Task.WhenAny(messageLoopTask, exitTask);
            if (completedAfterAttach == exitTask)
            {
                await exitTask;
                await transport.DisposeAsync();
            }

            await messageLoopTask;

            var exitedBeforeTerminate = !ct.IsCancellationRequested && process.HasExited;
            int? exitCodeBeforeTerminate = exitedBeforeTerminate ? process.ExitCode : null;

            await TerminateSubprocessAsync();

            if (exitedBeforeTerminate && exitCodeBeforeTerminate is { } exitCode)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow][[ExternalChannel]][/] Managed WebSocket adapter [yellow]{Name}[/] exited with code {exitCode}");

                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"Managed WebSocket adapter process exited with code {exitCode}");
            }

            _consecutiveFailures = 0;
        }
        finally
        {
            _wsAttachTcs = null;
            StopHeartbeatTimer();
            _connection?.CancelAllSubscriptions();
            _connection = null;
            _handler = null;
            _transport = null;
            await TerminateSubprocessAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Message loop (shared between subprocess and WebSocket modes)
    // ─────────────────────────────────────────────────────────────────────────

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
                    HandleNotification(msg, handler);
                    continue;
                }

                if (!msg.IsRequest)
                    continue;

                // Reject if at capacity
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
            connection.CancelAllSubscriptions();
        }
    }

    private static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerConnection connection,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousConnection = AppServerRequestContext.CurrentConnection;
        AppServerRequestContext.CurrentTransport = transport;
        AppServerRequestContext.CurrentConnection = connection;
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
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
                await Console.Error.WriteLineAsync(
                    $"[ExternalChannel:{handler}] Internal error: {ex}");
                return;
            }

            // null result means the handler already sent the response inline (turn/start)
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
        }
    }

    private void HandleNotification(AppServerIncomingMessage msg, AppServerRequestHandler handler)
    {
        switch (msg.Method)
        {
            case AppServerMethods.Initialized:
                handler.HandleInitializedNotification();
                // Start heartbeat probing after adapter is ready
                StartHeartbeatTimer();
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Heartbeat
    // ─────────────────────────────────────────────────────────────────────────

    private void StartHeartbeatTimer()
    {
        StopHeartbeatTimer();
        _heartbeatTimer = new Timer(
            __ => _ = SendHeartbeatAsync(),
            state: null,
            dueTime: HeartbeatInterval,
            period: HeartbeatInterval);
    }

    private void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task SendHeartbeatAsync()
    {
        if (_stopped || _transport == null || _connection is not { IsClientReady: true })
            return;

        try
        {
            await _transport.SendClientRequestAsync(
                AppServerMethods.ExtChannelHeartbeat,
                new { },
                timeout: HeartbeatTimeout);

            // Heartbeat succeeded — connection is healthy
        }
        catch (OperationCanceledException) when (!_stopped && _runCts is { IsCancellationRequested: false })
        {
            // SendClientRequestAsync uses CancellationTokenSource.CancelAfter() for timeouts,
            // which throws TaskCanceledException (a subclass of OperationCanceledException).
            // If neither _stopped nor _runCts is canceled, this is a heartbeat timeout.
            AnsiConsole.MarkupLine(
                $"[red][[ExternalChannel]][/] Heartbeat timeout for [yellow]{Name}[/] — " +
                "connection unhealthy, triggering reconnect");

            // Dispose the transport to trigger reconnect.
            // This causes ReadMessageAsync to return null, exiting RunMessageLoopAsync normally.
            // The StartAsync while-loop then retries the cycle.
            // NOTE: Do NOT cancel _runCts here — that would exit the while-loop permanently.
            if (_transport is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Heartbeat error for [yellow]{Name}[/]: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backoff
    // ─────────────────────────────────────────────────────────────────────────

    private static TimeSpan CalculateBackoff(int failures)
    {
        var seconds = Math.Min(
            InitialBackoff.TotalSeconds * Math.Pow(2, failures - 1),
            MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static ExternalChannelDeliveryDependencies CreateDeliveryDependencies(
        string hostWorkspacePath,
        PathBlacklist? pathBlacklist,
        IApprovalService? approvalService,
        Func<string, object>? deliveryDependenciesFactory)
    {
        if (deliveryDependenciesFactory?.Invoke(hostWorkspacePath) is ExternalChannelDeliveryDependencies provided)
            return provided;

        var mediaRoot = Path.Combine(hostWorkspacePath, ".craft", "external-channel-media");
        var artifactStore = new FileSystemChannelMediaArtifactStore(mediaRoot);
        var fileAccessGuard = new FileAccessGuard(
            hostWorkspacePath,
            requireApprovalOutsideWorkspace: true,
            approvalService,
            pathBlacklist);
        var resolver = new ChannelMediaResolver(artifactStore, Path.Combine(mediaRoot, "tmp"), fileAccessGuard);
        var dispatcher = new ExternalChannelMessageDispatcher(resolver, artifactStore);
        return new ExternalChannelDeliveryDependencies(artifactStore, resolver, dispatcher);
    }

    private static ExtChannelToolCallResult ParseToolResult(AppServerIncomingMessage response)
    {
        if (response.Result is not { } result || result.ValueKind != JsonValueKind.Object)
        {
            return new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "AdapterProtocolViolation",
                ErrorMessage = "Adapter returned an invalid tool response payload."
            };
        }

        var parsed = JsonSerializer.Deserialize<ExtChannelToolCallResult>(
            result.GetRawText(),
            SessionWireJsonOptions.Default);
        if (parsed == null)
        {
            return new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "AdapterProtocolViolation",
                ErrorMessage = "Adapter returned an empty tool response payload."
            };
        }

        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorCode))
            parsed.ErrorCode = "AdapterToolCallFailed";
        if (!parsed.Success && string.IsNullOrWhiteSpace(parsed.ErrorMessage))
            parsed.ErrorMessage = "Adapter reported a failed tool call.";

        return parsed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAsyncDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _runCts?.Dispose();
    }
}
