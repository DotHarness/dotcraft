using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Commands.Core;
using DotCraft.Logging;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

internal sealed class TurnRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    AppServerResponseWriter responseWriter,
    CommandRegistry commandRegistry,
    SkillsLoader? skillsLoader,
    SkillVariantContext skillVariants,
    TraceStore? traceStore,
    SessionStreamDebugLogger? streamDebugLogger,
    SessionApprovalDecision defaultApprovalDecision,
    AppServerThreadWireProjector threadProjector) : IAppServerDomainHandler
{
    private static readonly HttpClient ImageHttpClient = new();

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.TurnStart, HandleTurnStartAsync);
        table.Map(AppServerMethods.TurnEnqueue, HandleTurnEnqueueAsync);
        table.Map(AppServerMethods.TurnQueueRemove, HandleTurnQueueRemoveAsync);
        table.Map(AppServerMethods.TurnQueueReorder, HandleTurnQueueReorderAsync);
        table.Map(AppServerMethods.TurnSteer, HandleTurnSteerAsync);
        table.Map(AppServerMethods.TurnInterrupt, HandleTurnInterruptAsync);
    }

    private void RecordSkillReferences(string threadId, IReadOnlyList<SessionWireInputPart> nativeParts)
    {
        if (traceStore == null || string.IsNullOrWhiteSpace(threadId))
            return;

        foreach (var part in nativeParts)
        {
            if (!string.Equals(part.Type, "skillRef", StringComparison.Ordinal))
                continue;

            var name = part.Name?.Trim().TrimStart('/', '$').Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            traceStore.Record(new TraceEvent
            {
                Type = TraceEventType.SkillReferenced,
                SessionKey = threadId,
                ToolName = name
            });
        }
    }

    private async Task<object?> HandleTurnStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnStartParams>(msg);

        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var inputMaterialization = new InputMaterializationService(
            commandRegistry,
            skillsLoader,
            skillVariants.IsVariantModeEnabled(),
            skillVariants.BuildTarget());
        var normalizedInput = InputMaterializationService.NormalizeInputParts(p.Input);
        ValidateTurnStartInput(normalizedInput);

        var materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
        var content = await ResolveInputPartsAsync(materializedInput.MaterializedInputParts.ToList(), ct);
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

        var channelScopeInfo = connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = p.Sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = p.Sender?.GroupId,
                DefaultDeliveryTarget = p.Sender?.GroupId,
            }
            : new ChannelSessionInfo
            {
                Channel = connection.HasAcpExtensions ? "acp" : "cli",
                UserId = connection.ClientInfo?.Name ?? "anonymous"
            };

        ChatMessage[]? messages = null;
        if (p.Messages.HasValue && p.Messages.Value.ValueKind != JsonValueKind.Null)
        {
            try
            {
                messages = JsonSerializer.Deserialize<ChatMessage[]>(
                    p.Messages.Value.GetRawText(),
                    SessionWireJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw AppServerErrors.InvalidParams($"Failed to deserialize 'messages': {ex.Message}");
            }
        }

        var initialTurnTcs = new TaskCompletionSource<SessionWireTurn>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task OnTurnStarted(SessionWireTurn initialTurn)
        {
            var responsePayload = new { turn = initialTurn };
            try
            {
                await responseWriter.WriteResponseAsync(msg.Id, responsePayload, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Client disconnected before the response could be delivered; keep draining.
            }
            catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
            {
                // Client disconnected before the response could be delivered; keep draining.
            }

            initialTurnTcs.TrySetResult(initialTurn);
        }

        using var channelScope = ChannelSessionScope.Set(channelScopeInfo);

        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        if (p.Cwd != null || p.RuntimeWorkspaceRoots != null)
        {
            await sessionService.UpdateThreadWorkspaceAsync(
                p.ThreadId,
                p.Cwd,
                p.RuntimeWorkspaceRoots,
                ct);
        }

        var events = sessionService.SubmitInputAsync(
            p.ThreadId,
            content,
            p.Sender,
            messages,
            CancellationToken.None,
            new SessionInputSnapshot
            {
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText,
                SentAsGoal = p.SentAsGoal
            });

        if (connection.HasSubscription(p.ThreadId))
        {
            var subscribedEnumerator = events.GetAsyncEnumerator(CancellationToken.None);
            var drainOwnsEnumerator = false;
            try
            {
                while (await subscribedEnumerator.MoveNextAsync())
                {
                    var evt = subscribedEnumerator.Current;
                    if (evt.EventType == SessionEventType.TurnStarted && evt.TurnPayload is { } startedTurn)
                    {
                        var wireTurn = startedTurn.ToWire(includeItems: false) with { Items = [] };
                        try
                        {
                            await responseWriter.WriteResponseAsync(msg.Id, new { turn = wireTurn }, ct);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // The response is best-effort after disconnect; keep draining below.
                        }
                        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
                        {
                            // The response is best-effort after disconnect; keep draining below.
                        }

                        _ = DrainSubscribedTurnEventsAsync(subscribedEnumerator, connection, CancellationToken.None);
                        drainOwnsEnumerator = true;
                        break;
                    }
                }
            }
            finally
            {
                if (!drainOwnsEnumerator)
                    await subscribedEnumerator.DisposeAsync();
            }

            return null;
        }

        var dispatcher = new AppServerEventDispatcher(
            events,
            connection,
            transport,
            sessionService,
            OnTurnStarted,
            defaultApprovalDecision: defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger,
            enrichThreadWire: threadProjector.EnrichForNotification);

        var dispatchTask = dispatcher.RunAsync(CancellationToken.None);

        _ = dispatchTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                initialTurnTcs.TrySetException(t.Exception!.GetBaseException());
            else if (t.IsCanceled)
                initialTurnTcs.TrySetCanceled(ct);
            else
                initialTurnTcs.TrySetException(new AppServerException(
                    AppServerErrors.InternalErrorCode,
                    "Event dispatch completed without emitting a TurnStarted event."));
        }, TaskContinuationOptions.ExecuteSynchronously);

        await initialTurnTcs.Task;
        return null;
    }

    private async Task DrainSubscribedTurnEventsAsync(
        IAsyncEnumerator<SessionEvent> events,
        AppServerConnection owningConnection,
        CancellationToken ct)
    {
        try
        {
            while (await events.MoveNextAsync())
            {
                ct.ThrowIfCancellationRequested();
                var evt = events.Current;
                if (evt.EventType == SessionEventType.ApprovalRequested)
                    await ScheduleDisconnectedApprovalFallbackAsync(evt, owningConnection, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutdown or explicit drain cancellation.
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[AppServer] Subscribed turn drain error for thread {ex.Message}");
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    private Task ScheduleDisconnectedApprovalFallbackAsync(
        SessionEvent evt,
        AppServerConnection owningConnection,
        CancellationToken ct)
    {
        var item = evt.ItemPayload;
        if (item?.Payload is not ApprovalRequestPayload req)
            return Task.CompletedTask;
        if (evt.TurnId == null)
            return Task.CompletedTask;

        if (owningConnection.IsClosed)
            return CreateInteractiveRequestSender().ResolveApprovalWithFallbackAsync(
                evt.ThreadId,
                evt.TurnId,
                req.RequestId,
                ct);

        _ = Task.Run(async () =>
        {
            try
            {
                await owningConnection.Closed;
                await CreateInteractiveRequestSender().ResolveApprovalWithFallbackAsync(
                    evt.ThreadId,
                    evt.TurnId,
                    req.RequestId,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"[AppServer] Disconnected approval fallback failed: {ex.Message}");
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private async Task<object?> HandleTurnEnqueueAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnEnqueueParams>(msg);
        var materializedInput = await PrepareTurnInputAsync(p.Input, ct);
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

        using var channelScope = CreateChannelScope(p.Sender);
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var queued = await sessionService.EnqueueTurnInputAsync(
            p.ThreadId,
            materializedInput.Content,
            p.Sender,
            ct,
            new SessionInputSnapshot
            {
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText,
                SentAsGoal = p.SentAsGoal
            });
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        return new TurnEnqueueResponse
        {
            QueuedInput = queued,
            QueuedInputs = thread.QueuedInputs.ToList()
        };
    }

    private async Task<object?> HandleTurnQueueRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnQueueRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.QueuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");
        var queuedInputs = await sessionService.RemoveQueuedTurnInputAsync(p.ThreadId, p.QueuedInputId, ct);
        return new TurnQueueRemoveResponse { QueuedInputs = queuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnQueueReorderAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnQueueReorderParams>(msg);
        if (p.OrderedQueuedInputIds == null)
            throw AppServerErrors.InvalidParams("'orderedQueuedInputIds' is required.");
        var queuedInputs = await sessionService.ReorderQueuedTurnInputsAsync(p.ThreadId, p.OrderedQueuedInputIds, ct);
        return new TurnQueueReorderResponse { QueuedInputs = queuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnSteerAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnSteerParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ExpectedTurnId))
            throw AppServerErrors.InvalidParams("'expectedTurnId' is required.");
        if (string.IsNullOrWhiteSpace(p.QueuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");

        using var channelScope = CreateChannelScope(p.Sender);
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var result = await sessionService.SteerTurnAsync(
            p.ThreadId,
            p.ExpectedTurnId,
            p.QueuedInputId,
            ct,
            p.Sender);
        return new TurnSteerResponse { TurnId = result.TurnId, QueuedInputs = result.QueuedInputs.ToList() };
    }

    private async Task<object?> HandleTurnInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<TurnInterruptParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);

        var turn = thread.Turns.FirstOrDefault(t => t.Id == p.TurnId);
        if (turn == null)
            throw AppServerErrors.TurnNotFound(p.TurnId);

        if (turn.Status != TurnStatus.Running
            && turn.Status != TurnStatus.WaitingApproval
            && turn.Status != TurnStatus.WaitingInput)
        {
            throw AppServerErrors.TurnNotRunning(p.TurnId);
        }

        await sessionService.CancelTurnAsync(p.ThreadId, p.TurnId, ct);
        return new { };
    }

    private void ValidateTurnStartInput(IReadOnlyList<SessionWireInputPart> input)
    {
        foreach (var part in input)
        {
            if (!string.Equals(part.Type, "commandRef", StringComparison.Ordinal))
                continue;

            var commandName = !string.IsNullOrWhiteSpace(part.Name)
                ? part.Name
                : ExtractCommandName(part.RawText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(commandName))
                continue;

            var registration = commandRegistry.GetRegistration(commandName);
            if (registration == null ||
                string.Equals(registration.Category, "custom", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw AppServerErrors.InvalidParams(
                $"Built-in slash command '{registration.Name}' cannot be sent as 'commandRef' in 'turn/start'. Use 'command/execute' or dedicated UI instead.");
        }
    }

    private async Task<PreparedTurnInput> PrepareTurnInputAsync(
        IReadOnlyList<SessionWireInputPart> input,
        CancellationToken ct)
    {
        if (input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var inputMaterialization = new InputMaterializationService(
            commandRegistry,
            skillsLoader,
            skillVariants.IsVariantModeEnabled(),
            skillVariants.BuildTarget());
        var normalizedInput = InputMaterializationService.NormalizeInputParts(input);
        ValidateTurnStartInput(normalizedInput);
        var materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
        var content = await ResolveInputPartsAsync(materializedInput.MaterializedInputParts.ToList(), ct);
        return new PreparedTurnInput
        {
            NativeInputParts = materializedInput.NativeInputParts,
            MaterializedInputParts = materializedInput.MaterializedInputParts,
            DisplayText = materializedInput.DisplayText,
            Content = content
        };
    }

    private IDisposable? CreateChannelScope(SenderContext? sender)
    {
        var channelScopeInfo = connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = sender?.GroupId,
                DefaultDeliveryTarget = sender?.GroupId,
            }
            : new ChannelSessionInfo
            {
                Channel = connection.HasAcpExtensions ? "acp" : "cli",
                UserId = connection.ClientInfo?.Name ?? "anonymous"
            };

        return ChannelSessionScope.Set(channelScopeInfo);
    }

    private AppServerInteractiveRequestSender CreateInteractiveRequestSender() =>
        new(
            connection,
            transport,
            sessionService,
            defaultApprovalDecision);

    private sealed class PreparedTurnInput
    {
        public IReadOnlyList<SessionWireInputPart> NativeInputParts { get; init; } = [];

        public IReadOnlyList<SessionWireInputPart> MaterializedInputParts { get; init; } = [];

        public string DisplayText { get; init; } = string.Empty;

        public List<AIContent> Content { get; init; } = [];
    }

    private static string ExtractCommandName(string rawCommand)
    {
        var trimmed = rawCommand.Trim();
        if (trimmed.Length == 0)
            return rawCommand;

        var whitespaceIndex = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespaceIndex >= 0 ? trimmed[..whitespaceIndex] : trimmed;
    }

    private static async Task<List<AIContent>> ResolveInputPartsAsync(
        List<SessionWireInputPart> parts,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            AIContent content;
            switch (part.Type)
            {
                case "localImage" when part.Path is { } path:
                    content = await ResolveLocalImageAsync(path, part.MimeType, part.FileName, ct);
                    break;
                case "image" when part.Url is { } url:
                    content = await ResolveRemoteImageAsync(url, ct);
                    break;
                default:
                    content = part.ToAIContent();
                    break;
            }
            result.Add(content);
        }
        return result;
    }

    private static async Task<AIContent> ResolveLocalImageAsync(
        string path,
        string? mimeTypeHint,
        string? fileNameHint,
        CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var mediaType = InferMediaType(path);
            var data = new DataContent(bytes, mediaType);
            data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            data.AdditionalProperties[AppServerInputMetadataKeys.LocalImagePath] = path;
            if (!string.IsNullOrWhiteSpace(mimeTypeHint))
                data.AdditionalProperties[AppServerInputMetadataKeys.LocalImageMimeType] = mimeTypeHint.Trim();
            if (!string.IsNullOrWhiteSpace(fileNameHint))
                data.AdditionalProperties[AppServerInputMetadataKeys.LocalImageFileName] = fileNameHint.Trim();
            return data;
        }
        catch
        {
            // Best-effort: return placeholder if file cannot be read.
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static async Task<AIContent> ResolveRemoteImageAsync(string url, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return new TextContent("[image:invalid-url]");

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return new TextContent($"[image:blocked-scheme:{uri.Scheme}]");
            }

            if (IsInternalAddress(uri.Host))
                return new TextContent("[image:blocked-internal]");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await ImageHttpClient.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType
                ?? InferMediaType(url);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if URL cannot be fetched.
            return new TextContent($"[image:{url}]");
        }
    }

    private static bool IsInternalAddress(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            host == "::1" ||
            host.StartsWith("127.", StringComparison.Ordinal) ||
            host.StartsWith("0.", StringComparison.Ordinal))
        {
            return true;
        }

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("localhost"))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();

            if (bytes.Length == 4)
            {
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }

            if (bytes.Length == 16)
            {
                if (bytes.AsSpan().ToArray().All(b => b == 0) && bytes[15] == 1) return true;
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
            }
        }

        return false;
    }

    private static string InferMediaType(string pathOrUrl)
    {
        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
    }
}
