using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Data;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Realtime;

namespace DotCraft.Oratorio.Integrations;

public sealed partial class AppServerRunWorker
{
    private async Task<bool> ConfirmImplementationRecoveryAsync(string runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.AsNoTracking().FirstAsync(x => x.RunId == runId, ct);
        if (run.Purpose != RunPurpose.Implementation || run.Attempt <= 1)
            return true;

        var previous = await db.Runs.AsNoTracking()
            .Where(x => x.RoundId == run.RoundId && x.Attempt < run.Attempt &&
                x.RunnerKind == "appServer" && x.Purpose == RunPurpose.Implementation &&
                (x.Status == RunStatus.Failed || x.Status == RunStatus.TimedOut) && x.TurnId != null)
            .OrderByDescending(x => x.Attempt)
            .Select(x => new InterruptedRun(x.RunId, x.AppServerEndpoint, x.ThreadId, x.TurnId))
            .FirstOrDefaultAsync(ct);
        if (previous is null)
            return true;

        var (result, error) = await TerminatePersistedTurnAsync(previous, ct);
        if (result is TimeoutTerminalResult.Cancelled or TimeoutTerminalResult.Failed or
            TimeoutTerminalResult.Completed or TimeoutTerminalResult.AlreadyTerminal)
            return true;

        await FailRunAsync(runId, RunStatus.Failed, "appServerTerminationUnconfirmed",
            BuildTerminationMessage("Cannot resume implementation until the previous turn has ended.", result, error),
            allowRetry: false, ct);
        return false;
    }

    private async Task<(TimeoutTerminalResult Result, Exception? Error)> TerminatePersistedTurnAsync(
        InterruptedRun run,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(run.AppServerEndpoint) ||
            string.IsNullOrWhiteSpace(run.ThreadId) ||
            string.IsNullOrWhiteSpace(run.TurnId))
        {
            return (TimeoutTerminalResult.MissingTurn, null);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeoutInterruptWait);
            await using var client = await clientFactory.ConnectAsync(
                run.AppServerEndpoint,
                timeout.Token,
                endpointResolver.ResolveConfiguredToken());
            await client.ResumeThreadAsync(run.ThreadId, null, null, timeout.Token);
            try
            {
                await client.InterruptTurnAsync(run.ThreadId, run.TurnId, timeout.Token);
            }
            catch (JsonRpcException ex) when (ex.RpcCode is -32013 or -32014)
            {
                return (TimeoutTerminalResult.AlreadyTerminal, null);
            }

            var result = await WaitForTerminationTerminalAsync(
                run.RunId,
                run.TurnId,
                client,
                "appServerRunnerInterrupted",
                "The DotCraft AppServer runner was interrupted before it completed.",
                timeout.Token);
            return (result, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (TimeoutTerminalResult.WaitTimedOut, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Interrupted AppServer run {RunId} could not confirm termination of persisted turn {TurnId}.", run.RunId, run.TurnId);
            return (TimeoutTerminalResult.InterruptFailed, ex);
        }
    }

