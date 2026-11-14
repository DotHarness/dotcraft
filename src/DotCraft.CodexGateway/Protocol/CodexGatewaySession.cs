using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;
using DotCraft.Sdk;

namespace DotCraft.CodexGateway.Protocol;

public sealed partial class CodexGatewaySession : IRemoteControlRpcSession
{
    private readonly CodexHandshakeSession _handshake;
    private readonly CodexWorkspaceDiscovery _discovery;
    private readonly CodexWorkspaceCatalog _catalog;
    private readonly string _stateDirectory;
    private readonly CodexRequestBroker _requests;
    private readonly Action<string>? _diagnostic;
    private readonly object _workspaceGate = new();
    private readonly Dictionary<string, WorkspaceConnection> _workspaces = new(StringComparer.Ordinal);
    private readonly List<Task> _retired = [];
    private bool _disposed;
    private sealed record WorkspaceConnection(Task<CodexWorkspaceSession> Session, CancellationTokenSource Lifetime);
    private readonly Func<CodexWorkspace, Func<JsonElement, CancellationToken, Task>, CodexRequestBroker,
        CancellationToken, Task<CodexWorkspaceSession>> _connect;

    public CodexGatewaySession(
        RemoteControlHostOptions options,
        CodexWorkspaceCatalog catalog,
        Func<RemoteControlHostStatus> status,
        Func<JsonElement, CancellationToken, Task> send,
        string? executable = null,
        Action<string>? observedMethod = null,
        Func<CodexWorkspace, Func<JsonElement, CancellationToken, Task>, CodexRequestBroker,
            CancellationToken, Task<CodexWorkspaceSession>>? connectWorkspace = null)
    {
        _catalog = catalog;
        _catalog.Changed += CatalogChanged;
        _stateDirectory = Path.GetFullPath(options.StateDirectory);
        _diagnostic = observedMethod;
        _discovery = new(options.StateDirectory, catalog);
        _handshake = new(options, status, send, observedMethod, HandleAsync);
        _requests = new(_handshake.SendAsync);
        _connect = connectWorkspace ?? ((workspace, output, requests, ct) => CodexWorkspaceSession.ConnectAsync(
            workspace.Path, options.StateDirectory, output,
            new DotCraftLocalOptions
            {
                Executable = executable,
                ClientName = "dotcraft-codex-gateway",
                ClientTitle = "Codex remote gateway",
                ExtraCapabilities = new Dictionary<string, object?>
                {
                    ["remoteHost"] = true, ["commandExecutionStreaming"] = true, ["toolExecutionLifecycle"] = true
                }
            }, ct, requests));
    }

    public Task ReceiveAsync(JsonElement message, CancellationToken cancellationToken) =>
        _requests.TryReceive(message) ? Task.CompletedTask : _handshake.ReceiveAsync(message, cancellationToken);

    private async Task<object?> HandleAsync(string method, JsonElement parameters, CancellationToken ct)
    {
        var workspaces = _catalog.Snapshot();
        if (_discovery.TryHandle(method, parameters, workspaces, out var discovered)) return discovered;
        if (method is "project/list" or "project/read") return Projects(method, parameters, workspaces);
        if (method == "thread/list") return await ListThreadsAsync(parameters, workspaces, ct);
        if (method == "model/list") return await ListModelsAsync(parameters, workspaces, ct);
        if (method == "config/read" && workspaces.Count == 0
            && parameters.Text("projectId") is null && parameters.Text("cwd") is null)
            return UnboundConfiguration(parameters);
        if (!method.StartsWith("thread/", StringComparison.Ordinal)
            && !method.StartsWith("turn/", StringComparison.Ordinal)
            && !method.StartsWith("config/", StringComparison.Ordinal)
            && method != "collaborationMode/list")
            return null;
        if (workspaces.Count == 0) throw new ProjectRequiredException();
        var route = parameters.Text("threadId") is { } threadId
            ? _catalog.ByThread(workspaces, threadId)
            : method is "config/value/write" or "config/batchWrite"
                ? ConfigurationRoute(parameters, workspaces) : CodexWorkspaceCatalog.Select(workspaces, parameters);
        if (parameters.Text("projectId") is { } project && project != route.Id)
            throw new ArgumentException("Conversation and project belong to different workspaces.");
        var p = JsonNode.Parse(parameters.GetRawText())!.AsObject();
        p.Remove("projectId");
        if (method is "config/value/write" or "config/batchWrite") p.Remove("filePath");
        if (method == "thread/start") p["cwd"] ??= route.Path;
        var backend = await GetWorkspaceAsync(route, ct);
        var result = await backend.HandleAsync(method, JsonSerializer.SerializeToElement(p), ct);
        if (result is not null && method == "config/read") return await ProjectConfigurationAsync(result, route, backend, parameters, ct);
        if (result is not null && method is "config/value/write" or "config/batchWrite") return ProjectConfigurationWrite(result, route);
        return result is null ? null : Decorate(result, route);
    }

