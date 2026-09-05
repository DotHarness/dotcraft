using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.RemoteTools;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Workspaces;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.Tests.Tools;

internal static class RemoteToolHostTestHost
{
    public static RemoteToolHostState Setup(
        RemoteToolHostStorage storage,
        IReadOnlyDictionary<string, string> workspaces,
        string hostId = "rth_test")
    {
        var state = new RemoteToolHostState
        {
            HostId = hostId,
            DisplayName = "test-host",
            Workspaces = new Dictionary<string, string>(workspaces, StringComparer.Ordinal)
        };
        storage.SaveHostState(state);
        return state;
    }

    /// <summary>Builds the Agent-side mirror of the workspace execution tools.</summary>
    public static async Task<IReadOnlyList<ToolRegistration>> AgentRegistrationsAsync(
        string workspacePath,
        string agentDataPath,
        bool enableLsp = false)
    {
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = enableLsp;
        await using var terminals = new BackgroundTerminalService(
            Path.Combine(agentDataPath, "agent-terminals"),
            config.Tools.Shell.Background);
        var lsp = enableLsp
            ? new LspServerManager(
                config,
                DotCraftPaths.CreateForExecutionHost(workspacePath, agentDataPath, agentDataPath))
            : null;
        try
        {
            var source = new WorkspaceExecutionToolSource(config, terminals, lspServerManager: lsp);
            return await source.GetRegistrationsAsync(new ToolPlanningContext(
                "agent-thread",
                null,
                workspacePath,
                agentDataPath,
                "agent",
                null,
                [],
                1,
                workspaceRoots: [workspacePath]));
        }
        finally
        {
            if (lsp is not null)
                await lsp.DisposeAsync();
        }
    }

    public static void WriteConfig(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>
/// Runs the real Remote Tool Host MCP server over the outbound stream transport, with a loopback
/// socket standing in for the Hub-brokered data connection.
/// </summary>
internal sealed class RemoteToolHostTestServer : IAsyncDisposable
{
    private readonly RemoteToolHostStorage _storage;
    private readonly LeaseTerminalRegistry _terminals = new();
    private readonly RemoteToolHostMcpHandlers _handlers;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _sessions = [];
    private readonly List<Stream> _streams = [];

    public RemoteToolHostTestServer(
        RemoteToolHostStorage storage,
        string peerId = "sat_test",
        string? reportedPeerId = null)
    {
        _storage = storage;
        PeerId = peerId;
        ReportedPeerId = reportedPeerId ?? peerId;
        Leases = new WorkspaceLeaseManager(
            onReleased: released =>
            {
                _terminals.ReleaseLease(released.LeaseId);
                RemoteToolArtifactStore.CleanupLeaseArtifacts(storage.ArtifactsRootPath, released.LeaseId);
            });
        _handlers = new RemoteToolHostMcpHandlers(storage, Leases, _terminals);
        Directory = new TestDirectory(this);
    }

    public string PeerId { get; }
    public string ReportedPeerId { get; }
    public WorkspaceLeaseManager Leases { get; }
    public IRemoteToolHostDirectory Directory { get; }

    public RemoteToolHostClient CreateClient(IApprovalService approvals) => new(Directory, approvals);

    /// <summary>Opens an MCP session that bypasses the client so raw protocol framing can be tested.</summary>
    public async Task<McpClient> ConnectRawAsync(CancellationToken cancellationToken = default)
    {
        var client = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await McpClient.CreateAsync(
            new StreamClientTransport(client, client, loggerFactory: null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        foreach (var stream in _streams.ToArray())
        {
            try { await stream.DisposeAsync(); }
            catch (Exception) { }
        }
        foreach (var session in _sessions.ToArray())
        {
            try { await session; }
            catch (Exception) { }
        }
        await _handlers.DisposeAsync();
        _shutdown.Dispose();
    }

    private async Task<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepting = listener.AcceptTcpClientAsync(cancellationToken);
        var connecting = new TcpClient();
        await connecting.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);
        var accepted = await accepting;
        var clientStream = connecting.GetStream();
        var serverStream = accepted.GetStream();
        lock (_streams)
        {
            _streams.Add(clientStream);
            _streams.Add(serverStream);
            _sessions.Add(RunSessionAsync(serverStream));
        }
        return clientStream;
    }

    private async Task RunSessionAsync(Stream stream)
    {
        try
        {
            await using var transport = new StreamServerTransport(
                stream,
                stream,
                RemoteToolHostServerOptions.ServerName,
                loggerFactory: null);
            await using var server = McpServer.Create(
                transport,
                RemoteToolHostServerOptions.Create(_handlers, ReportedPeerId),
                loggerFactory: null,
                serviceProvider: null);
            await server.RunAsync(_shutdown.Token);
        }
        catch (Exception)
        {
            // The client closing its socket ends the session; tests assert on client results.
        }
    }

    private sealed class TestDirectory(RemoteToolHostTestServer server) : IRemoteToolHostDirectory
    {
        public string? CurrentEndpoint => "test://hub";

        public ValueTask<IReadOnlyList<RemoteToolHostDescriptor>> ListAsync(CancellationToken cancellationToken)
        {
            var state = server._storage.LoadHostState();
            IReadOnlyList<RemoteToolHostDescriptor> hosts = state is null
                ? []
                :
                [
                    new RemoteToolHostDescriptor(
                        server.PeerId,
                        state.DisplayName,
                        true,
                        [
                            .. state.Workspaces
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .Select(pair =>
                                {
                                    var status = server.Leases.GetStatus(pair.Key);
                                    return new RemoteToolWorkspaceDescriptor(
                                        pair.Key,
                                        pair.Value,
                                        status is null,
                                        status is null ? null : "other",
                                        status?.ExpiresAt);
                                })
                        ])
                ];
            return ValueTask.FromResult(hosts);
        }

        public async ValueTask<RemoteToolHostConnection> ConnectAsync(
            string hostId,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(hostId, server.PeerId, StringComparison.Ordinal))
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.HostNotRegistered,
                    $"The local Hub has no paired machine '{hostId}'.");
            var stream = await server.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new RemoteToolHostConnection(
                new StreamClientTransport(stream, stream, loggerFactory: null),
                "test://hub");
        }
    }
}

internal sealed class MemoryCredentialStore : IRemoteToolCredentialStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Values => _values;
    public void Write(string reference, string secret) => _values[reference] = secret;
    public string? Read(string reference) => _values.GetValueOrDefault(reference);
    public void Delete(string reference) => _values.Remove(reference);
}

internal sealed class ApproveService : IApprovalService
{
    public int RequestCount { get; private set; }
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => Task.FromResult(true);
    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) => Task.FromResult(true);
    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
    {
        RequestCount++;
        return Task.FromResult(true);
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
    public void Advance(TimeSpan duration) => utcNow += duration;
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dotcraft-rth-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { }
    }
}