    private async Task HandleForcedTerminationAsync(
        string runId,
        IDotCraftAppServerClient? client,
        string? threadId,
        RunTurn turn,
        AppServerRunTerminationReason reason,
        CancellationToken ct)
    {
        var turnId = turn.Id;
        var (errorCode, reasonMessage) = reason == AppServerRunTerminationReason.Stalled
            ? ("appServerStalled", "DotCraft AppServer run heartbeat stalled.")
            : ("appServerTimedOut", "DotCraft AppServer run timed out.");
        if (client is null || string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
        {
            await CompleteForcedTerminationAsync(
                runId,
                RunStatus.TimedOut,
                errorCode,
                reasonMessage,
                TimeoutTerminalResult.MissingTurn,
                CancellationToken.None);
            return;
        }

        if (!turn.Running)
        {
            await CompleteForcedTerminationAsync(
                runId,
                RunStatus.TimedOut,
                errorCode,
                reasonMessage,
                TimeoutTerminalResult.AlreadyTerminal,
                CancellationToken.None);
            return;
        }

        runCoordinator.UpdateRunStatus(runId, turnId, "timedOut");
        PublishRunStatus(runId, "timedOut", turnId, errorCode, reasonMessage, 95, $"{reasonMessage} Interrupting DotCraft turn.");
        await UpdateRunHeartbeatAsync(runId, 95, $"{reasonMessage} Interrupting DotCraft turn.", turnId, CancellationToken.None);

        var terminalResult = TimeoutTerminalResult.WaitTimedOut;
        Exception? interruptError = null;
        try
        {
            using var interruptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            interruptTimeout.CancelAfter(TimeoutInterruptWait);
            await client.InterruptTurnAsync(threadId, turnId, interruptTimeout.Token);
            terminalResult = await WaitForTerminationTerminalAsync(runId, turnId, client, errorCode, reasonMessage, interruptTimeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            terminalResult = TimeoutTerminalResult.WaitTimedOut;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AppServer run {RunId} could not interrupt DotCraft turn {TurnId} after {TerminationReason}.", runId, turnId, reason);
            interruptError = ex;
            terminalResult = TimeoutTerminalResult.InterruptFailed;
        }

        await CompleteForcedTerminationAsync(
            runId,
            RunStatus.TimedOut,
            errorCode,
            reasonMessage,
            terminalResult,
            CancellationToken.None,
            interruptError);
    }

    private async Task CompleteForcedTerminationAsync(
        string runId,
        RunStatus status,
        string confirmedErrorCode,
        string reasonMessage,
        TimeoutTerminalResult terminalResult,
        CancellationToken ct,
        Exception? interruptError = null)
    {
        var terminationConfirmed = terminalResult is
            TimeoutTerminalResult.Cancelled or
            TimeoutTerminalResult.Failed or
            TimeoutTerminalResult.Completed or
            TimeoutTerminalResult.AlreadyTerminal;
        var errorCode = terminationConfirmed ? confirmedErrorCode : "appServerTerminationUnconfirmed";
        var message = BuildTerminationMessage(reasonMessage, terminalResult, interruptError);
        logger.LogInformation(
            "AppServer run {RunId} forced termination completed with {TerminalResult}; termination confirmed: {TerminationConfirmed}.",
            runId,
            terminalResult,
            terminationConfirmed);
        await FailRunAsync(
            runId,
            status,
            errorCode,
            message,
            allowRetry: terminationConfirmed,
            ct);
    }

    private async Task<TimeoutTerminalResult> WaitForTerminationTerminalAsync(
        string runId,
        string turnId,
        IDotCraftAppServerClient client,
        string errorCode,
        string reasonMessage,
        CancellationToken ct)
    {
        await foreach (var runEvent in client.ReadEventsAsync(ct))
        {
            if (runEvent.Type is "turn/completed" or "turn/failed" or "turn/cancelled" &&
                !string.Equals(runEvent.TurnId, turnId, StringComparison.Ordinal))
                continue;

            if (runEvent.Type == "turn/completed")
            {
                PublishRunStatus(runId, "timedOut", null, errorCode, "DotCraft turn completed after Oratorio forced termination.", 100, "DotCraft turn completed after Oratorio forced termination.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn completed after Oratorio forced termination.", null, CancellationToken.None);
                return TimeoutTerminalResult.Completed;
            }

            if (runEvent is DotCraftRunEvent<TurnNotification> { Type: "turn/failed" } failed)
            {
                PublishRunStatus(runId, "timedOut", null, errorCode, TurnError(failed.Params), 100, "DotCraft turn failed after Oratorio forced termination.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn failed after Oratorio forced termination.", null, CancellationToken.None);
                return TimeoutTerminalResult.Failed;
            }

            if (runEvent.Type == "turn/cancelled")
            {
                PublishRunStatus(runId, "timedOut", null, errorCode, "DotCraft turn was cancelled after Oratorio forced termination.", 100, "DotCraft turn acknowledged the interrupt.");
                await UpdateRunHeartbeatAsync(runId, 98, "DotCraft turn acknowledged the interrupt.", null, CancellationToken.None);
                return TimeoutTerminalResult.Cancelled;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/started" } started)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemStartedType, started.Params, streaming: true);
                await UpdateRunHeartbeatAsync(runId, 95, $"DotCraft item started after forced termination: {reasonMessage}", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemDeltaNotification> deltaEvent)
            {
                var delta = deltaEvent.Params.Delta;
                if (!string.IsNullOrEmpty(delta))
                {
                    PublishItemDelta(runId, runEvent.Type, deltaEvent.Params);
                }

                await UpdateRunHeartbeatAsync(runId, 95, $"DotCraft agent produced output after forced termination: {reasonMessage}", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<ItemNotification> { Type: "item/completed" } completed)
            {
                PublishItemSnapshot(runId, DrawerEvent.ItemCompletedType, completed.Params, streaming: false);
                await UpdateRunHeartbeatAsync(runId, 96, $"DotCraft item completed after forced termination: {reasonMessage}", null, CancellationToken.None);
                continue;
            }

            if (runEvent is DotCraftRunEvent<PlanUpdatedNotification> { Type: "plan/updated" } plan)
            {
                PublishPlan(runId, plan.Params);
            }
        }

        return TimeoutTerminalResult.Disconnected;
    }

    private static string BuildTerminationMessage(string reasonMessage, TimeoutTerminalResult result, Exception? interruptError = null) =>
        result switch
        {
            TimeoutTerminalResult.Cancelled => $"{reasonMessage} DotCraft turn was interrupted and cancelled.",
            TimeoutTerminalResult.Failed => $"{reasonMessage} DotCraft turn failed after the interrupt.",
            TimeoutTerminalResult.Completed => $"{reasonMessage} DotCraft turn completed after forced termination and the late result was discarded.",
            TimeoutTerminalResult.AlreadyTerminal => $"{reasonMessage} DotCraft reported that the persisted turn was already terminal.",
            TimeoutTerminalResult.Disconnected => $"{reasonMessage} DotCraft turn interrupt was requested, but the AppServer disconnected before a terminal notification.",
            TimeoutTerminalResult.WaitTimedOut => $"{reasonMessage} DotCraft turn interrupt was requested, but no terminal notification arrived within 30 seconds.",
            TimeoutTerminalResult.InterruptFailed => $"{reasonMessage} DotCraft turn interrupt failed: {interruptError!.Message}.",
            TimeoutTerminalResult.MissingTurn => $"{reasonMessage} No DotCraft turn was available to interrupt.",
            _ => reasonMessage
        };
}
