using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

/// <summary>One authenticated connection with independent execution, snapshot, and resource lifetime.</summary>
public sealed partial class RemoteExecutionSession : IAsyncDisposable
{
    private const int MaxUnavailableDetailChars = 200;
    private const int MaxRemoteArtifactPathChars = 1024;
    private readonly int _defaultMaxResultChars;
    private readonly int _spillPreviewLines;
    private readonly long _maxTransferBytes;
    private readonly SemaphoreSlim _routeGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly RemoteOperationScope _operations = new();
    private readonly Action<RemoteExecutionSession> _released;
    private SessionLease _lease = null!;
    private Task? _disposal;

    private RemoteExecutionSession(string id, AppConfig config,
        Action<RemoteExecutionSession> released)
    {
        Id = id;
        _released = released;
        _defaultMaxResultChars = config.Tools.ResultLimits.MaxToolResultChars;
        _spillPreviewLines = config.Tools.ResultLimits.SpillPreviewLines;
        _maxTransferBytes = config.Tools.File.MaxTransferBytes;
    }

    public string Id { get; }
    public RemoteToolRoute Route => _lease.Route;
    public string HostDisplayName { get; private set; } = string.Empty;
    public RemoteToolEnvironment EnvironmentInfo => new(_lease.HostName, _lease.OperatingSystem,
        _lease.UserName, _lease.WorkspacePath);
    public bool IsAvailable => !_operations.Closed && !_lease.Lost;
    public event Action? ConnectionLost;

    internal static async Task<RemoteExecutionSession> CreateAsync(string id, string hostId, string workspaceId,
        RemoteToolHostConnection connection, string ownerId, AppConfig config,
        Action<RemoteExecutionSession> released, CancellationToken ct)
    {
        var session = new RemoteExecutionSession(id, config, released);
        var transport = await HostSession.CreateAsync(connection, ct).ConfigureAwait(false);
        try
        {
            var info = await SendAsync<WorkspaceListRequest, WorkspaceListResponse>(transport.Client,
                RemoteToolHostProtocol.WorkspacesList, new(RemoteToolHostProtocol.ProfileVersion, ownerId), ct).ConfigureAwait(false);
            ValidateHostIdentity(hostId, info);
            var acquired = await SendAsync<WorkspaceAcquireRequest, WorkspaceAcquireResponse>(transport.Client,
                RemoteToolHostProtocol.WorkspacesAcquire, new(RemoteToolHostProtocol.ProfileVersion, ownerId, workspaceId), ct)
                .ConfigureAwait(false);
            session._lease = new(new(hostId, workspaceId, acquired.LeaseId, acquired.HostInstanceId, id),
                acquired.WorkspacePath, transport, ownerId, session.OnLeaseLost)
            {
                HostName = info.Hostname, OperatingSystem = info.Os, UserName = info.Username,
                BuildVersion = info.BuildVersion, SupportsPlugins = info.Capabilities!.Contains(RemotePluginProtocol.Capability)
            };
            session.HostDisplayName = info.DisplayName;
            session._lease.StartHeartbeat();
            _ = session.ObserveConnectionAsync();
            return session;
        }
        catch { await transport.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async ValueTask<RemoteToolConnectResult> DescribeAsync(string threadId, CancellationToken cancellationToken = default)
    {
        using var operation = _operations.Enter(cancellationToken);
        return await BuildMatchSummaryAsync(threadId, Route, operation.Token).ConfigureAwait(false);
    }

    public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        using var operation = _operations.Enter(cancellationToken);
        await _routeGate.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            var lease = RequireLease(Route);
            await SendAsync<PluginThreadRelease, JsonObject>(lease.Session.Client, RemoteToolHostProtocol.ExecutionThreadRelease,
                new(Route.LeaseId, Route.WorkspaceId, threadId), operation.Token).ConfigureAwait(false);
            lock (_stateGate)
            {
                _snapshots.Remove(threadId);
                _preparedSnapshots.Remove(threadId);
                _preparationFailures.Remove(threadId);
            }
        }
        finally { _routeGate.Release(); }
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolRoute route,
        ToolDefinition definition,
        string contractHash,
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        using var operation = _operations.TryEnter(cancellationToken);
        if (operation is null || !IsAvailable || route != Route)
            return Failure(RemoteToolErrorCodes.LeaseLost, "The remote execution session was lost.");
        cancellationToken = operation.Token;
        var lease = _lease;

        var invocationId = "rti_" + Guid.NewGuid().ToString("N");
        string? preparedBinding = null;
        long? snapshotRevision = null;
        if (definition.Id.Kind == ToolSourceKind.PluginNative)
        {
            ToolRegistration registration;
            lock (_stateGate)
            {
                if (_preparationFailures.TryGetValue(context.ThreadId, out var failure)
                    && failure.Revision == context.SnapshotRevision)
                    return Failure(failure.Code, failure.Message);
                if (!_preparedSnapshots.TryGetValue(context.ThreadId, out var prepared)
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
            result = await lease.Session.Client.CallToolAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !_lease.Lost)
        {
            throw;
        }
        catch (Exception ex)
        {
            OnLeaseLost();
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

    public ValueTask DisposeAsync()
    {
        lock (_stateGate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _operations.DisposeAsync().ConfigureAwait(false);
        await _lease.DisposeAndReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        await _lease.Session.DisposeAsync().ConfigureAwait(false);
        _routeGate.Dispose();
        _released(this);
    }

    private async Task ObserveConnectionAsync()
    {
        await _lease.Session.Client.Completion.ConfigureAwait(false);
        if (!_operations.Closed) OnLeaseLost();
    }

    private void OnLeaseLost()
    {
        lock (_stateGate)
        {
            if (_lease.Lost) return;
            _lease.Lost = true;
        }
        _lease.StopHeartbeat();
        _ = _operations.DisposeAsync();
        if (ConnectionLost is { } lost)
            foreach (var observer in lost.GetInvocationList().Cast<Action>())
                try { observer(); } catch { }
    }

    private async Task<RemoteToolConnectResult> BuildMatchSummaryAsync(
        string threadId,
        RemoteToolRoute route,
        CancellationToken cancellationToken)
    {
        var lease = RequireLease(route);
        IReadOnlyList<ToolDefinition> definitions;
        lock (_stateGate)
            definitions = _snapshots.TryGetValue(threadId, out var snapshot)
                ? snapshot.Registrations.Select(RemoteToolMetadata.NativeDefinition).ToArray() : [];
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

    private static void ValidateHostIdentity(string hostId, WorkspaceListResponse response)
    {
        if (response.Capabilities is null || !response.Capabilities.Contains(RemoteToolHostProtocol.ExecutionSessionsCapability)
            || !response.Capabilities.Contains("files-v1"))
            throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch,
                "The remote Host does not support isolated execution sessions and file transfer.");
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

    private sealed record RemoteContract(string ProfileVersion, string DefinitionId, string ContractHash, string CatalogRevision);
}
