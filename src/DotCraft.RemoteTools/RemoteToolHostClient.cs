using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient :
    IRemoteToolHostClient,
    IRemoteFileTransferClient,
    IAsyncDisposable
{
    private const int MaxUnavailableDetailChars = 200;
    private const int MaxRemoteArtifactPathChars = 1024;
    private readonly IRemoteToolHostDirectory _directory;
    private readonly IApprovalService _approvalService;
    private readonly int _defaultMaxResultChars;
    private readonly int _spillPreviewLines;
    private readonly long _maxTransferBytes;
    private readonly string _clientInstanceId = "agent_" + Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _routeGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, RemoteToolRoute> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<RouteKey, SharedLease> _leases = new();
    private readonly ConcurrentDictionary<string, HostSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IApprovalService> _pendingApprovals = new(StringComparer.Ordinal);
    private bool _disposed;

    public RemoteToolHostClient(
        IRemoteToolHostDirectory directory,
        IApprovalService approvalService,
        AppConfig? config = null)
    {
        _directory = directory;
        _approvalService = approvalService;
        var tools = (config ?? new AppConfig()).Tools;
        var limits = tools.ResultLimits;
        _defaultMaxResultChars = limits.MaxToolResultChars;
        _spillPreviewLines = limits.SpillPreviewLines;
        _maxTransferBytes = tools.File.MaxTransferBytes;
    }

    /// <summary>Reads the Hub catalog without opening a data connection to a paired machine.</summary>
    public async ValueTask<RemoteToolHostCatalog> ListAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var descriptors = await _directory.ListAsync(cancellationToken).ConfigureAwait(false);
        CaptureDisplayNames(descriptors);
        TryGetRoute(threadId, out var route);
        return new RemoteToolHostCatalog([.. descriptors.Select(MarkOwnLeases)], route);
    }

    /// <summary>The Hub cannot tell whose lease it is, so this process names its own.</summary>
    private RemoteToolHostDescriptor MarkOwnLeases(RemoteToolHostDescriptor host)
    {
        lock (_stateGate)
        {
            if (_leases.Count == 0)
                return host;
            return host with
            {
                Workspaces =
                [
                    .. host.Workspaces.Select(workspace => workspace.BusyOwner is not null
                        && _leases.ContainsKey(new RouteKey(host.HostId, workspace.WorkspaceId))
                            ? workspace with { BusyOwner = "self" }
                            : workspace)
                ]
            };
        }
    }

    public async ValueTask<RemoteToolConnectResult> ConnectAsync(
        string threadId,
        string hostId,
        string workspaceId,
        CancellationToken cancellationToken = default,
        RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        (RemoteToolConnectResult Result, RemoteToolRoute? Published) connected;
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            connected = await ConnectRouteAsync(threadId, hostId, workspaceId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not RemoteToolHostException and not OperationCanceledException)
        {
            throw new RemoteToolHostException(ToolErrorCodes.ExecutionFailed, ex.Message, inner: ex);
        }
        finally
        {
            _routeGate.Release();
        }

        if (connected.Published is not null)
            RaiseRouteChanged(threadId, RemoteToolRouteChangeReason.Connected, initiator, connected.Published);
        return connected.Result;
    }

    public async ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
        string threadId,
        CancellationToken cancellationToken = default,
        RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client)
    {
        RemoteToolRoute? previous;
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                _routes.Remove(threadId, out previous);
            }
            if (previous is not null)
            {
                try { await ReleasePluginThreadAsync(threadId, previous, cancellationToken).ConfigureAwait(false); }
                finally { await ReleaseRouteReferenceAsync(previous, cancellationToken).ConfigureAwait(false); }
            }
        }
        finally
        {
            _routeGate.Release();
        }

        if (previous is null)
            return new RemoteToolDisconnectResult(false);
        RaiseRouteChanged(threadId, RemoteToolRouteChangeReason.Disconnected, initiator, previous);
        return new RemoteToolDisconnectResult(true, previous);
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
            if (!_routes.TryGetValue(context.ThreadId, out var currentRoute) || currentRoute != route
                || !_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out lease!)
                || !string.Equals(lease.Route.LeaseId, route.LeaseId, StringComparison.Ordinal)
                || lease.Lost)
            {
                return Failure(RemoteToolErrorCodes.LeaseLost, "The remote workspace lease was lost.");
            }
        }

        var invocationId = "rti_" + Guid.NewGuid().ToString("N");
        string? preparedBinding = null;
        long? snapshotRevision = null;
        if (definition.Id.Kind == ToolSourceKind.PluginNative)
        {
            ToolRegistration registration;
            lock (_stateGate)
            {
                if (_preparationFailures.TryGetValue((context.ThreadId, route.LeaseId), out var failure)
                    && failure.Revision == context.SnapshotRevision)
                    return Failure(failure.Code, failure.Message);
                if (!_preparedSnapshots.TryGetValue((context.ThreadId, route.LeaseId), out var prepared)
                    || prepared.Snapshot.Revision != context.SnapshotRevision
                    || !prepared.Snapshot.Registrations.Any(item => item.Binding.Id == context.RuntimeBindingId)
                    || !prepared.Bindings.TryGetValue(definition.Id.ToString(), out var binding)
                    || binding.ContractHash != contractHash)
                    return Failure(RemoteToolErrorCodes.RemoteToolUnavailable, "This plugin snapshot has not been prepared on the remote Host.");
                preparedBinding = binding.BindingId;
                snapshotRevision = prepared.Snapshot.Revision;
                registration = prepared.Snapshot.Registrations.Single(item => item.Binding.Id == context.RuntimeBindingId);
            }
            var authority = await registration.Binding.Lease.CheckAsync(context, cancellationToken).ConfigureAwait(false);
            if (!authority.IsAvailable)
                return Failure(RemoteToolErrorCodes.RemoteToolUnavailable, authority.Error?.Message ?? "The plugin source is no longer active.");
        }
        var meta = new RemoteInvocationMeta(
            route.LeaseId,
            route.WorkspaceId,
            invocationId,
            definition.Id.ToString(),
            contractHash,
            context.ThreadId,
            context.TurnId,
            ResolveRemoteResultLimit(definition),
            Math.Clamp(_spillPreviewLines, 1, 500), preparedBinding, snapshotRevision);
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
        string threadId,
        RemoteToolRoute route,
        CancellationToken cancellationToken)
    {
        var key = new RouteKey(route.HostId, route.WorkspaceId);
        SharedLease lease;
        IReadOnlyList<ToolDefinition> definitions;
        lock (_stateGate)
        {
            lease = _leases[key];
            definitions = _snapshots.TryGetValue(threadId, out var snapshot)
                ? snapshot.Registrations.Select(RemoteToolMetadata.NativeDefinition).ToArray()
                : [];
        }
        var catalog = await lease.Session.Client.ListToolsAsync(
            new ListToolsRequestParams
            {
                Meta = new JsonObject
                {
                    ["dotcraft"] = JsonSerializer.SerializeToNode(
                        new RemoteCatalogScope(route.LeaseId, route.WorkspaceId, threadId),
                        RemoteToolHostProtocol.JsonOptions)
                }
            },
            cancellationToken).ConfigureAwait(false);
        var remote = catalog.Tools
            .Select(ParseRemoteContract)
            .Where(item => item is not null)
            .ToDictionary(item => item!.DefinitionId, item => item!, StringComparer.Ordinal);
        var builds = $"host build {lease.BuildVersion}, agent build {RemoteToolHostProtocol.BuildVersion}";
        var matched = new List<string>();
        var unavailable = new List<string>();
        var reasons = new List<RemoteToolUnavailableReason>();
        foreach (var definition in definitions.OrderBy(item => item.Name.ToString(), StringComparer.Ordinal))
        {
            var name = definition.Name.ToString();
            var expected = RemoteToolContractHasher.Compute(definition);
            if (!remote.TryGetValue(definition.Id.ToString(), out var contract))
            {
                unavailable.Add(name);
                reasons.Add(new RemoteToolUnavailableReason(
                    name,
                    RemoteToolErrorCodes.RemoteToolUnavailable,
                    Bounded($"The Remote Tool Host does not export this tool ({builds}).")));
            }
            else if (!string.Equals(contract.ContractHash, expected, StringComparison.Ordinal))
            {
                unavailable.Add(name);
                reasons.Add(new RemoteToolUnavailableReason(
                    name,
                    RemoteToolErrorCodes.ToolContractMismatch,
                    Bounded($"Remote contract {contract.ContractHash} differs from local {expected} ({builds}).")));
            }
            else
            {
                matched.Add(name);
            }
        }
        return new RemoteToolConnectResult(
            route,
            new RemoteToolEnvironment(
                lease.HostName,
                lease.OperatingSystem,
                lease.UserName,
                lease.WorkspacePath),
            matched,
            unavailable,
            reasons);
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
        List<string> affected;
        lock (_stateGate)
        {
            if (!_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out var lease) || lease.Lost)
                return;
            lease.Lost = true;
            affected =
            [
                .. _routes
                    .Where(entry => string.Equals(entry.Value.LeaseId, route.LeaseId, StringComparison.Ordinal))
                    .Select(entry => entry.Key)
            ];
        }

        foreach (var threadId in affected)
        {
            RaiseRouteChanged(
                threadId,
                RemoteToolRouteChangeReason.LeaseLost,
                RemoteToolRouteInitiator.System,
                route);
        }
    }

    private async Task<HostSession> GetSessionAsync(string hostId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(hostId, out var existing))
        {
            if (existing.Matches(hostId, _directory.CurrentEndpoint))
                return existing;
            await InvalidateSessionAsync(hostId).ConfigureAwait(false);
        }
        var connection = await _directory.ConnectAsync(hostId, cancellationToken).ConfigureAwait(false);
        var created = await HostSession.CreateAsync(
            hostId,
            connection,
            HandleElicitationAsync,
            cancellationToken).ConfigureAwait(false);
        if (_sessions.TryAdd(hostId, created))
            return created;
        await created.DisposeAsync().ConfigureAwait(false);
        return _sessions[hostId];
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

    private static void ValidateHostIdentity(string hostId, WorkspaceListResponse response)
    {
        var builds = $"host build {response.BuildVersion}, agent build {RemoteToolHostProtocol.BuildVersion}";
        if (!string.Equals(response.ProfileVersion, RemoteToolHostProtocol.ProfileVersion, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                $"Remote host profile '{response.ProfileVersion}' is unsupported ({builds}).");
        if (!string.Equals(hostId, response.HostId, StringComparison.Ordinal))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.ProtocolMismatch,
                $"The remote endpoint returned a different hostId than the paired host ({builds}).");
    }

    private static string Bounded(string detail) =>
        detail.Length <= MaxUnavailableDetailChars ? detail : detail[..MaxUnavailableDetailChars];

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

    internal static RemoteToolArtifactMeta? ParseRemoteArtifact(JsonNode? node)
    {
        if (node is null)
            return null;
        var artifact = node.Deserialize<RemoteToolArtifactMeta>(RemoteToolHostProtocol.JsonOptions);
        if (artifact is null
            || string.IsNullOrWhiteSpace(artifact.Path)
            || artifact.CharacterCount < 0
            || artifact.Path.Length > MaxRemoteArtifactPathChars
            || artifact.Path.Any(char.IsControl))
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record RemoteContract(
        string ProfileVersion,
        string DefinitionId,
        string ContractHash,
        string CatalogRevision);

    private readonly record struct RouteKey(string HostId, string WorkspaceId);
}
