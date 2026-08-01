using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace DotCraft.Protocol.AppServer;

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
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewOpen, OpenAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewResourceRead, ReadResourceAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewToolsList, ListToolsAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewToolCall, CallToolAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewMessage, MessageAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewModelContextUpdate, UpdateModelContextAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewOpenLink, OpenLinkAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewClose, CloseAsync);
    }

    private async Task<object?> OpenAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = AppServerParams.Get<McpAppViewOpenParams>(message);
        if (string.IsNullOrWhiteSpace(parameters.ThreadId)
            || string.IsNullOrWhiteSpace(parameters.TurnId)
            || string.IsNullOrWhiteSpace(parameters.ItemId))
            throw AppServerErrors.InvalidParams("'threadId', 'turnId', and 'itemId' are required.");
        var thread = await _sessions.GetThreadAsync(parameters.ThreadId, cancellationToken).ConfigureAwait(false);
        var item = thread.Turns
            .FirstOrDefault(turn => string.Equals(turn.Id, parameters.TurnId, StringComparison.Ordinal))?
            .Items.FirstOrDefault(candidate => string.Equals(candidate.Id, parameters.ItemId, StringComparison.Ordinal));
        var eligibility = await McpAppEligibilityResolver.ResolveAsync(
            parameters.ThreadId,
            parameters.TurnId,
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
            ThreadId = parameters.ThreadId,
            TurnId = parameters.TurnId,
            SourceItemId = parameters.ItemId,
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
        var latestSnapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(parameters.ThreadId, cancellationToken).ConfigureAwait(false);
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

        return new McpAppViewOpenResult
        {
            ViewHandle = state.Handle,
            Resource = ToResourceWire(resource!),
            ToolInput = payload.Arguments?.DeepClone().AsObject() ?? [],
            ToolResult = new McpAppToolResultWire
            {
                Content = payload.Content?.DeepClone().AsArray() ?? [],
                StructuredContent = payload.StructuredContent?.DeepClone(),
                Meta = payload.Meta?.DeepClone(),
                IsError = payload.IsError == true,
                ErrorCode = payload.ErrorCode,
                ErrorMessage = payload.ErrorMessage
            }
        };
    }

    private async Task<object?> ReadResourceAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewResourceReadParams>(message);
        var state = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(parameters.Uri, UriKind.Absolute, out _))
            throw McpAppViewErrors.Create("invalid_input", "A valid absolute resource URI is required.");
        ReadResourceResult result;
        try
        {
            using var resourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            resourceCts.CancelAfter(ResourceReadTimeout);
            result = await state.Manager.ReadResourceAsync(
                state.ServerName,
                parameters.Uri,
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
        return new McpAppViewResourceReadResult
        {
            Contents = contents
        };
    }

    private async Task<object?> ListToolsAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewToolsListParams>(message);
        var state = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        var snapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        var tools = snapshot.Registrations.Values
            .Where(registration => registration.Definition.Id.Kind == ToolSourceKind.Mcp
                                   && string.Equals(registration.Definition.Id.SourceId, state.ServerName, StringComparison.Ordinal)
                                   && registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
                                   && McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var metadata)
                                   && metadata.Visibility.HasFlag(McpAppVisibility.App))
            .OrderBy(registration => registration.Definition.Id.SourceToolId.Value, StringComparer.Ordinal)
            .Select(registration => new McpAppToolWire
            {
                Name = registration.Definition.Id.SourceToolId.Value,
                Description = registration.Definition.Description,
                InputSchema = JsonNode.Parse(registration.Definition.InputSchema.GetRawText())?.AsObject() ?? []
            })
            .ToList();
        return new McpAppViewToolsListResult { Tools = tools };
    }

    private async Task<object?> CallToolAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewToolCallParams>(message);
        if (string.IsNullOrWhiteSpace(parameters.Tool))
            throw McpAppViewErrors.Create("invalid_input", "A raw MCP tool name is required.");
        var state = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        state.CheckToolRate();
        await state.ToolCallSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _snapshots!.GetEffectiveToolSnapshotAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
            var canonicalName = McpToolNaming.CanonicalToolName(state.ServerName, parameters.Tool);
            if (!snapshot.Registrations.TryGetValue(canonicalName, out var registration)
                || registration.Definition.Id.Kind != ToolSourceKind.Mcp
                || !string.Equals(registration.Definition.Id.SourceId, state.ServerName, StringComparison.Ordinal)
                || !string.Equals(registration.Definition.Id.SourceToolId.Value, parameters.Tool, StringComparison.Ordinal)
                || !registration.InvocationAudiences.HasFlag(ToolInvocationAudience.App)
                || !McpAppMetadataParser.TryGetToolMetadata(registration.Definition, out var metadata)
                || !metadata.Visibility.HasFlag(McpAppVisibility.App))
                throw McpAppViewErrors.Create("unauthorized", "The MCP App is not authorized to call this tool.");

            var result = await _dispatcher!.DispatchThreadToolAsync(
                state.ThreadId,
                canonicalName,
                parameters.Arguments,
                $"app_{Guid.NewGuid():N}",
                ToolInvocationAudience.App,
                cancellationToken,
                new ToolInvocationOrigin("mcpApp", state.SourceItemId)).ConfigureAwait(false);
            var wire = ToToolResultWire(result);
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(wire, SessionWireJsonOptions.Default)) > MaxBridgeResultBytes)
                throw McpAppViewErrors.Create("result_too_large", "The MCP App tool result exceeds the maximum size.");
            return wire;
        }
        finally
        {
            state.ToolCallSlots.Release();
        }
    }

    private async Task<object?> MessageAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewMessageParams>(message);
        var state = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        state.CheckMessageRate();
        if (!string.Equals(parameters.Role, "user", StringComparison.Ordinal)
            || !string.Equals(parameters.Content.Type, "text", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(parameters.Content.Text)
            || Encoding.UTF8.GetByteCount(parameters.Content.Text) > MaxMessageBytes)
            throw McpAppViewErrors.Create("invalid_input", "MCP App messages must contain one bounded user text block.");

        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = "mcpApp",
            Label = state.ServerName,
            RefId = state.SourceItemId
        });
        var content = new AIContent[] { new TextContent(parameters.Content.Text) };
        var queued = await _sessions.EnqueueTurnInputAsync(state.ThreadId, content, ct: cancellationToken).ConfigureAwait(false);
        _contextStore!.CaptureForQueuedInput(state.Handle, state.ThreadId, queued.Id);
        await _sessions.TryStartNextQueuedTurnAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        return new McpAppViewMessageResult { QueuedInputId = queued.Id };
    }

    private async Task<object?> UpdateModelContextAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewModelContextUpdateParams>(message);
        var state = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        state.CheckMessageRate();
        if ((parameters.Content is null || parameters.Content.Count == 0) && parameters.StructuredContent is null)
        {
            _contextStore!.ClearView(state.Handle);
            return new McpAppViewModelContextUpdateResult { Cleared = true };
        }

        var context = ParseTransientContext(parameters.Content, parameters.StructuredContent);
        _contextStore!.Set(state.Handle, state.ThreadId, context);
        return new McpAppViewModelContextUpdateResult { Cleared = false };
    }

    private async Task<object?> OpenLinkAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        var parameters = AppServerParams.Get<McpAppViewOpenLinkParams>(message);
        _ = await ValidateAsync(parameters.ViewHandle, cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(parameters.Url, UriKind.Absolute, out var uri)
            || !(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
                 || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)))
            throw McpAppViewErrors.Create("unauthorized", "The MCP App link scheme is not allowed.");
        return new McpAppViewOpenLinkResult { Url = uri.AbsoluteUri };
    }

    private Task<object?> CloseAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var parameters = AppServerParams.Get<McpAppViewCloseParams>(message);
        var closed = _views.Close(parameters.ViewHandle, out var state);
        if (state is not null)
        {
            _contextStore?.ClearView(state.Handle);
            state.Dispose();
        }
        return Task.FromResult<object?>(new McpAppViewCloseResult { Closed = closed });
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
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpAppViewOpen);
    }

    private static McpAppResourceWire ToResourceWire(McpAppResourceContent resource) => new()
    {
        Uri = resource.Uri.AbsoluteUri,
        MimeType = resource.MimeType,
        Html = resource.Text ?? Encoding.UTF8.GetString(resource.Blob.Span),
        Ui = new McpAppResourceMetadataWire
        {
            PrefersBorder = resource.Metadata.PrefersBorder ?? false,
            RequestedDomain = resource.Metadata.Domain,
            Csp = new McpAppResourceCspWire
            {
                ConnectDomains = resource.Metadata.Csp?.ConnectDomains.ToList() ?? [],
                ResourceDomains = resource.Metadata.Csp?.ResourceDomains.ToList() ?? [],
                FrameDomains = resource.Metadata.Csp?.FrameDomains.ToList() ?? [],
                BaseUriDomains = resource.Metadata.Csp?.BaseUriDomains.ToList() ?? []
            }
        }
    };

    private static McpAppToolResultWire ToToolResultWire(ToolExecutionResult result)
    {
        if (result.RawSourceResult is { } raw)
        {
            var source = JsonNode.Parse(raw.GetRawText())?.AsObject();
            return new McpAppToolResultWire
            {
                Content = source?["content"]?.DeepClone().AsArray() ?? [],
                StructuredContent = source?["structuredContent"]?.DeepClone(),
                Meta = source?["_meta"]?.DeepClone(),
                IsError = source?["isError"]?.GetValue<bool>() ?? !result.Success,
                ErrorCode = result.Error?.Code,
                ErrorMessage = result.Error?.Message
            };
        }

        var content = new JsonArray();
        if (!string.IsNullOrEmpty(result.Content))
            content.Add(new JsonObject { ["type"] = "text", ["text"] = result.Content });
        return new McpAppToolResultWire
        {
            Content = content,
            StructuredContent = result.StructuredContent is { } structured ? JsonNode.Parse(structured.GetRawText()) : null,
            Meta = result.Meta is { } meta ? JsonNode.Parse(meta.GetRawText()) : null,
            IsError = !result.Success,
            ErrorCode = result.Error?.Code,
            ErrorMessage = result.Error?.Message
        };
    }

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
            global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.McpAppViewStatusUpdated,
            new McpAppViewStatusUpdatedParams
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