    private async Task<CodexWorkspaceSession> GetWorkspaceAsync(CodexWorkspace route, CancellationToken ct)
    {
        WorkspaceConnection connection;
        lock (_workspaceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_catalog.Contains(route.Id)) throw new ArgumentException("Project is no longer authorized by this gateway.");
            if (!_workspaces.TryGetValue(route.Id, out connection!))
            {
                var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task<CodexWorkspaceSession> session;
                try
                {
                    session = _connect(route, (message, token) => !lifetime.IsCancellationRequested
                        ? SendWorkspaceAsync(route, message, token) : Task.CompletedTask, _requests, lifetime.Token);
                }
                catch { lifetime.Dispose(); throw; }
                connection = new(session, lifetime);
                _workspaces.Add(route.Id, connection);
            }
        }
        try { return await connection.Session; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_catalog.Contains(route.Id))
        {
            throw new ArgumentException("Project is no longer authorized by this gateway.");
        }
        catch
        {
            lock (_workspaceGate)
                if (_workspaces.TryGetValue(route.Id, out var current) && ReferenceEquals(current, connection))
                {
                    _workspaces.Remove(route.Id);
                    connection.Lifetime.Dispose();
                }
            throw;
        }
    }

    private Task SendWorkspaceAsync(CodexWorkspace route, JsonElement message, CancellationToken ct)
    {
        var node = JsonNode.Parse(message.GetRawText())!.AsObject();
        if (node["params"]?["thread"] is JsonObject thread) thread["projectId"] = route.Id;
        return _handshake.SendAsync(JsonSerializer.SerializeToElement(node), ct);
    }

    private static object Decorate(object result, CodexWorkspace route)
    {
        var node = JsonSerializer.SerializeToNode(result);
        if (node is JsonObject root && root["thread"] is JsonObject thread) thread["projectId"] = route.Id;
        return node!;
    }

    private void CatalogChanged()
    {
        lock (_workspaceGate)
        {
            _retired.RemoveAll(task => task.IsCompletedSuccessfully);
            foreach (var id in _workspaces.Keys.Where(id => !_catalog.Contains(id)).ToArray())
            {
                var connection = _workspaces[id];
                connection.Lifetime.Cancel();
                _workspaces.Remove(id);
                _retired.Add(DisposeWorkspaceAsync(connection));
            }
        }
    }

    private static async Task DisposeWorkspaceAsync(WorkspaceConnection connection)
    {
        try
        {
            CodexWorkspaceSession session;
            try { session = await connection.Session; }
            catch { return; }
            await session.DisposeAsync();
        }
        finally { connection.Lifetime.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        _catalog.Changed -= CatalogChanged;
        Task[] retiring;
        lock (_workspaceGate)
        {
            _disposed = true;
            foreach (var connection in _workspaces.Values)
            {
                connection.Lifetime.Cancel();
                _retired.Add(DisposeWorkspaceAsync(connection));
            }
            _workspaces.Clear();
            retiring = _retired.ToArray();
        }
        await Task.WhenAll(retiring);
        await _requests.DisposeAsync();
        await _handshake.DisposeAsync();
    }
}
