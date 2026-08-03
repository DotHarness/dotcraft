using System.Text.Json;
using DotCraft.Channels;
using DotCraft.Commands.Core;
using DotCraft.Logging;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

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
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.TurnStart, HandleTurnStartAsync);
        table.Map(Contract.AppServerRpc.TurnEnqueue, HandleTurnEnqueueAsync);
        table.Map(Contract.AppServerRpc.TurnQueueRemove, HandleTurnQueueRemoveAsync);
        table.Map(Contract.AppServerRpc.TurnQueueReorder, HandleTurnQueueReorderAsync);
        table.Map(Contract.AppServerRpc.TurnQueueUpdate, HandleTurnQueueUpdateAsync);
        table.Map(Contract.AppServerRpc.TurnInterrupt, HandleTurnInterruptAsync);
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

    private async Task<AppServerTypedResult<Contract.TurnStartResult>> HandleTurnStartAsync(
        AppServerTypedRequest<Contract.TurnStartParams> request,
        CancellationToken ct)
    {
        var msg = request.Message;
        var p = request.Params;
        var input = TurnContractMapper.ToDomain(p.Input);
        var sender = TurnContractMapper.ToDomain(p.Sender);

        var materializedInput = await PrepareTurnInputAsync(input, ct);
        var content = materializedInput.Content;
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

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
            var responsePayload = new Contract.TurnStartResult
            {
                Turn = AppServerContractMapper.ToContract(initialTurn)
            };
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
            sender,
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
                            await responseWriter.WriteResponseAsync(
                                msg.Id,
                                new Contract.TurnStartResult
                                {
                                    Turn = AppServerContractMapper.ToContract(wireTurn)
                                },
                                ct);
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

            return AppServerTypedResult<Contract.TurnStartResult>.Written;
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
        return AppServerTypedResult<Contract.TurnStartResult>.Written;
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

    private async Task<AppServerTypedResult<Contract.TurnEnqueueResult>> HandleTurnEnqueueAsync(
        AppServerTypedRequest<Contract.TurnEnqueueParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var input = TurnContractMapper.ToDomain(p.Input);
        var sender = TurnContractMapper.ToDomain(p.Sender);
        var materializedInput = await PrepareTurnInputAsync(input, ct);
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

        using var channelScope = CreateChannelScope(sender);
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var queued = await sessionService.EnqueueTurnInputAsync(
            p.ThreadId,
            materializedInput.Content,
            sender,
            ct,
            new SessionInputSnapshot
            {
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText,
                SentAsGoal = p.SentAsGoal
            });
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var result = new Contract.TurnEnqueueResult
        {
            QueuedInput = TurnContractMapper.ToContract(queued),
            QueuedInputs = TurnContractMapper.ToContract(thread.QueuedInputs)
        };
        return AppServerTypedResult<Contract.TurnEnqueueResult>.FromResult(result);
    }

    private async Task<AppServerTypedResult<Contract.TurnQueueRemoveResponse>> HandleTurnQueueRemoveAsync(
        AppServerTypedRequest<Contract.TurnQueueRemoveParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var queuedInputId = Require(p.QueuedInputId, "'queuedInputId' is required.");
        if (string.IsNullOrWhiteSpace(queuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");
        var queuedInputs = await sessionService.RemoveQueuedTurnInputAsync(threadId, queuedInputId, ct);
        return AppServerTypedResult<Contract.TurnQueueRemoveResponse>.FromResult(new()
        {
            QueuedInputs = DotCraft.Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queuedInputs))
        });
    }

    private async Task<AppServerTypedResult<Contract.TurnQueueReorderResponse>> HandleTurnQueueReorderAsync(
        AppServerTypedRequest<Contract.TurnQueueReorderParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        if (!p.OrderedQueuedInputIds.IsSet || p.OrderedQueuedInputIds.Value is null)
            throw AppServerErrors.InvalidParams("'orderedQueuedInputIds' is required.");
        var queuedInputs = await sessionService.ReorderQueuedTurnInputsAsync(
            threadId,
            p.OrderedQueuedInputIds.Value,
            ct);
        return AppServerTypedResult<Contract.TurnQueueReorderResponse>.FromResult(new()
        {
            QueuedInputs = DotCraft.Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queuedInputs))
        });
    }

    private async Task<AppServerTypedResult<Contract.TurnQueueUpdateResult>> HandleTurnQueueUpdateAsync(
        AppServerTypedRequest<Contract.TurnQueueUpdateParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var expectedTurnId = Require(p.ExpectedTurnId, "'expectedTurnId' is required.");
        var queuedInputId = Require(p.QueuedInputId, "'queuedInputId' is required.");
        var status = Require(p.Status, "'status' is required.");
        if (string.IsNullOrWhiteSpace(expectedTurnId))
            throw AppServerErrors.InvalidParams("'expectedTurnId' is required.");
        if (string.IsNullOrWhiteSpace(queuedInputId))
            throw AppServerErrors.InvalidParams("'queuedInputId' is required.");
        if (!string.Equals(status, "queued", StringComparison.Ordinal)
            && !string.Equals(status, "guidancePending", StringComparison.Ordinal))
        {
            throw AppServerErrors.InvalidParams("'status' must be 'queued' or 'guidancePending'.");
        }

        await sessionService.EnsureThreadLoadedAsync(threadId, ct);
        var queuedInputs = await sessionService.UpdateQueuedTurnInputAsync(
            threadId,
            queuedInputId,
            expectedTurnId,
            status,
            ct);
        return AppServerTypedResult<Contract.TurnQueueUpdateResult>.FromResult(new()
        {
            QueuedInputs = DotCraft.Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queuedInputs))
        });
    }

    private async Task<AppServerTypedResult<DotCraft.Protocol.RpcEmpty>> HandleTurnInterruptAsync(
        AppServerTypedRequest<Contract.TurnInterruptParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
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
        return AppServerTypedResult<DotCraft.Protocol.RpcEmpty>.FromResult(new());
    }

    private void ValidateTurnInput(IReadOnlyList<SessionWireInputPart> input)
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
        ValidateTurnInput(normalizedInput);
        var materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
        List<AIContent> content;
        try
        {
            content = await SessionInputPartResolver.ResolveStrictAsync(
                materializedInput.MaterializedInputParts,
                ct);
        }
        catch (SessionInputPartValidationException ex)
        {
            if (string.Equals(
                ex.Code,
                SessionInputPartResolver.RemoteImageUrlErrorCode,
                StringComparison.Ordinal))
            {
                throw AppServerErrors.RemoteImageUrlNotSupported();
            }

            throw AppServerErrors.InvalidParams(ex.Message);
        }

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

    private static T Require<T>(DotCraft.Protocol.Optional<T> value, string message)
    {
        if (!value.IsSet || value.Value is null)
            throw AppServerErrors.InvalidParams(message);
        return value.Value;
    }

}
