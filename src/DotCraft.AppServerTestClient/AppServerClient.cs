using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppServerTestClient;

/// <summary>
/// JSON-RPC 2.0 client for the DotCraft AppServer stdio protocol.
/// Spawns <c>dotcraft app-server</c> as a child process, writes requests to its stdin
/// and reads responses and notifications from its stdout.
///
/// All protocol operations are delegated to <see cref="AppServerWireClient"/>, which
/// provides the full reusable wire protocol implementation. This class handles only the
/// subprocess lifecycle (spawn, kill, dispose).
///
/// Usage pattern:
/// <code>
/// await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
/// await client.InitializeAsync();
/// var thread = await client.SendRequestAsync("thread/start", params);
/// </code>
///
/// Server-initiated requests (e.g. <c>item/approval/request</c>) are dispatched through
/// <see cref="ServerRequestHandler"/> if set, or enqueued in the notification queue otherwise.
/// </summary>
public sealed class AppServerClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly Process _process;
    private readonly AppServerWireClient _wire;
    private bool _disposed;

    private AppServerClient(Process process)
    {
        _process = process;
        _wire = new AppServerWireClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
    }

    /// <summary>
    /// Optional handler for server-initiated JSON-RPC requests (messages with both
    /// <c>method</c> and <c>id</c>). The handler receives the full message document
    /// and returns a result object. The client sends a well-formed JSON-RPC response
    /// automatically using the same <c>id</c>.
    ///
    /// When null (default), server requests are placed in the notification queue so
    /// callers can handle them via <see cref="WaitForNotificationAsync"/>.
    /// </summary>
    public Func<JsonDocument, Task<object?>>? ServerRequestHandler
    {
        get => _wire.ServerRequestHandler;
        set => _wire.ServerRequestHandler = value;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns <c>dotcraft app-server</c> as a child process and starts the background reader.
    /// </summary>
    /// <param name="dotcraftBin">Path to the <c>dotcraft</c> executable.</param>
    /// <param name="workspacePath">Workspace path for the subprocess. Defaults to current directory.</param>
    public static Task<AppServerClient> SpawnAsync(
        string dotcraftBin,
        string? workspacePath = null)
    {
        var psi = new ProcessStartInfo(dotcraftBin, "app-server")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        if (workspacePath != null)
            psi.WorkingDirectory = workspacePath;

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {dotcraftBin} app-server");

        var client = new AppServerClient(process);
        client._wire.Start();
        return Task.FromResult(client);
    }

    // -------------------------------------------------------------------------
    // Protocol helpers (delegated to AppServerWireClient)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs the full initialize → initialized handshake.
    /// Returns the server's initialize response.
    /// </summary>
    public Task<JsonDocument> InitializeAsync(
        bool approvalSupport = true,
        bool streamingSupport = true,
        List<string>? optOutMethods = null) =>
        _wire.InitializeAsync(
            clientName: "dotcraft-test-client",
            clientVersion: "0.1.0",
            approvalSupport: approvalSupport,
            streamingSupport: streamingSupport,
            optOutMethods: optOutMethods);

    /// <summary>
    /// Streams notifications for the given turn until <c>turn/completed</c>,
    /// <c>turn/failed</c>, or <c>turn/cancelled</c> is received.
    /// Each notification is passed to <paramref name="onNotification"/>.
    /// </summary>
    public async Task StreamTurnAsync(
        string threadId,
        string turnId,
        Action<JsonDocument>? onNotification = null,
        TimeSpan? timeout = null)
    {
        await foreach (var notif in _wire.ReadTurnNotificationsAsync(timeout))
        {
            onNotification?.Invoke(notif);
        }
    }

    /// <summary>Sends a JSON-RPC request and awaits the response.</summary>
    public Task<JsonDocument> SendRequestAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default) =>
        _wire.SendRequestAsync(method, @params, timeout, ct);

    /// <summary>Sends a JSON-RPC notification (no id, no response expected).</summary>
    public Task SendNotificationAsync(string method, object? @params = null) =>
        _wire.SendNotificationAsync(method, @params);

    /// <summary>
    /// Waits for the next notification matching <paramref name="method"/> (null means any).
    /// Returns null on timeout.
    /// </summary>
    public Task<JsonDocument?> WaitForNotificationAsync(string? method = null, TimeSpan? timeout = null) =>
        _wire.WaitForNotificationAsync(method, timeout);

    /// <summary>Sends a JSON-RPC response to a server-initiated request.</summary>
    public Task SendResponseAsync(JsonElement requestId, object? result)
    {
        if (requestId.ValueKind != JsonValueKind.Number)
            throw new ArgumentException("Request id must be a numeric JSON element.", nameof(requestId));
        return _wire.SendResponseAsync(requestId.GetInt32(), result);
    }

    /// <summary>Sends an approval response back to the server.</summary>
    public Task SendApprovalResponseAsync(JsonElement requestId, string decision) =>
        SendResponseAsync(requestId, new { decision });

    /// <summary>
    /// Closes the stdio connection and gives the AppServer process time to flush
    /// persisted state before falling back to a forced process-tree kill.
    /// </summary>
    public async Task StopAsync(TimeSpan? gracefulTimeout = null)
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await _wire.DisposeAsync();
        }
        finally
        {
            if (!_process.HasExited)
            {
                var timeout = gracefulTimeout ?? DefaultShutdownTimeout;
                var exited = _process.WaitForExitAsync();
                if (await Task.WhenAny(exited, Task.Delay(timeout)) == exited)
                {
                    await exited;
                }
                else
                {
                    try { _process.Kill(entireProcessTree: true); } catch { }
                }
            }

            _process.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync() => await StopAsync();
}
