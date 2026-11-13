using System.Text.Json;
using DotCraft.Skills;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

internal sealed class TurnRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    AppServerResponseWriter responseWriter,
    SkillsLoader? skillsLoader,
    TraceStore? traceStore,
    SessionApprovalDecision defaultApprovalDecision,
    ILogger<TurnRequestHandler>? logger) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.TurnStart, HandleTurnStartAsync);
        table.Map(Contract.AppServerRpc.TurnEnqueue, HandleTurnEnqueueAsync);
        table.Map(Contract.AppServerRpc.TurnSteer, HandleTurnSteerAsync);
        table.Map(Contract.AppServerRpc.TurnQueueRemove, HandleTurnQueueRemoveAsync);
        table.Map(Contract.AppServerRpc.TurnQueueReorder, HandleTurnQueueReorderAsync);
        table.Map(Contract.AppServerRpc.TurnQueueUpdate, HandleTurnQueueUpdateAsync);
        table.Map(Contract.AppServerRpc.TurnQueueStart, HandleTurnQueueStartAsync);
        table.Map(Contract.AppServerRpc.TurnInterrupt, HandleTurnInterruptAsync);
    }

    private void RecordSkillReferences(string threadId, IReadOnlyList<SessionInputPart> nativeParts)
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
        var settings = TurnSettingsContractMapper.ToDomain(p);
        var input = TurnContractMapper.ToDomain(p.Input);
        var sender = TurnContractMapper.ToDomain(p.Sender);

        var materializedInput = await PrepareTurnInputAsync(input, ct, allowEmpty: true);
        var content = materializedInput.Content;
        RecordSkillReferences(p.ThreadId, materializedInput.NativeInputParts);

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

        using var channelScope = ChannelSessionScope.Set(await ResolveChannelSessionAsync(sender, p.ThreadId));

        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        await connection.RunThreadNotificationAsync(p.ThreadId, _ => AdmitTurnAsync(), ct);
        return AppServerTypedResult<Contract.TurnStartResult>.Written;

        async Task AdmitTurnAsync()
        {
            var admission = await sessionService.StartOrSteerTurnAsync(
                p.ThreadId,
                content,
                sender,
                messages,
                CancellationToken.None,
                new SessionInputSnapshot
                {
                    ClientUserMessageId = p.ClientUserMessageId,
                    NativeInputParts = materializedInput.NativeInputParts,
                    MaterializedInputParts = materializedInput.MaterializedInputParts,
                    DisplayText = materializedInput.DisplayText,
                    SentAsGoal = p.SentAsGoal
                },
                settings);

            var events = admission.Started
                ? WithNonInteractiveFallback(admission.Events ?? throw new InvalidOperationException("A started Turn requires an execution stream."))
                : null;
            try
            {
                await responseWriter.WriteResponseAsync(
                    msg.Id,
                    new Contract.TurnStartResult
                    {
                        Turn = AppServerContractMapper.ToContract(
                            admission.Turn.ToWire(includeItems: false) with { Items = [] })
                    }, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex)) { }
            finally
            {
                if (events is not null)
                    _ = DrainTurnEventsAsync(events.GetAsyncEnumerator(CancellationToken.None));
            }
        }
    }

    private async Task DrainTurnEventsAsync(IAsyncEnumerator<SessionEvent> events)
    {
        try
        {
            while (await events.MoveNextAsync())
            {
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Turn execution drain failed");
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    private async IAsyncEnumerable<SessionEvent> WithNonInteractiveFallback(IAsyncEnumerable<SessionEvent> events)
    {
        var requests = new AppServerInteractiveRequestSender(connection, transport, sessionService);
        await foreach (var evt in events)
        {
            if (evt.TurnId is { } turnId)
            {
                if (evt.EventType == SessionEventType.ApprovalRequested
                    && !connection.SupportsApproval
                    && evt.ItemPayload?.Payload is ApprovalRequestPayload approval)
                {
                    await requests.ResolveApprovalByPolicyAsync(evt.ThreadId, turnId, approval.RequestId, defaultApprovalDecision);
                }
                else if (evt.EventType == SessionEventType.UserInputRequested
                         && !connection.SupportsRequestUserInput
                         && evt.ItemPayload?.Payload is UserInputRequestPayload userInput)
                {
                    await requests.ResolveUserInputWithEmptyAnswersAsync(evt.ThreadId, turnId, userInput.RequestId);
                }
            }

            yield return evt;
        }
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

        using var channelScope = ChannelSessionScope.Set(await ResolveChannelSessionAsync(sender, p.ThreadId));
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);
        var queued = await sessionService.EnqueueTurnInputAsync(
            p.ThreadId,
            materializedInput.Content,
            sender,
            ct,
            new SessionInputSnapshot
            {
                ClientUserMessageId = p.ClientUserMessageId,
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

    private async Task<AppServerTypedResult<Contract.TurnSteerResult>> HandleTurnSteerAsync(
        AppServerTypedRequest<Contract.TurnSteerParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = p.ThreadId;
        var expectedTurnId = p.ExpectedTurnId;
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(expectedTurnId))
            throw AppServerErrors.InvalidParams("'expectedTurnId' is required.");
        var input = TurnContractMapper.ToDomain(p.Input);
        var sender = TurnContractMapper.ToDomain(p.Sender);
        var materializedInput = await PrepareTurnInputAsync(input, ct);
        RecordSkillReferences(threadId, materializedInput.NativeInputParts);

        using var channelScope = ChannelSessionScope.Set(await ResolveChannelSessionAsync(sender, threadId));
        await sessionService.EnsureThreadLoadedAsync(threadId, ct);
        var turnId = await sessionService.SteerTurnAsync(
            threadId,
            expectedTurnId,
            materializedInput.Content,
            sender,
            ct,
            new SessionInputSnapshot
            {
                ClientUserMessageId = p.ClientUserMessageId,
                NativeInputParts = materializedInput.NativeInputParts,
                MaterializedInputParts = materializedInput.MaterializedInputParts,
                DisplayText = materializedInput.DisplayText
            });

        return AppServerTypedResult<Contract.TurnSteerResult>.FromResult(new()
        {
            TurnId = turnId
        });
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
            QueuedInputs = Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queuedInputs))
        });
    }

    private async Task<AppServerTypedResult<Contract.TurnQueueStartResponse>> HandleTurnQueueStartAsync(
        AppServerTypedRequest<Contract.TurnQueueStartParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = Require(p.ThreadId, "'threadId' is required.");
        var queuedInputId = p.QueuedInputId.IsSet && !string.IsNullOrWhiteSpace(p.QueuedInputId.Value)
            ? p.QueuedInputId.Value
            : null;
        var turnId = await sessionService.StartQueuedTurnAsync(threadId, queuedInputId, ct, WithNonInteractiveFallback);
        return AppServerTypedResult<Contract.TurnQueueStartResponse>.FromResult(new() { TurnId = turnId });
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
            QueuedInputs = Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
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
            QueuedInputs = Protocol.Optional<IReadOnlyList<Contract.QueuedTurnInput>>.FromValue(
                TurnContractMapper.ToContract(queuedInputs))
        });
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> HandleTurnInterruptAsync(
        AppServerTypedRequest<Contract.TurnInterruptParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);

        var turn = thread.Turns.FirstOrDefault(t => t.Id == p.TurnId)
                   ?? await sessionService.ReadThreadTurnAsync(thread.Id, p.TurnId, ct);
        if (turn == null)
            throw AppServerErrors.TurnNotFound(p.TurnId);

        if (turn.Status != TurnStatus.Running
            && turn.Status != TurnStatus.WaitingApproval
            && turn.Status != TurnStatus.WaitingInput)
        {
            throw AppServerErrors.TurnNotRunning(p.TurnId);
        }

        await sessionService.CancelTurnAsync(p.ThreadId, p.TurnId, ct);
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<PreparedTurnInput> PrepareTurnInputAsync(
        IReadOnlyList<SessionInputPart> input,
        CancellationToken ct,
        bool allowEmpty = false)
    {
        if (input.Count == 0 && !allowEmpty)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var inputMaterialization = new InputMaterializationService(skillsLoader);
        SessionInputMaterializationResult materializedInput;
        List<AIContent> content;
        try
        {
            var normalizedInput = InputMaterializationService.NormalizeInputParts(input);
            materializedInput = inputMaterialization.MaterializeNormalized(normalizedInput);
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

    private async Task<ChannelSessionInfo> ResolveChannelSessionAsync(SenderContext? sender, string threadId)
    {
        return connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = sender?.GroupId,
                DefaultDeliveryTarget = (await sessionService.GetThreadAsync(threadId)).ChannelContext,
                Chat = connection.TryGetThreadChat(threadId, out var chat) ? chat : null,
                Surface = connection.Surface
            }
            : new ChannelSessionInfo
            {
                Channel = connection.Surface.Id,
                UserId = "local",
                Surface = connection.Surface
            };
    }

    private sealed class PreparedTurnInput
    {
        public IReadOnlyList<SessionInputPart> NativeInputParts { get; init; } = [];

        public IReadOnlyList<SessionInputPart> MaterializedInputParts { get; init; } = [];

        public string DisplayText { get; init; } = string.Empty;

        public List<AIContent> Content { get; init; } = [];
    }

    private static T Require<T>(Protocol.Optional<T> value, string message)
    {
        if (!value.IsSet || value.Value is null)
            throw AppServerErrors.InvalidParams(message);
        return value.Value;
    }

}
