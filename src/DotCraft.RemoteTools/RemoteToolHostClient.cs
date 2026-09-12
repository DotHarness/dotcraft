using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient : IRemoteToolHostClient, IRemoteFileTransferClient, IAsyncDisposable
{
    private readonly IRemoteToolHostDirectory _directory;
    private readonly RemoteExecutionClient _execution;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, ThreadRoute> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (EffectiveToolSnapshot Snapshot, string Mode)> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<RemoteExecutionSession, int> _references = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _routeGates = new(StringComparer.Ordinal);
    private readonly RemoteOperationScope _operations = new();
    private Task? _disposal;

    public RemoteToolHostClient(IRemoteToolHostDirectory directory, AppConfig? config = null)
    {
        _directory = directory;
        _execution = new RemoteExecutionClient(config);
    }

    public async ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default)
    {
        var hosts = await _directory.ListAsync(cancellationToken).ConfigureAwait(false);
        CaptureDisplayNames(hosts);
        TryGetRoute(threadId, out var route);
        lock (_stateGate)
            return new([.. hosts.Select(host => host with
            {
                Workspaces = [.. host.Workspaces.Select(workspace =>
                    workspace.BusyOwner is not null && _routes.Values.Any(binding =>
                        binding.Session.IsAvailable && binding.Route.HostId == host.HostId && binding.Route.WorkspaceId == workspace.WorkspaceId)
                        ? workspace with { BusyOwner = "self" } : workspace)]
            })], route);
    }

    public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode)
    {
        lock (_stateGate)
        {
            if (_snapshots.TryGetValue(threadId, out var previous) && previous.Snapshot.Revision >= snapshot.Revision) return;
            _snapshots[threadId] = (snapshot, mode);
            if (_routes.TryGetValue(threadId, out var route)) route.Session.UpdateRemoteToolSnapshot(threadId, snapshot, mode);
        }
    }

    public async ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
        CancellationToken cancellationToken = default)
    {
        UpdateRemoteToolSnapshot(threadId, snapshot, mode);
        ThreadRoute? binding;
        lock (_stateGate) _routes.TryGetValue(threadId, out binding);
        if (binding is null) return;
        using var call = binding.Operations.TryEnter(cancellationToken);
        if (call is null) return;
        try { await binding.Session.PrepareAsync(threadId, snapshot, mode, call.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }

    public async ValueTask<RemoteToolConnectResult> ConnectAsync(string threadId, string hostId, string workspaceId,
        CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        using var operation = _operations.Enter(cancellationToken);
        var gate = _routeGates.GetOrAdd(threadId, _ => new(1, 1));
        await gate.WaitAsync(operation.Token).ConfigureAwait(false);
        RemoteToolConnectResult result;
        RemoteExecutionSession? candidate = null;
        ThreadRoute? previous;
        try
        {
            lock (_stateGate) _routes.TryGetValue(threadId, out previous);
            if (previous is not null && previous.Session.IsAvailable && previous.Route.HostId == hostId && previous.Route.WorkspaceId == workspaceId
                && previous.Endpoint == _directory.CurrentEndpoint)
            {
                await PrepareConnectionAsync(threadId, previous.Session, operation.Token).ConfigureAwait(false);
                return (await previous.Session.DescribeAsync(threadId, operation.Token).ConfigureAwait(false)) with { AlreadyConnected = true };
            }
            var connection = await _directory.ConnectAsync(hostId, operation.Token).ConfigureAwait(false);
            candidate = await _execution.OpenSessionAsync("execution_" + Guid.NewGuid().ToString("N"), hostId, workspaceId,
                connection, operation.Token).ConfigureAwait(false);
            await PrepareConnectionAsync(threadId, candidate, operation.Token).ConfigureAwait(false);
            result = await candidate.DescribeAsync(threadId, operation.Token).ConfigureAwait(false);
            var published = candidate;
            candidate.ConnectionLost += () => OnSessionLost(published);
            lock (_stateGate)
            {
                operation.Token.ThrowIfCancellationRequested();
                if (!candidate.IsAvailable) throw Lost();
                _hostDisplayNames[hostId] = candidate.HostDisplayName;
                _workspaceDisplayNames[new(hostId, workspaceId)] = candidate.EnvironmentInfo.WorkspacePath;
                _routes[threadId] = new(candidate, connection.Endpoint);
                _references[candidate] = 1;
            }
            candidate = null;
            if (previous is not null) await ReleaseAsync(threadId, previous).ConfigureAwait(false);
        }
        finally
        {
            try { if (candidate is not null) await candidate.DisposeAsync().ConfigureAwait(false); }
            finally { gate.Release(); }
        }
        RaiseRouteChanged(threadId, RemoteToolRouteChangeReason.Connected, initiator, result.Route);
        return result;
    }

    private async Task PrepareConnectionAsync(string threadId, RemoteExecutionSession session, CancellationToken ct)
    {
        (EffectiveToolSnapshot Snapshot, string Mode) current;
        lock (_stateGate)
            if (!_snapshots.TryGetValue(threadId, out current)) return;
        await session.PrepareAsync(threadId, current.Snapshot, current.Mode, ct).ConfigureAwait(false);
    }

    public async ValueTask<RemoteToolDisconnectResult> DisconnectAsync(string threadId, CancellationToken cancellationToken = default,
        RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
    {
        using var operation = _operations.Enter(cancellationToken);
        cancellationToken = operation.Token;
        var gate = _routeGates.GetOrAdd(threadId, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThreadRoute? previous;
        try
        {
            lock (_stateGate) _routes.Remove(threadId, out previous);
            if (previous is not null) await ReleaseAsync(threadId, previous).ConfigureAwait(false);
        }
        finally { gate.Release(); }
        if (previous is null) return new(false);
        RaiseRouteChanged(threadId, RemoteToolRouteChangeReason.Disconnected, initiator, previous.Route);
        return new(true, previous.Route);
    }

    private async Task ReleaseAsync(string threadId, ThreadRoute binding)
    {
        await binding.Operations.DisposeAsync().ConfigureAwait(false);
        try { await binding.Session.ReleaseThreadAsync(threadId).ConfigureAwait(false); }
        catch (Exception exception) when (exception is RemoteToolHostException or ObjectDisposedException or IOException) { }
        finally
        {
            bool last;
            lock (_stateGate)
            {
                last = --_references[binding.Session] == 0;
                if (last) _references.Remove(binding.Session);
            }
            if (last) await binding.Session.DisposeAsync().ConfigureAwait(false);
        }
    }

    public bool TryGetRoute(string threadId, out RemoteToolRoute route)
    {
        lock (_stateGate)
        {
            if (_routes.TryGetValue(threadId, out var binding)) { route = binding.Route; return true; }
            route = null!;
            return false;
        }
    }

    public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot)
    {
        lock (_stateGate)
        {
            if (!_routes.TryGetValue(threadId, out var binding)) { snapshot = null!; return false; }
            snapshot = new(binding.Session.IsAvailable ? RemoteToolConnectionStatus.Connected : RemoteToolConnectionStatus.LeaseLost,
                binding.Route.HostId, binding.Route.WorkspaceId, binding.Session.EnvironmentInfo);
            return true;
        }
    }

    public bool TryForkRoute(string parentThreadId, string childThreadId)
    {
        lock (_stateGate)
        {
            if (!_routes.TryGetValue(parentThreadId, out var parent) || _routes.ContainsKey(childThreadId)) return false;
            _routes[childThreadId] = new(parent.Session, parent.Endpoint);
            _references[parent.Session]++;
            return true;
        }
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(RemoteToolRoute route, ToolDefinition definition, string contractHash,
        ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
    {
        var binding = Find(context.ThreadId, route);
        if (binding is null) return ToolExecutionResult.Failed(new ToolError(RemoteToolErrorCodes.LeaseLost,
            "The captured remote execution session is no longer connected."));
        using var operation = binding.Operations.TryEnter(cancellationToken);
        if (operation is null) return ToolExecutionResult.Failed(new ToolError(RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost."));
        return await binding.Session.InvokeAsync(route, definition, contractHash, context, arguments, operation.Token).ConfigureAwait(false);
    }

    public async ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId,
        byte[] bytes, CancellationToken cancellationToken = default)
    {
        var binding = Find(threadId, route) ?? throw Lost();
        using var operation = binding.Operations.TryEnter(cancellationToken) ?? throw Lost();
        return await binding.Session.WriteImageAsync(route, threadId, callId, bytes, operation.Token).ConfigureAwait(false);
    }

    public async ValueTask<RemoteFileTransferResult> TransferAsync(string threadId, RemoteFileTransferRequest request,
        RemoteLocalWorkspace local, CancellationToken cancellationToken = default, Action<RemoteFileTransferProgress>? reportProgress = null)
    {
        ThreadRoute? binding;
        lock (_stateGate) _routes.TryGetValue(threadId, out binding);
        if (binding is null) return new(false, request.Direction, request.LocalPath, request.RemotePath, 0, 0,
            RemoteToolErrorCodes.LeaseLost, "No remote workspace is connected.");
        using var operation = binding.Operations.TryEnter(cancellationToken);
        if (operation is null) return new(false, request.Direction, request.LocalPath, request.RemotePath, 0, 0,
            RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost.");
        return await binding.Session.TransferAsync(threadId, request, local, operation.Token, reportProgress).ConfigureAwait(false);
    }

    private ThreadRoute? Find(string threadId, RemoteToolRoute route)
    {
        lock (_stateGate)
            return _routes.TryGetValue(threadId, out var binding) && binding.Route == route && binding.Session.IsAvailable
                ? binding : null;
    }

    private void OnSessionLost(RemoteExecutionSession session)
    {
        string[] affected;
        lock (_stateGate) affected = _routes.Where(pair => ReferenceEquals(pair.Value.Session, session)).Select(pair => pair.Key).ToArray();
        foreach (var thread in affected)
            RaiseRouteChanged(thread, RemoteToolRouteChangeReason.LeaseLost, RemoteToolRouteInitiator.System, session.Route);
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateGate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        await _operations.DisposeAsync().ConfigureAwait(false);
        KeyValuePair<string, ThreadRoute>[] routes;
        lock (_stateGate) { routes = _routes.ToArray(); _routes.Clear(); _snapshots.Clear(); }
        await Task.WhenAll(routes.Select(pair => ReleaseAsync(pair.Key, pair.Value))).ConfigureAwait(false);
        await _execution.DisposeAsync().ConfigureAwait(false);
        foreach (var gate in _routeGates.Values) gate.Dispose();
    }

    private static RemoteToolHostException Lost() => new(RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost.");
    private readonly record struct RouteKey(string HostId, string WorkspaceId);
    private sealed record ThreadRoute(RemoteExecutionSession Session, string? Endpoint)
    {
        internal RemoteToolRoute Route => Session.Route;
        internal RemoteOperationScope Operations { get; } = new();
    }
}
