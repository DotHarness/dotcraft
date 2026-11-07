using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private readonly SemaphoreSlim _attachmentGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ThreadAttachment> _attachments = new(StringComparer.Ordinal);

    private async Task<AppServerTypedResult<Contract.ThreadResumeResult>> HandleThreadResumeAsync(
        AppServerTypedRequest<Contract.ThreadResumeParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var dynamicTools = WorktreeContractMapper.ToDynamicTools(p.DynamicTools);
        var additionalContext = WorktreeContractMapper.ToAdditionalContext(p.AdditionalContext);
        threadBinder.ValidateRuntimeInputs(dynamicTools, additionalContext);
        var chat = threadBinder.ValidateChat(p.Chat);
        if (p.InitialTurnsPage is { } pageOptions)
            _ = ParseRestorationView(pageOptions);

        var wasLoaded = sessionService.TryGetLoadedThread(p.ThreadId) is not null;
        var thread = await sessionService.ResumeThreadAsync(p.ThreadId, ct);
        if (p.Cwd != null || p.RuntimeWorkspaceRoots != null)
            thread = await sessionService.UpdateThreadWorkspaceAsync(p.ThreadId, p.Cwd, p.RuntimeWorkspaceRoots, ct);
        await threadBinder.BindThreadRuntimeAsync(
            thread, dynamicTools, additionalContext, ct, bindConnectionTools: !wasLoaded);
        threadBinder.BindChat(thread.Id, chat);
        var instructionSources = await sessionService.GetInstructionSourcesAsync(thread.Id, ct);
        var preparedPage = PrepareThreadRestorePage(p.ThreadId, p.InitialTurnsPage, ct);
        var view = preparedPage?.View ?? ThreadObservationView.Metadata;
        var summaryTools = preparedPage?.SummaryTools ?? [];

        await RestoreAttachmentAsync(thread.Id, view, summaryTools, async snapshot =>
        {
            var wire = await ProjectRestorationThreadAsync(thread, snapshot, ct);
            var result = new Contract.ThreadResumeResult
            {
                Thread = AppServerContractMapper.ToContract(wire),
                InstructionSources = instructionSources,
                CurrentState = ProjectCurrentState(snapshot),
                InitialTurnsPage = preparedPage is null ? null : await preparedPage.MergeAsync(snapshot)
            };
            snapshot.EnsureCurrent();
            await responseWriter.WriteResponseAsync(request.Message.Id, result, ct);
            if (!connection.HasSubscription(thread.Id))
                throw new OperationCanceledException("Thread observation was released.");
            RestorePendingRequests(snapshot);
        }, ct);
        return AppServerTypedResult<Contract.ThreadResumeResult>.Written;
    }

    internal Task AttachCreatedThreadAsync(
        SessionThread thread,
        Func<SessionWireThread, Task> respond,
        CancellationToken ct) =>
        RestoreAttachmentAsync(thread.Id, ThreadObservationView.Metadata, [], async snapshot =>
        {
            var wire = await ProjectRestorationThreadAsync(thread, snapshot, ct);
            snapshot.EnsureCurrent();
            await respond(wire);
        }, ct);

    private async Task RestoreAttachmentAsync(
        string threadId,
        ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools,
        Func<ThreadObservationSnapshot, Task> respond,
        CancellationToken ct)
    {
        await _attachmentGate.WaitAsync(ct);
        try
        {
            if (_attachments.TryGetValue(threadId, out var existing)
                && connection.HasSubscription(threadId) && !existing.Cancellation.IsCancellationRequested)
            {
                await existing.Observation.RefreshAsync(view, summaryTools, respond, ct);
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ThreadObservation? observation = null;
            try
            {
                connection.BeginObservation(threadId);
                observation = await sessionService.ObserveThreadAsync(threadId, view, summaryTools, cancellation.Token);
                if (!connection.TryAddSubscription(threadId, cancellation))
                    throw new InvalidOperationException("Thread observation is already attached.");
                var attachment = new ThreadAttachment(observation, cancellation);
                _attachments[threadId] = attachment;
                connection.SetObservation(threadId, observation);
                connection.CompleteObservation(threadId, observation);
                await respond(observation.Snapshot);
                _ = RunAttachmentAsync(threadId, attachment);
            }
            catch
            {
                connection.CompleteObservation(threadId, null);
                observation?.Dispose();
                if (observation is not null)
                {
                    connection.RemoveObservation(threadId, observation);
                    if (_attachments.TryGetValue(threadId, out var failed) && ReferenceEquals(failed.Observation, observation))
                        _attachments.TryRemove(new KeyValuePair<string, ThreadAttachment>(threadId, failed));
                }
                connection.TryCancelSubscription(threadId, cancellation);
                cancellation.Dispose();
                throw;
            }
        }
        finally
        {
            _attachmentGate.Release();
        }
    }

    private async Task WithCurrentThreadViewAsync(
        string threadId,
        ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools,
        Func<ThreadObservationSnapshot, Task> respond,
        CancellationToken ct,
        ThreadObservationItemSelection? selection = null)
    {
        if (sessionService.TryGetLoadedThread(threadId) is null)
            return;
        await _attachmentGate.WaitAsync(ct);
        try
        {
            if (_attachments.TryGetValue(threadId, out var attachment)
                && connection.HasSubscription(threadId) && !attachment.Cancellation.IsCancellationRequested)
            {
                await attachment.Observation.RefreshAsync(view, summaryTools, respond, ct, selection);
            }
            else
            {
                using var observation = await sessionService.ObserveThreadAsync(
                    threadId, ThreadObservationView.Metadata, [], ct);
                var response = observation.RefreshAsync(view, summaryTools, respond, ct, selection);
                using var completion = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var pump = DrainTemporaryObservationAsync(observation, completion.Token);
                try { await response; }
                finally
                {
                    await completion.CancelAsync();
                    try { await pump; }
                    catch (OperationCanceledException) when (completion.IsCancellationRequested) { }
                }
            }
        }
        finally
        {
            _attachmentGate.Release();
        }
    }

    private static async Task DrainTemporaryObservationAsync(ThreadObservation observation, CancellationToken ct)
    {
        await foreach (var _ in observation.Events.WithCancellation(ct)) { }
    }

    private async Task RunAttachmentAsync(string threadId, ThreadAttachment attachment)
    {
        try
        {
            var dispatcher = new AppServerEventDispatcher(
                attachment.Observation.Events, connection, transport, sessionService,
                streamDebugLogger: streamDebugLogger,
                enrichThreadWire: threadProjector.EnrichForNotification);
            await dispatcher.RunAsync(attachment.Cancellation.Token);
        }
        catch (OperationCanceledException) when (attachment.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "AppServer observation failed for thread {ThreadId}", threadId);
        }
        finally
        {
            attachment.Observation.Dispose();
            connection.RemoveObservation(threadId, attachment.Observation);
            _attachments.TryRemove(new KeyValuePair<string, ThreadAttachment>(threadId, attachment));
            connection.TryCancelSubscription(threadId, attachment.Cancellation);
        }
    }

    private async Task<SessionWireThread> ProjectRestorationThreadAsync(
        SessionThread thread, ThreadObservationSnapshot snapshot, CancellationToken ct)
    {
        var wire = await threadProjector.ProjectAsync(thread, false, false, ct);
        return wire with
        {
            Runtime = snapshot.Runtime.ToWireRuntimeState(),
            ContextUsage = snapshot.ContextUsage,
            Plan = snapshot.Plan is null ? null : SessionWireMapper.ToWire(snapshot.Plan),
            QueuedInputs = snapshot.QueuedInputs.ToList(),
            Configuration = snapshot.Configuration
        };
    }

    private Contract.ThreadResumeCurrentState ProjectCurrentState(ThreadObservationSnapshot snapshot) => new()
    {
        TurnId = snapshot.Turn?.Id,
        TurnStatus = snapshot.Turn is { } turn ? JsonNamingPolicy.CamelCase.ConvertName(turn.Status.ToString()) : null,
        ItemCount = VisibleItemCount(snapshot.ItemCount, snapshot.ToolExecutionCount),
        PlanTurnId = snapshot.PlanTurnId,
        ToolArguments = snapshot.ToolArguments.Count == 0 ? null : snapshot.ToolArguments,
        ToolProgress = snapshot.ToolProgress.Count == 0 ? null : snapshot.ToolProgress.ToDictionary(
            entry => entry.Key, entry => new Contract.ThreadToolProgressState
            {
                CallId = entry.Value.CallId,
                ToolName = entry.Value.ToolName,
                Progress = JsonSerializer.SerializeToElement(entry.Value.Progress)
            }),
        Diff = snapshot.Diff,
        PendingRequests = snapshot.PendingRequests.Select(pending => new Contract.ThreadPendingRequestState
        {
            Method = pending.Type == ItemType.ApprovalRequest
                ? Contract.AppServerMethodNames.ApprovalRequest : Contract.AppServerMethodNames.UserInputRequest,
            RequestId = pending.RequestId,
            ItemId = pending.ItemId,
            TurnId = snapshot.Turn!.Id,
            IsBlocking = pending.IsBlocking
        }).ToArray()
    };

    private static (ThreadObservationView View, IReadOnlyList<DotCraft.Tools.ToolName> Tools) ParseRestorationView(
        Contract.ThreadResumeInitialTurnsPageParams page)
    {
        if (page.ItemsView == "full")
        {
            if (page.SummaryTools is not null)
                throw AppServerErrors.InvalidParams("'summaryTools' requires 'itemsView' to be 'summary'.");
            return (ThreadObservationView.Full, []);
        }
        var tools = ParseSummaryView(page.ItemsView ?? "summary", page.SummaryTools);
        return (tools is null ? ThreadObservationView.Metadata : ThreadObservationView.Summary,
            tools?.Select(tool => new DotCraft.Tools.ToolName(tool.Namespace, tool.Name)).ToArray() ?? []);
    }

    private void RestorePendingRequests(ThreadObservationSnapshot snapshot)
    {
        if (sessionService.TryGetLoadedThread(snapshot.ThreadId) is not { } thread
            || snapshot.Turn is not { } head
            || thread.Turns.FirstOrDefault(turn => turn.Id == head.Id) is not { } current)
            return;

        var sender = new AppServerInteractiveRequestSender(connection, transport, sessionService);
        foreach (var pending in snapshot.PendingRequests)
        {
            var item = current.Items.FirstOrDefault(item => item.Id == pending.ItemId);
            if (item?.Payload is ApprovalRequestPayload approval
                && !current.Items.Any(item => item.Payload is ApprovalResponsePayload response
                    && response.RequestId == approval.RequestId))
                _ = SendRestoredRequestAsync(() => sender.SendApprovalRequestAsync(
                    thread.Id, current.Id, item.Id, approval));
            else if (item?.Payload is UserInputRequestPayload input
                && !current.Items.Any(item => item.Payload is UserInputResponsePayload response
                    && response.RequestId == input.RequestId))
                _ = SendRestoredRequestAsync(() => sender.SendUserInputRequestAsync(
                    thread.Id, current.Id, item.Id, input));
        }
    }

    private async Task SendRestoredRequestAsync(Func<Task<bool>> send)
    {
        try { await send(); }
        catch (Exception ex) { logger?.LogError(ex, "Restored interactive request failed"); }
    }

    private sealed record ThreadAttachment(ThreadObservation Observation, CancellationTokenSource Cancellation);
}
