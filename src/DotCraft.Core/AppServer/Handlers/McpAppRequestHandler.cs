using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

/// <summary>Implements connection-owned MCP App View capabilities with isolated UI authority.</summary>
internal sealed class McpAppRequestHandler : IAppServerDomainHandler, IDisposable
{
    private const int MaxMessageBytes = 16 * 1024;
    private const int MaxBridgeResultBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan ResourceReadTimeout = TimeSpan.FromSeconds(10);

    private readonly ISessionService _sessions;
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly IThreadToolDispatchService? _dispatcher;
    private readonly IThreadToolSnapshotService? _snapshots;
    private readonly IThreadToolSnapshotChangeSource? _snapshotChanges;
    private readonly IThreadMcpRuntimeService? _mcpRuntime;
    private readonly McpAppTransientContextStore? _contextStore;
    private readonly McpAppViewRegistry _views;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _publishedSnapshotRevisions = new(StringComparer.Ordinal);
    private readonly HashSet<McpClientManager> _watchedManagers = new(ReferenceEqualityComparer.Instance);
    private readonly object _watchedManagersLock = new();
    private int _disposed;

    public McpAppRequestHandler(
        ISessionService sessions,
        AppServerConnection connection,
        IAppServerTransport transport,
        IThreadToolDispatchService? dispatcher,
        IThreadToolSnapshotService? snapshots,
        IThreadMcpRuntimeService? mcpRuntime,
        McpAppTransientContextStore? contextStore,
        McpAppViewRegistry? views = null)
    {
        _sessions = sessions;
        _connection = connection;
        _transport = transport;
        _dispatcher = dispatcher;
        _snapshots = snapshots;
        _snapshotChanges = snapshots as IThreadToolSnapshotChangeSource;
        _mcpRuntime = mcpRuntime;
        _contextStore = contextStore;
        _views = views ?? new McpAppViewRegistry();
        _connection.McpAppThreadEligibilityRevoked += OnThreadEligibilityRevoked;
        if (_snapshotChanges is not null)
            _snapshotChanges.EffectiveToolSnapshotChanged += OnEffectiveToolSnapshotChanged;
        _ = DisposeWhenConnectionClosesAsync();
    }

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewOpen, OpenAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewResourceRead, ReadResourceAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewToolsList, ListToolsAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewToolCall, CallToolAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewMessage, MessageAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewModelContextUpdate, UpdateModelContextAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewOpenLink, OpenLinkAsync);
        table.Map(Protocol.AppServer.AppServerRpc.McpAppViewClose, CloseAsync);
    }

    private async Task<object?> OpenAsync(
        AppServerTypedRequest<Contract.McpAppViewOpenParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = request.Params;
        var threadId = ValueOrDefault(parameters.ThreadId);
        var turnId = ValueOrDefault(parameters.TurnId);
        var itemId = ValueOrDefault(parameters.ItemId);
        if (string.IsNullOrWhiteSpace(threadId)
            || string.IsNullOrWhiteSpace(turnId)
            || string.IsNullOrWhiteSpace(itemId))
            throw AppServerErrors.InvalidParams("'threadId', 'turnId', and 'itemId' are required.");
        var thread = await _sessions.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var item = thread.Turns
            .FirstOrDefault(turn => string.Equals(turn.Id, turnId, StringComparison.Ordinal))?
            .Items.FirstOrDefault(candidate => string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
        var eligibility = await McpAppEligibilityResolver.ResolveAsync(
            threadId,
            turnId,
            item,
            _snapshots,
            _mcpRuntime,
            cancellationToken).ConfigureAwait(false);
        if (eligibility is null)
            throw McpAppViewErrors.Create("stale", "The MCP tool result is not currently eligible for a View.");
        var payload = eligibility.Payload;
        var registration = eligibility.Registration;
        var appMetadata = eligibility.AppMetadata;
        var manager = eligibility.Manager;
        var generation = eligibility.Generation;
        var canonicalName = registration.Definition.Name;
        var resourceUri = appMetadata.ResourceUri!;
        var snapshot = eligibility.Snapshot;
        WatchManager(manager);

        ReadResourceResult rawResource;
        try
        {
            using var resourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            resourceCts.CancelAfter(ResourceReadTimeout);
            rawResource = await manager.ReadResourceAsync(
                payload.Server,
                resourceUri.AbsoluteUri,
                generation,
                resourceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw McpAppViewErrors.Create("resource_timeout", "The MCP App resource read timed out.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw McpAppViewErrors.Create("protocol_error", "The MCP App resource could not be read.", exception.Message);
        }

        if (!McpAppMetadataParser.TryParseResourceContent(rawResource, resourceUri, out var resource, out var resourceError))
            throw McpAppViewErrors.Create("protocol_error", "The MCP App resource is invalid.", resourceError);

        var handle = $"view_{Guid.NewGuid():N}";
        var state = _views.Add(new McpAppViewState
        {
            Handle = handle,
            ThreadId = threadId,
            TurnId = turnId,
            SourceItemId = itemId,
            ServerName = payload.Server,
            Origin = payload.Origin,
            Generation = generation,
            ToolName = canonicalName,
            DefinitionId = registration.Definition.Id,
            RuntimeBindingId = registration.Binding.Id,
            SnapshotRevision = snapshot.Revision,
            BindingRevision = registration.Binding.Revision,
            RawSourceToolId = payload.SourceToolId,
            ResourceUri = resourceUri,
            Manager = manager
        });

        // Close the publication race between the initial authority check/resource read and
        // capability registration. A change before Add is observed here; a change after Add is
        // observed by the snapshot/status subscriptions.
        var latestSnapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(threadId, cancellationToken).ConfigureAwait(false);
        var latestGeneration = await manager.GetGenerationAsync(payload.Server, cancellationToken).ConfigureAwait(false);
        var newerRevisionPublished = _publishedSnapshotRevisions.TryGetValue(state.ThreadId, out var publishedRevision)
                                      && publishedRevision != state.SnapshotRevision;
        if (latestGeneration != generation || newerRevisionPublished || !HasCurrentAuthority(latestSnapshot, state))
        {
            if (_views.Close(state.Handle, out var staleState) && staleState is not null)
                staleState.Dispose();
            throw McpAppViewErrors.Create(
                latestGeneration != generation ? "stale" : "revoked",
                latestGeneration != generation
                    ? "The MCP server generation changed."
                    : "The MCP App tool authority is no longer valid.");
        }

        return new Contract.McpAppViewOpenResult
        {
            ViewHandle = state.Handle,
            Resource = McpAppContractMapper.ToContract(resource!),
            ToolInput = McpAppContractMapper.ToElement(payload.Arguments?.DeepClone().AsObject() ?? []),
            ToolResult = new Contract.McpAppToolResult
            {
                Content = McpAppContractMapper.ToElement(payload.Content?.DeepClone().AsArray() ?? []),
                StructuredContent = OmitIfNull(McpAppContractMapper.ToNullableElement(payload.StructuredContent?.DeepClone())),
                Meta = OmitIfNull(McpAppContractMapper.ToNullableElement(payload.Meta?.DeepClone())),
                IsError = payload.IsError == true,
                ErrorCode = OmitIfNull(payload.ErrorCode),
                ErrorMessage = OmitIfNull(payload.ErrorMessage)
            }
        };
    }

    private async Task<object?> ReadResourceAsync(
        AppServerTypedRequest<Contract.McpAppViewResourceReadParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var viewHandle = ValueOrDefault(parameters.ViewHandle) ?? string.Empty;
        var uriText = ValueOrDefault(parameters.Uri);
        var state = await ValidateAsync(viewHandle, cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out _))
            throw McpAppViewErrors.Create("invalid_input", "A valid absolute resource URI is required.");
        ReadResourceResult result;
        try
        {
            using var resourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            resourceCts.CancelAfter(ResourceReadTimeout);
            result = await state.Manager.ReadResourceAsync(
                state.ServerName,
                uriText,
                state.Generation,
                resourceCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw McpAppViewErrors.Create("resource_timeout", "The MCP App resource read timed out.");
        }
        var contents = SerializeArray(result.Contents);
        if (Encoding.UTF8.GetByteCount(contents.ToJsonString()) > MaxBridgeResultBytes)
            throw McpAppViewErrors.Create("result_too_large", "The MCP App resource exceeds the maximum size.");
        return new Contract.McpAppViewResourceReadResult
        {
            Contents = McpAppContractMapper.ToElement(contents)
        };
    }

    private async Task<object?> ListToolsAsync(
        AppServerTypedRequest<Contract.McpAppViewToolsListParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var state = await ValidateAsync(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var snapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        var tools = snapshot.Registrations.Values
            .Where(registration => IsAppVisibleTool(registration, state.ServerName))
            .OrderBy(registration => registration.Definition.Id.SourceToolId.Value, StringComparer.Ordinal)
            .Select(registration => new Contract.McpAppTool
            {
                Name = registration.Definition.Id.SourceToolId.Value,
                Description = OmitIfNull(registration.Definition.Description),
                InputSchema = registration.Definition.InputSchema.Clone()
            })
            .ToList();
        return new Contract.McpAppViewToolsListResult { Tools = tools };
    }

    private async Task<object?> CallToolAsync(
        AppServerTypedRequest<Contract.McpAppViewToolCallParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var toolName = ValueOrDefault(parameters.Tool);
        if (string.IsNullOrWhiteSpace(toolName))
            throw McpAppViewErrors.Create("invalid_input", "A raw MCP tool name is required.");
        var state = await ValidateAsync(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, cancellationToken).ConfigureAwait(false);
        state.CheckToolRate();
        await state.ToolCallSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
            var canonicalName = McpToolNaming.CanonicalToolName(state.ServerName, toolName);
            if (!snapshot.Registrations.TryGetValue(canonicalName, out var registration)
                || registration.Definition.Id.Kind != ToolSourceKind.Mcp
                || !string.Equals(registration.Definition.Id.SourceId, state.ServerName, StringComparison.Ordinal)
                || !string.Equals(registration.Definition.Id.SourceToolId.Value, toolName, StringComparison.Ordinal)
                || !registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
                || !McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var metadata)
                || !metadata.Visibility.HasFlag(McpAppVisibility.App))
                throw McpAppViewErrors.Create("unauthorized", "The MCP App is not authorized to call this tool.");

            var result = await _dispatcher!.DispatchThreadToolAsync(
                state.ThreadId,
                canonicalName,
                ToJsonObject(ValueOrDefault(parameters.Arguments)),
                $"app_{Guid.NewGuid():N}",
                ToolInvocationAudience.App,
                cancellationToken,
                new ToolInvocationOrigin("mcpApp", state.SourceItemId)).ConfigureAwait(false);
            var wire = McpAppContractMapper.ToContract(result);
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(wire, SessionWireJsonOptions.Default)) > MaxBridgeResultBytes)
                throw McpAppViewErrors.Create("result_too_large", "The MCP App tool result exceeds the maximum size.");
            return wire;
        }
        finally
        {
            state.ToolCallSlots.Release();
        }
    }

    private async Task<object?> MessageAsync(
        AppServerTypedRequest<Contract.McpAppViewMessageParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var state = await ValidateAsync(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, cancellationToken).ConfigureAwait(false);
        state.CheckMessageRate();
        var role = ValueOrDefault(parameters.Role);
        var messageContent = ValueOrDefault(parameters.Content);
        var contentType = messageContent is null ? null : ValueOrDefault(messageContent.Type);
        var text = messageContent is null ? null : ValueOrDefault(messageContent.Text);
        if (!string.Equals(role, "user", StringComparison.Ordinal)
            || !string.Equals(contentType, "text", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(text)
            || Encoding.UTF8.GetByteCount(text) > MaxMessageBytes)
            throw McpAppViewErrors.Create("invalid_input", "MCP App messages must contain one bounded user text block.");

        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = "mcpApp",
            Label = state.ServerName,
            RefId = state.SourceItemId
        });
        var content = new AIContent[] { new TextContent(text) };
        var queued = await _sessions.EnqueueTurnInputAsync(state.ThreadId, content, ct: cancellationToken).ConfigureAwait(false);
        _contextStore!.CaptureForQueuedInput(state.Handle, state.ThreadId, queued.Id);
        await _sessions.TryStartNextQueuedTurnAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        return new Contract.McpAppViewMessageResult { QueuedInputId = queued.Id };
    }

    private async Task<object?> UpdateModelContextAsync(
        AppServerTypedRequest<Contract.McpAppViewModelContextUpdateParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        var state = await ValidateAsync(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, cancellationToken).ConfigureAwait(false);
        state.CheckMessageRate();
        var content = ToJsonArray(ValueOrDefault(parameters.Content));
        var structuredContent = ToNullableJsonObject(ValueOrDefault(parameters.StructuredContent));
        if ((content is null || content.Count == 0) && structuredContent is null)
        {
            _contextStore!.ClearView(state.Handle);
            return new Contract.McpAppViewModelContextUpdateResult { Cleared = true };
        }

        var context = ParseTransientContext(content, structuredContent);
        _contextStore!.Set(state.Handle, state.ThreadId, context);
        return new Contract.McpAppViewModelContextUpdateResult { Cleared = false };
    }

    private async Task<object?> OpenLinkAsync(
        AppServerTypedRequest<Contract.McpAppViewOpenLinkParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params;
        _ = await ValidateAsync(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(ValueOrDefault(parameters.Url), UriKind.Absolute, out var uri)
            || !(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
                 || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)))
            throw McpAppViewErrors.Create("unauthorized", "The MCP App link scheme is not allowed.");
        return new Contract.McpAppViewOpenLinkResult { Url = uri.AbsoluteUri };
    }

    private Task<object?> CloseAsync(
        AppServerTypedRequest<Contract.McpAppViewCloseParams> request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var parameters = request.Params;
        var closed = _views.Close(ValueOrDefault(parameters.ViewHandle) ?? string.Empty, out var state);
        if (state is not null)
        {
            _contextStore?.ClearView(state.Handle);
            state.Dispose();
        }
        return Task.FromResult<object?>(new Contract.McpAppViewCloseResult { Closed = closed });
    }

    private async Task<McpAppViewState> ValidateAsync(string handle, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var state = _views.Get(handle);
        var generation = await state.Manager.GetGenerationAsync(state.ServerName, cancellationToken).ConfigureAwait(false);
        if (generation != state.Generation)
        {
            await RevokeViewAsync(
                state,
                "revoked",
                "generation_changed",
                "The MCP server generation changed.").ConfigureAwait(false);
            throw McpAppViewErrors.Create("stale", "The MCP server generation changed.");
        }

        var snapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        if (!HasCurrentAuthority(snapshot, state))
        {
            await RevokeViewAsync(
                state,
                "revoked",
                "snapshot_changed",
                "The MCP App View authority was revoked.").ConfigureAwait(false);
            throw McpAppViewErrors.Create("revoked", "The MCP App View authority was revoked.");
        }
        return state;
    }

    private static bool HasCurrentAuthority(EffectiveToolSnapshot snapshot, McpAppViewState state) =>
        snapshot.Revision == state.SnapshotRevision
        && snapshot.Registrations.TryGetValue(state.ToolName, out var registration)
        && registration.Definition.Id == state.DefinitionId
        && registration.Binding.Id == state.RuntimeBindingId
        && registration.Binding.Revision == state.BindingRevision
        && McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var appMetadata)
        && appMetadata.ResourceUri is not null
        && Uri.Compare(
            appMetadata.ResourceUri,
            state.ResourceUri,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal) == 0;

    private void EnsureAvailable()
    {
        if (!_connection.SupportsMcpApps || _dispatcher is null || _snapshots is null || _mcpRuntime is null || _contextStore is null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.McpAppViewOpen);
    }

    private static bool IsAppVisibleTool(ToolRegistration registration, string serverName) =>
        registration.Definition.Id.Kind == ToolSourceKind.Mcp
        && string.Equals(registration.Definition.Id.SourceId, serverName, StringComparison.Ordinal)
        && registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
        && McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var metadata)
        && metadata.Visibility.HasFlag(McpAppVisibility.App);

    private static JsonObject ToJsonObject(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        return JsonNode.Parse(element.Value.GetRawText()) as JsonObject
               ?? throw McpAppViewErrors.Create("invalid_input", "The MCP App value must be a JSON object.");
    }

    private static JsonObject? ToNullableJsonObject(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return JsonNode.Parse(element.Value.GetRawText()) as JsonObject
               ?? throw McpAppViewErrors.Create("invalid_input", "The MCP App value must be a JSON object.");
    }

    private static JsonArray? ToJsonArray(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return JsonNode.Parse(element.Value.GetRawText()) as JsonArray
               ?? throw McpAppViewErrors.Create("invalid_input", "The MCP App content must be a JSON array.");
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new Protocol.Optional<T?>(value);

    private static JsonArray SerializeArray<T>(IEnumerable<T> values)
    {
        var node = JsonSerializer.SerializeToNode(values, SessionWireJsonOptions.Default);
        return node as JsonArray ?? [];
    }

    private static IReadOnlyList<AIContent> ParseTransientContext(JsonArray? content, JsonObject? structuredContent)
    {
        var result = new List<AIContent>();
        var byteCount = structuredContent is null ? 0 : Encoding.UTF8.GetByteCount(structuredContent.ToJsonString());
        foreach (var node in content ?? [])
        {
            if (node is not JsonObject block || block["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type))
                throw McpAppViewErrors.Create("invalid_input", "The MCP App context contains an unknown content block.");
            if (type == "text" && block["text"] is JsonValue textValue && textValue.TryGetValue<string>(out var text))
            {
                byteCount += Encoding.UTF8.GetByteCount(text);
                result.Add(new TextContent(text));
                continue;
            }
            if (type == "image"
                && block["data"] is JsonValue dataValue && dataValue.TryGetValue<string>(out var data)
                && block["mimeType"] is JsonValue mimeValue && mimeValue.TryGetValue<string>(out var mimeType)
                && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bytes = Convert.FromBase64String(data);
                    byteCount += bytes.Length;
                    result.Add(new DataContent(bytes, mimeType));
                    continue;
                }
                catch (FormatException)
                {
                    // Rejected below as one invalid request.
                }
            }
            throw McpAppViewErrors.Create("invalid_input", "The MCP App context contains an unsupported content block.");
        }
        if (structuredContent is not null)
            result.Add(new TextContent($"[Untrusted MCP App structured context]\n{structuredContent.ToJsonString()}"));
        if (byteCount > MaxMessageBytes)
            throw McpAppViewErrors.Create("invalid_input", "The MCP App context exceeds the maximum size.");
        return result;
    }

    private async Task DisposeWhenConnectionClosesAsync()
    {
        await _connection.Closed.ConfigureAwait(false);
        Dispose();
    }

    private void WatchManager(McpClientManager manager)
    {
        lock (_watchedManagersLock)
        {
            if (_watchedManagers.Add(manager))
                manager.StatusChanged += OnMcpStatusChanged;
        }
    }

    private void OnMcpStatusChanged(object? sender, McpServerStatusChangedEventArgs args)
    {
        if (sender is McpClientManager manager)
            _ = RevokeStaleViewsAsync(manager, args.Status);
    }

    private void OnEffectiveToolSnapshotChanged(object? sender, EffectiveToolSnapshotChangedEventArgs args)
    {
        _publishedSnapshotRevisions[args.ThreadId] = args.Revision;
        _ = RevokeViewsForSnapshotChangeAsync(args);
    }

    private void OnThreadEligibilityRevoked(string threadId) =>
        _ = RevokeViewsForThreadAsync(threadId);

    private async Task RevokeViewsForThreadAsync(string threadId)
    {
        try
        {
            var candidates = _views.Snapshot()
                .Where(view => string.Equals(view.ThreadId, threadId, StringComparison.Ordinal))
                .ToArray();
            foreach (var view in candidates)
            {
                await RevokeViewAsync(
                    view,
                    "revoked",
                    "thread_rolled_back",
                    "The thread was rolled back.").ConfigureAwait(false);
            }
        }
        catch
        {
            // Connection teardown or concurrent View closure already released the capabilities.
        }
    }

    private async Task RevokeViewsForSnapshotChangeAsync(EffectiveToolSnapshotChangedEventArgs args)
    {
        try
        {
            var candidates = _views.Snapshot()
                .Where(view => string.Equals(view.ThreadId, args.ThreadId, StringComparison.Ordinal)
                               && view.SnapshotRevision != args.Revision)
                .ToArray();
            foreach (var view in candidates)
            {
                await RevokeViewAsync(
                    view,
                    "revoked",
                    "snapshot_changed",
                    "The effective tool authority changed.").ConfigureAwait(false);
            }
        }
        catch
        {
            // Connection teardown or concurrent View closure already released the capabilities.
        }
    }

    private async Task RevokeStaleViewsAsync(McpClientManager manager, McpServerStatusSnapshot status)
    {
        try
        {
            var generation = await manager.GetGenerationAsync(status.Name).ConfigureAwait(false);
            var candidates = _views.Snapshot().Where(view => ReferenceEquals(view.Manager, manager)
                                                              && string.Equals(view.ServerName, status.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var view in candidates)
            {
                var ready = status.Enabled && string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase);
                if (ready && generation == view.Generation)
                    continue;
                await RevokeViewAsync(
                    view,
                    ready ? "revoked" : "offline",
                    ready ? "generation_changed" : "server_offline",
                    ready ? "The MCP server generation changed." : "The MCP server is offline.").ConfigureAwait(false);
            }
        }
        catch
        {
            // Connection teardown or a concurrent runtime disposal already revoked the View.
        }
    }

    private async Task RevokeViewAsync(
        McpAppViewState view,
        string status,
        string code,
        string fallbackText)
    {
        if (!_views.Close(view.Handle, out var closed) || closed is null)
            return;

        _contextStore?.ClearView(view.Handle);
        closed.Dispose();
        if (_connection.IsClosed)
            return;

        await _transport.NotifyContractAsync(
            Protocol.AppServer.AppServerRpc.McpAppViewStatusUpdated,
            new Contract.McpAppViewStatusUpdatedParams
            {
                ViewHandle = view.Handle,
                Status = status,
                Code = code,
                FallbackText = fallbackText
            }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _connection.McpAppThreadEligibilityRevoked -= OnThreadEligibilityRevoked;
        if (_snapshotChanges is not null)
            _snapshotChanges.EffectiveToolSnapshotChanged -= OnEffectiveToolSnapshotChanged;
        lock (_watchedManagersLock)
        {
            foreach (var manager in _watchedManagers)
                manager.StatusChanged -= OnMcpStatusChanged;
            _watchedManagers.Clear();
        }
        foreach (var view in _views.Snapshot())
            _contextStore?.ClearView(view.Handle);
        _views.Dispose();
    }
}
