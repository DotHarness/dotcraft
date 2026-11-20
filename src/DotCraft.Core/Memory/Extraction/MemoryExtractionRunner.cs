using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Memory.Extraction;

public interface IMemoryExtractionRunner
{
    /// <returns>The Turn the extraction forked after when it completed, or null when it was skipped or did not complete.</returns>
    Task<string?> RunAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken);
}

/// <summary>Runs memory extraction as an ephemeral fork of the Thread that adds nothing to the Thread itself.</summary>
public sealed class MemoryExtractionRunner(
    ISessionService sessions,
    IContributionRegistry contributions,
    MemoryStore workspaceStore,
    string dataPath,
    IModelRuntimeDiagnostics? diagnostics = null,
    ILogger? logger = null,
    Func<MemoryPolicy>? policy = null,
    string? userDataPath = null) : IMemoryExtractionRunner
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    public async Task<string?> RunAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken)
    {
        var parent = await sessions.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (parent.Turns.LastOrDefault() is not { Status: TurnStatus.Completed } forkPoint || forkPoint.Id == cursorTurnId)
            return null;

        var turnsSinceCursor = await CountTurnsAsync(threadId, cursorTurnId, cancellationToken).ConfigureAwait(false);
        var store = MemoryScopes.ForThread(parent.Configuration, workspaceStore, dataPath);
        var personal = MemoryScopes.PersonalStore(parent.Configuration?.MemoryPersonal ?? true, userDataPath, store);
        var current = policy?.Invoke() ?? MemoryPolicy.Default;
        var run = await ExtractAsync(parent, forkPoint, turnsSinceCursor, store.MemoryDirectoryPath, personal?.MemoryDirectoryPath, current, cancellationToken)
            .ConfigureAwait(false);
        if (run is not { } completed)
            return null;

        RecordOutcome(parent.Id, forkPoint.Id, completed.ForkId, completed.Outcome, completed.Turn);
        return completed.Outcome == "completed" ? forkPoint.Id : null;
    }

    private async Task<int> CountTurnsAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var turn in sessions
                           .ReadThreadTurnsAsync(threadId, ThreadHistorySortDirection.Descending, includeItems: false, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (turn.Id == cursorTurnId)
                break;
            count++;
        }

        return count;
    }

    private async Task<ExtractionRun?> ExtractAsync(
        SessionThread parent,
        SessionTurn forkPoint,
        int turnCount,
        string storeDirectory,
        string? personalDirectory,
        MemoryPolicy current,
        CancellationToken cancellationToken)
    {
        SessionThread fork;
        try
        {
            fork = await sessions.ForkThreadAsync(
                parent.Id,
                new ThreadForkOptions
                {
                    Ephemeral = true,
                    Purpose = ThreadForkPurpose.MemoryExtraction,
                    ForkPoint = new ThreadForkPoint { TurnId = forkPoint.Id }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            logger?.LogDebug("Skipped memory extraction for thread {ThreadId}: Turn {TurnId} is no longer the latest completed Turn.", parent.Id, forkPoint.Id);
            return null;
        }

        try
        {
            var message = MemoryExtractionPrompt.Build(
                turnCount,
                storeDirectory,
                MemoryFiles.List(storeDirectory, current.TypeNames),
                personalDirectory,
                personalDirectory is null ? [] : MemoryFiles.List(personalDirectory, current.TypeNames),
                current.ExtractionGuidance);
            var (outcome, turn) = await RunForkAsync(
                fork.Id,
                message,
                personalDirectory is null ? [storeDirectory] : [storeDirectory, personalDirectory],
                cancellationToken).ConfigureAwait(false);
            return new ExtractionRun(fork.Id, outcome, turn);
        }
        finally
        {
            await DeleteForkAsync(fork.Id).ConfigureAwait(false);
        }
    }

    private async Task<(string Outcome, SessionTurn? Turn)> RunForkAsync(
        string forkId,
        string message,
        IReadOnlyList<string> storeDirectories,
        CancellationToken cancellationToken)
    {
        using var guard = contributions.Add<IToolPolicyEvaluator>(
            new MemoryExtractionToolPolicy(storeDirectories),
            ContributionOptions.ForThread(forkId));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);
        var outcome = "failed";
        SessionTurn? turn = null;
        try
        {
            await foreach (var evt in sessions.SubmitInputAsync(forkId, [new TextContent(message)], ct: timeout.Token)
                               .ConfigureAwait(false))
            {
                switch (evt.EventType)
                {
                    case SessionEventType.TurnCompleted:
                        outcome = "completed";
                        turn = evt.TurnPayload;
                        break;
                    case SessionEventType.TurnCancelled:
                        outcome = "cancelled";
                        turn = evt.TurnPayload;
                        break;
                    case SessionEventType.TurnFailed:
                        turn = evt.TurnPayload;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Memory extraction fork {ThreadId} timed out.", forkId);
        }

        return (outcome, turn);
    }

    private void RecordOutcome(
        string parentThreadId,
        string parentTurnId,
        string forkId,
        string outcome,
        SessionTurn? turn)
    {
        var writtenFiles = turn is null
            ? Array.Empty<string>()
            : MemoryUpdates.SucceededPaths(turn).Select(path => Path.GetFileName(path)).Distinct(StringComparer.Ordinal).ToArray();
        logger?.LogDebug(
            "Memory extraction for thread {ThreadId} ended {Outcome}, writing {FileCount} files.",
            parentThreadId,
            outcome,
            writtenFiles.Length);
        diagnostics?.Record(new ModelRuntimeDiagnostic(
            "memory_extraction.outcome",
            new Dictionary<string, object?>
            {
                ["sessionKey"] = forkId,
                ["parentThreadId"] = parentThreadId,
                ["parentTurnId"] = parentTurnId,
                ["threadId"] = forkId,
                ["turnId"] = turn?.Id,
                ["outcome"] = outcome,
                ["writtenFiles"] = writtenFiles
            }));
    }

    private async Task DeleteForkAsync(string forkId)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(CleanupTimeout);
            await sessions.DeleteThreadPermanentlyAsync(forkId, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete memory extraction fork {ThreadId}.", forkId);
        }
    }

    private sealed record ExtractionRun(string ForkId, string Outcome, SessionTurn? Turn);
}
