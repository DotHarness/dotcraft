using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostClient :
    IRemoteToolHostClient,
    IAsyncDisposable
{
    private readonly RemoteToolHostStorage _storage;
    private readonly IApprovalService _approvalService;
    private readonly int _defaultMaxResultChars;
    private readonly int _spillPreviewLines;
    private readonly string _clientInstanceId = "agent_" + Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _routeGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, RemoteToolRoute> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<RouteKey, SharedLease> _leases = new();
    private readonly ConcurrentDictionary<string, HostSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IApprovalService> _pendingApprovals = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, ToolDefinition> _definitions =
        new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
    private bool _disposed;

    public RemoteToolHostClient(
        RemoteToolHostStorage storage,
        IApprovalService approvalService,
        AppConfig? config = null)
    {
        _storage = storage;
        _approvalService = approvalService;
        var limits = (config ?? new AppConfig()).Tools.ResultLimits;
        _defaultMaxResultChars = limits.MaxToolResultChars;
        _spillPreviewLines = limits.SpillPreviewLines;
    }

    public void UpdateRemoteToolDefinitions(IReadOnlyList<ToolDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        lock (_stateGate)
        {
            _definitions = definitions.ToDictionary(
                definition => definition.Id.ToString(),
                StringComparer.Ordinal);
        }
    }

    public async ValueTask<RemoteToolHostCatalog> ListAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var descriptors = new List<RemoteToolHostDescriptor>();
        foreach (var registration in _storage.LoadRegistrations())
        {
            try
            {
                var session = await GetSessionAsync(registration, cancellationToken).ConfigureAwait(false);
                var response = await SendAsync<WorkspaceListRequest, WorkspaceListResponse>(
                    session.Client,
                    RemoteToolHostProtocol.WorkspacesList,
                    new WorkspaceListRequest(RemoteToolHostProtocol.ProfileVersion, _clientInstanceId),
                    cancellationToken).ConfigureAwait(false);
                ValidateHostIdentity(registration, response.HostId, response.ProfileVersion);
                descriptors.Add(new RemoteToolHostDescriptor(
                    registration.HostId,
                    response.DisplayName,
                    true,
                    response.Workspaces.Select(workspace => new RemoteToolWorkspaceDescriptor(
                        workspace.WorkspaceId,
                        workspace.Path,
                        !workspace.Busy)).ToArray()));
            }
            catch (Exception ex)
            {
                await InvalidateSessionAsync(registration.HostId).ConfigureAwait(false);
                descriptors.Add(new RemoteToolHostDescriptor(
                    registration.HostId,
                    registration.DisplayName,
                    false,
                    [],
                    MapConnectionError(ex, null).Code));
            }
        }

        TryGetRoute(threadId, out var route);
        return new RemoteToolHostCatalog(descriptors, route);
    }

    public async ValueTask<RemoteToolConnectResult> ConnectAsync(
        string threadId,
        string hostId,
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (TryGetRoute(threadId, out var existing)
                && string.Equals(existing.HostId, hostId, StringComparison.Ordinal)
                && string.Equals(existing.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                lock (_stateGate)
                {
                    if (!_leases.TryGetValue(new RouteKey(hostId, workspaceId), out var currentLease)
                        || currentLease.Lost)
                    {
                        throw new RemoteToolHostException(
                            RemoteToolErrorCodes.LeaseLost,
                            "The current Remote Tool Host lease was lost. Disconnect before reconnecting.");
                    }
                }
                var summary = await BuildMatchSummaryAsync(existing, cancellationToken).ConfigureAwait(false);
                return summary with { AlreadyConnected = true };
            }

            var registration = FindRegistration(hostId);
            var session = await GetSessionAsync(registration, cancellationToken).ConfigureAwait(false);
            var key = new RouteKey(hostId, workspaceId);
            SharedLease lease;
            lock (_stateGate)
            {
                _leases.TryGetValue(key, out lease!);
            }

            if (lease is null)
            {
                WorkspaceAcquireResponse acquired;
                try
                {
                    acquired = await SendAsync<WorkspaceAcquireRequest, WorkspaceAcquireResponse>(
                        session.Client,
                        RemoteToolHostProtocol.WorkspacesAcquire,
                        new WorkspaceAcquireRequest(
                            RemoteToolHostProtocol.ProfileVersion,
                            _clientInstanceId,
                            workspaceId),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw MapConnectionError(ex, null);
                }
                lease = new SharedLease(
                    new RemoteToolRoute(hostId, workspaceId, acquired.LeaseId, acquired.HostInstanceId),
                    acquired.WorkspacePath,
                    session,
                    _clientInstanceId,
                    OnLeaseLost);
                var hostInfo = await SendAsync<WorkspaceListRequest, WorkspaceListResponse>(
                    session.Client,
                    RemoteToolHostProtocol.WorkspacesList,
                    new WorkspaceListRequest(RemoteToolHostProtocol.ProfileVersion, _clientInstanceId),
                    cancellationToken).ConfigureAwait(false);
                ValidateHostIdentity(registration, hostInfo.HostId, hostInfo.ProfileVersion);
                lease.HostName = hostInfo.Hostname;
                lease.OperatingSystem = hostInfo.Os;
                lease.UserName = hostInfo.Username;
                lock (_stateGate)
                {
                    _leases.Add(key, lease);
                }
                lease.StartHeartbeat();
            }
            else if (lease.Lost)
            {
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.LeaseLost,
                    "The shared Remote Tool Host lease was lost. Existing routes must disconnect before reconnecting.");
            }

            RemoteToolRoute? previous;
            lock (_stateGate)
            {
                _routes.Remove(threadId, out previous);
                _routes[threadId] = lease.Route;
                lease.ReferenceCount++;
            }
            if (previous is not null)
                await ReleaseRouteReferenceAsync(previous, CancellationToken.None).ConfigureAwait(false);

            return await BuildMatchSummaryAsync(lease.Route, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _routeGate.Release();
        }
    }

    public async ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RemoteToolRoute? previous;
            lock (_stateGate)
            {
                _routes.Remove(threadId, out previous);
            }
            if (previous is null)
                return new RemoteToolDisconnectResult(false);
            await ReleaseRouteReferenceAsync(previous, cancellationToken).ConfigureAwait(false);
            return new RemoteToolDisconnectResult(true, previous);
        }
        finally
        {
            _routeGate.Release();
        }
    }

    public bool TryGetRoute(string threadId, out RemoteToolRoute route)
    {
        lock (_stateGate)
            return _routes.TryGetValue(threadId, out route!);
    }

    public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot)
    {
        lock (_stateGate)
        {
            if (!_routes.TryGetValue(threadId, out var route)
                || !_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out var lease))
            {
                snapshot = null!;
                return false;
            }

            snapshot = new RemoteToolConnectionSnapshot(
                lease.Lost ? RemoteToolConnectionStatus.LeaseLost : RemoteToolConnectionStatus.Connected,
                route.HostId,
                route.WorkspaceId,
                new RemoteToolEnvironment(
                    lease.HostName,
                    lease.OperatingSystem,
                    lease.UserName,
                    lease.WorkspacePath));
            return true;
        }
    }

    public bool TryForkRoute(string parentThreadId, string childThreadId)
    {
        lock (_stateGate)
        {
            if (!_routes.TryGetValue(parentThreadId, out var route)
                || _routes.ContainsKey(childThreadId)
                || !_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out var lease))
                return false;
            _routes.Add(childThreadId, route);
            lease.ReferenceCount++;
            return true;
        }
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolRoute route,
        ToolDefinition definition,
        string contractHash,
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        SharedLease lease;
        lock (_stateGate)
        {
            if (!_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out lease!)
                || !string.Equals(lease.Route.LeaseId, route.LeaseId, StringComparison.Ordinal)
                || lease.Lost)
            {
                return Failure(RemoteToolErrorCodes.LeaseLost, "The remote workspace lease was lost.");
            }
        }

        var invocationId = "rti_" + Guid.NewGuid().ToString("N");
        var meta = new RemoteInvocationMeta(
            route.LeaseId,
            route.WorkspaceId,
            invocationId,
            definition.Id.ToString(),
            contractHash,
            context.ThreadId,
            context.TurnId,
            ResolveRemoteResultLimit(definition),
            Math.Clamp(_spillPreviewLines, 1, 500));
        var request = new CallToolRequestParams
        {
            Name = definition.Name.ToString(),
            Arguments = arguments.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is null
                    ? JsonSerializer.SerializeToElement<object?>(null)
                    : JsonSerializer.SerializeToElement(pair.Value, RemoteToolHostProtocol.JsonOptions),
                StringComparer.Ordinal),
            Meta = new JsonObject
            {
                ["dotcraft"] = JsonSerializer.SerializeToNode(meta, RemoteToolHostProtocol.JsonOptions)
            }
        };

        var started = DateTimeOffset.UtcNow;
        CallToolResult result;
        try
        {
            _pendingApprovals[invocationId] = ResolveEffectiveApprovalService(_approvalService);
            try
            {
                result = await lease.Session.Client.CallToolAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingApprovals.TryRemove(invocationId, out _);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await InvalidateSessionAsync(route.HostId).ConfigureAwait(false);
            return Failure(
                RemoteToolErrorCodes.RemoteOutcomeUnknown,
                $"The remote transport ended before the execution outcome was known. Invocation: {invocationId}. {ex.Message}",
                invocationId);
        }

        var dotcraft = result.Meta?["dotcraft"] as JsonObject;
        var code = dotcraft?["code"]?.GetValue<string>();
        RemoteToolArtifactMeta? artifact;
        try
        {
            artifact = ParseRemoteArtifact(dotcraft?["remoteArtifact"]);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Failure(
                RemoteToolErrorCodes.ProtocolMismatch,
                "Remote Tool Host returned invalid artifact metadata.",
                invocationId);
        }
        var content = NormalizeText(result);
        if (result.IsError == true)
            return Failure(code ?? ToolErrorCodes.ExecutionFailed, content, invocationId);

        var latency = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        var provenance = new RemoteToolInvocationProvenance(
            "remote",
            route.HostId,
            route.WorkspaceId,
            route.HostInstanceId,
            invocationId,
            latency,
            artifact?.Path,
            artifact?.CharacterCount);
        return ToolExecutionResult.Succeeded(
            content,
            result.StructuredContent,
            meta: provenance.ToJson(),
            rawSourceResult: JsonSerializer.SerializeToElement(result, RemoteToolHostProtocol.JsonOptions),
            contentItems: NormalizeContentItems(result));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        SharedLease[] leases;
        lock (_stateGate)
        {
            leases = _leases.Values.ToArray();
            _routes.Clear();
            _leases.Clear();
        }
        foreach (var lease in leases)
            await lease.DisposeAndReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var session in _sessions.Values)
            await session.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
        _routeGate.Dispose();
    }

    private async Task<RemoteToolConnectResult> BuildMatchSummaryAsync(
        RemoteToolRoute route,
        CancellationToken cancellationToken)
    {
        var key = new RouteKey(route.HostId, route.WorkspaceId);
        SharedLease lease;
        IReadOnlyDictionary<string, ToolDefinition> definitions;
        lock (_stateGate)
        {
            lease = _leases[key];
            definitions = _definitions;
        }
        var catalog = await lease.Session.Client.ListToolsAsync(
            new ListToolsRequestParams(),
            cancellationToken).ConfigureAwait(false);
        var remote = catalog.Tools
            .Select(ParseRemoteContract)
            .Where(item => item is not null)
            .ToDictionary(item => item!.DefinitionId, item => item!, StringComparer.Ordinal);
        var matched = new List<string>();
        var unavailable = new List<string>();
        foreach (var definition in definitions.Values.OrderBy(item => item.Name.ToString(), StringComparer.Ordinal))
        {
            var definitionId = definition.Id.ToString();
            if (remote.TryGetValue(definitionId, out var contract)
                && string.Equals(
                    contract.ContractHash,
                    RemoteToolContractHasher.Compute(definition),
                    StringComparison.Ordinal))
                matched.Add(definition.Name.ToString());
            else
                unavailable.Add(definition.Name.ToString());
        }
        return new RemoteToolConnectResult(
            route,
            new RemoteToolEnvironment(
                lease.HostName,
                lease.OperatingSystem,
                lease.UserName,
                lease.WorkspacePath),
            matched,
            unavailable);
    }

    private async Task ReleaseRouteReferenceAsync(
        RemoteToolRoute route,
        CancellationToken cancellationToken)
    {
        SharedLease? lease = null;
        lock (_stateGate)
        {
            var key = new RouteKey(route.HostId, route.WorkspaceId);
            if (_leases.TryGetValue(key, out lease) && --lease.ReferenceCount == 0)
                _leases.Remove(key);
            else
                lease = null;
        }
        if (lease is not null)
            await lease.DisposeAndReleaseAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnLeaseLost(RemoteToolRoute route)
    {
        lock (_stateGate)
        {
            if (_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out var lease))
                lease.Lost = true;
        }
    }

    private RemoteToolHostRegistration FindRegistration(string hostId) =>
        _storage.LoadRegistrations().FirstOrDefault(
            item => string.Equals(item.HostId, hostId, StringComparison.Ordinal))
        ?? throw new RemoteToolHostException(
            RemoteToolErrorCodes.HostNotRegistered,
            $"Remote Tool Host '{hostId}' is not registered.");

    private async Task<HostSession> GetSessionAsync(
        RemoteToolHostRegistration registration,
        CancellationToken cancellationToken)
    {
        var token = _storage.GetToken(registration);
        if (_sessions.TryGetValue(registration.HostId, out var existing))
        {
            if (existing.Matches(registration, token))
                return existing;
            await InvalidateSessionAsync(registration.HostId).ConfigureAwait(false);
        }
        var created = await HostSession.CreateAsync(
            registration,
            token,
            HandleElicitationAsync,
            cancellationToken).ConfigureAwait(false);
        if (_sessions.TryAdd(registration.HostId, created))
            return created;
        await created.DisposeAsync().ConfigureAwait(false);
        return _sessions[registration.HostId];
    }

    private async Task InvalidateSessionAsync(string hostId)
    {
        if (_sessions.TryRemove(hostId, out var session))
            await session.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<ElicitResult> HandleElicitationAsync(
        ElicitRequestParams? request,
        CancellationToken cancellationToken)
    {
        if (request is null || !string.Equals(request.Mode, "form", StringComparison.Ordinal))
            return new ElicitResult { Action = "decline" };
        var invocationId = request.Meta?["dotcraft"]?["invocationId"]?.GetValue<string>();
        if (invocationId is null || !_pendingApprovals.TryGetValue(invocationId, out var approvalService))
            return new ElicitResult { Action = "decline" };
        var approved = await approvalService.RequestResourceApprovalAsync(
            "remoteToolHost",
            "execute",
            request.Message,
            context: null).ConfigureAwait(false);
        return approved
            ? new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["approved"] = JsonSerializer.SerializeToElement(true)
                }
            }
            : new ElicitResult { Action = "decline" };
    }

    private static IApprovalService ResolveEffectiveApprovalService(IApprovalService service)
    {
        var current = service;
        for (var depth = 0; depth < 16 && current is IApprovalServiceDecorator decorator; depth++)
        {
            var inner = decorator.GetInnerApprovalService(context: null);
            if (inner is null || ReferenceEquals(inner, current))
                break;
            current = inner;
        }
        return current;
    }

    private static async ValueTask<TResult> SendAsync<TParams, TResult>(
        McpClient client,
        string method,
        TParams parameters,
        CancellationToken cancellationToken) where TResult : notnull
    {
        var response = await client.SendRequestAsync<TParams, ExtensionResponse<TResult>>(
            method,
            parameters,
            RemoteToolHostProtocol.JsonOptions,
            default,
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Result is null)
            throw new RemoteToolHostException(
                response.Error?.Code ?? RemoteToolErrorCodes.ProtocolMismatch,
                response.Error?.Message ?? $"Remote extension '{method}' returned no result.");
        return response.Result;
    }

    private static void ValidateHostIdentity(
        RemoteToolHostRegistration registration,
        string hostId,
        string profileVersion)
    {
        if (!string.Equals(profileVersion, RemoteToolHostProtocol.ProfileVersion, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                $"Remote host profile '{profileVersion}' is unsupported.");
        if (!string.Equals(registration.HostId, hostId, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                "The remote endpoint returned a different hostId than the paired host.");
    }

    private static RemoteContract? ParseRemoteContract(Tool tool)
    {
        var node = tool.Meta?["dotcraft/remoteTool"];
        return node?.Deserialize<RemoteContract>(RemoteToolHostProtocol.JsonOptions);
    }

    private int ResolveRemoteResultLimit(ToolDefinition definition)
    {
        var configured = definition.Annotations.TryGetValue("dotcraft/maxResultChars", out var value)
                         && value.TryGetInt32(out var perTool)
            ? perTool
            : _defaultMaxResultChars;
        return configured <= 0
            ? RemoteToolHostProtocol.MaxTransportResultChars
            : Math.Min(configured, RemoteToolHostProtocol.MaxTransportResultChars);
    }

    private static RemoteToolArtifactMeta? ParseRemoteArtifact(JsonNode? node)
    {
        if (node is null)
            return null;
        var artifact = node?.Deserialize<RemoteToolArtifactMeta>(RemoteToolHostProtocol.JsonOptions);
        if (artifact is null
            || string.IsNullOrWhiteSpace(artifact.Path)
            || artifact.CharacterCount < 0
            || artifact.Path.StartsWith("/", StringComparison.Ordinal)
            || artifact.Path.Contains('\\')
            || artifact.Path.Contains(':')
            || artifact.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new JsonException("Invalid Remote Tool Host artifact metadata.");
        }

        return artifact;
    }

    private static ToolExecutionResult Failure(string code, string message, string? invocationId = null)
    {
        var parameters = invocationId is null
            ? null
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["remoteInvocationId"] = JsonSerializer.SerializeToElement(invocationId)
            };
        return ToolExecutionResult.Failed(new ToolError(code, message, parameters));
    }

    private static string NormalizeText(CallToolResult result)
    {
        var parts = result.Content.Select(content => content switch
        {
            TextContentBlock text => text.Text,
            _ => $"[{content.Type} content]"
        }).Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(Environment.NewLine, parts);
    }

    private static IReadOnlyList<AIContent>? NormalizeContentItems(CallToolResult result)
    {
        var items = new List<AIContent>();
        foreach (var content in result.Content)
        {
            switch (content)
            {
                case TextContentBlock text:
                    items.Add(new TextContent(text.Text));
                    break;
                case ImageContentBlock image:
                    try
                    {
                        items.Add(new DataContent(image.DecodedData, image.MimeType));
                    }
                    catch (FormatException)
                    {
                        items.Add(new TextContent("[Invalid remote image payload]"));
                    }
                    break;
                case AudioContentBlock audio:
                    try
                    {
                        items.Add(new DataContent(audio.DecodedData, audio.MimeType));
                    }
                    catch (FormatException)
                    {
                        items.Add(new TextContent("[Invalid remote audio payload]"));
                    }
                    break;
                default:
                    items.Add(new TextContent($"[{content.Type} content]"));
                    break;
            }
        }
        return items.Count == 0 ? null : items;
    }

    private static RemoteToolHostException MapConnectionError(Exception exception, string? invocationId)
    {
        if (exception is RemoteToolHostException typed)
            return typed;
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            return new RemoteToolHostException(
                RemoteToolErrorCodes.AuthenticationFailed,
                "Remote Tool Host rejected the paired bearer token.",
                invocationId,
                exception);
        if (exception is HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException })
            return new RemoteToolHostException(
                RemoteToolErrorCodes.CertificateMismatch,
                "Remote Tool Host TLS certificate did not match the paired fingerprint.",
                invocationId,
                exception);
        return new RemoteToolHostException(
            RemoteToolErrorCodes.HostOffline,
            exception.Message,
            invocationId,
            exception);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record RemoteContract(
        string ProfileVersion,
        string DefinitionId,
        string ContractHash,
        string CatalogRevision);

    private readonly record struct RouteKey(string HostId, string WorkspaceId);

    private sealed class SharedLease
    {
        private readonly string _clientInstanceId;
        private readonly Action<RemoteToolRoute> _lost;
        private readonly CancellationTokenSource _heartbeatCts = new();
        private Task? _heartbeatTask;

        public SharedLease(
            RemoteToolRoute route,
            string workspacePath,
            HostSession session,
            string clientInstanceId,
            Action<RemoteToolRoute> lost)
        {
            Route = route;
            WorkspacePath = workspacePath;
            Session = session;
            _clientInstanceId = clientInstanceId;
            _lost = lost;
        }

        public RemoteToolRoute Route { get; }
        public string WorkspacePath { get; }
        public HostSession Session { get; }
        public int ReferenceCount { get; set; }
        public bool Lost { get; set; }
        public string HostName { get; set; } = "unknown";
        public string OperatingSystem { get; set; } = "unknown";
        public string UserName { get; set; } = "unknown";

        public void StartHeartbeat() => _heartbeatTask = RunHeartbeatAsync();

        public async Task DisposeAndReleaseAsync(CancellationToken cancellationToken)
        {
            _heartbeatCts.Cancel();
            if (_heartbeatTask is not null)
            {
                try { await _heartbeatTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            try
            {
                await SendAsync<WorkspaceLeaseRequest, JsonObject>(
                    Session.Client,
                    RemoteToolHostProtocol.WorkspacesRelease,
                    new WorkspaceLeaseRequest(
                        RemoteToolHostProtocol.ProfileVersion,
                        _clientInstanceId,
                        Route.LeaseId,
                        Route.WorkspaceId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // TTL reclaims an unconfirmed release; disconnect still removes local routing immediately.
            }
            _heartbeatCts.Dispose();
        }

        private async Task RunHeartbeatAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var lastSuccess = DateTimeOffset.UtcNow;
            try
            {
                while (await timer.WaitForNextTickAsync(_heartbeatCts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await SendAsync<WorkspaceLeaseRequest, WorkspaceHeartbeatResponse>(
                            Session.Client,
                            RemoteToolHostProtocol.WorkspacesHeartbeat,
                            new WorkspaceLeaseRequest(
                                RemoteToolHostProtocol.ProfileVersion,
                                _clientInstanceId,
                                Route.LeaseId,
                                Route.WorkspaceId),
                            _heartbeatCts.Token).ConfigureAwait(false);
                        lastSuccess = DateTimeOffset.UtcNow;
                    }
                    catch (RemoteToolHostException ex) when (
                        !string.Equals(ex.Code, RemoteToolErrorCodes.LeaseLost, StringComparison.Ordinal)
                        && DateTimeOffset.UtcNow - lastSuccess < TimeSpan.FromSeconds(60))
                    {
                    }
                    catch when (DateTimeOffset.UtcNow - lastSuccess < TimeSpan.FromSeconds(60))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (_heartbeatCts.IsCancellationRequested)
            {
            }
            catch
            {
                _lost(Route);
            }
        }
    }

    private sealed class HostSession : IAsyncDisposable
    {
        private HostSession(
            McpClient client,
            string endpoint,
            string certificateFingerprint,
            string tokenHash)
        {
            Client = client;
            Endpoint = endpoint;
            CertificateFingerprint = certificateFingerprint;
            TokenHash = tokenHash;
        }

        public McpClient Client { get; }
        private string Endpoint { get; }
        private string CertificateFingerprint { get; }
        private string TokenHash { get; }

        public bool Matches(RemoteToolHostRegistration registration, string token) =>
            string.Equals(Endpoint, registration.Endpoint, StringComparison.Ordinal)
            && string.Equals(
                CertificateFingerprint,
                RemoteToolHostStorage.NormalizeFingerprint(registration.CertificateFingerprint),
                StringComparison.Ordinal)
            && string.Equals(TokenHash, TokenUtilities.HashToken(token), StringComparison.Ordinal);

        public static async Task<HostSession> CreateAsync(
            RemoteToolHostRegistration registration,
            string token,
            Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> elicitationHandler,
            CancellationToken cancellationToken)
        {
            var expectedFingerprint = RemoteToolHostStorage.NormalizeFingerprint(
                registration.CertificateFingerprint);
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                    return false;
                if (certificate is X509Certificate2 certificate2)
                {
                    return string.Equals(
                        RemoteToolCertificate.Fingerprint(certificate2),
                        expectedFingerprint,
                        StringComparison.Ordinal);
                }
                using var converted = new X509Certificate2(certificate);
                return string.Equals(
                    RemoteToolCertificate.Fingerprint(converted),
                    expectedFingerprint,
                    StringComparison.Ordinal);
            };
            var endpoint = new Uri(registration.Endpoint.TrimEnd('/') + "/mcp", UriKind.Absolute);
            var options = new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = registration.DisplayName,
                TransportMode = HttpTransportMode.AutoDetect,
                AdditionalHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = "Bearer " + token
                }
            };
            var transport = new HttpClientTransport(
                options,
                new HttpClient(handler),
                loggerFactory: null,
                ownsHttpClient: true);
            var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ProtocolVersion = RemoteToolHostProtocol.McpProtocolVersion,
                    Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler }
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new HostSession(
                client,
                registration.Endpoint,
                expectedFingerprint,
                TokenUtilities.HashToken(token));
        }

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
