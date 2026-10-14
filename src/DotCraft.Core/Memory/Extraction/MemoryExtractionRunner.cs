using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Memory.Extraction;

internal interface IMemoryExtractionRunner
{
    /// <returns>The Turn the extraction forked after when it completed, or null when it was skipped or did not complete.</returns>
    Task<string?> RunAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken);
}

/// <summary>Runs memory extraction as an ephemeral fork of the Thread that adds nothing to the Thread itself.</summary>
internal sealed class MemoryExtractionRunner(
    ISessionService sessions,
    IContributionRegistry contributions,
    MemoryStore workspaceStore,
    string dataPath,
    IModelRuntimeDiagnostics? diagnostics = null,
    ILogger? logger = null) : IMemoryExtractionRunner
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    public async Task<string?> RunAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken)
    {
        var parent = await sessions.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var turns = parent.Turns.ToArray();
        var cursorIndex = Array.FindIndex(turns, turn => turn.Id == cursorTurnId);
        if (turns.Length == 0 || turns[^1].Status != TurnStatus.Completed || cursorIndex == turns.Length - 1)
            return null;

        var forkPoint = turns[^1];
        var store = MemoryScopes.ForThread(parent.Configuration, workspaceStore, dataPath);
        SessionThread fork;
        try
        {
            fork = await sessions.ForkThreadAsync(
                threadId,
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
            logger?.LogDebug("Skipped memory extraction for thread {ThreadId}: Turn {TurnId} is no longer the latest completed Turn.", threadId, forkPoint.Id);
            return null;
        }

        try
        {
            var (outcome, turn) = await RunForkAsync(
                fork.Id,
                MemoryExtractionPrompt.Build(turns.Length - 1 - cursorIndex, store.MemoryDirectoryPath, MemoryFiles.List(store.MemoryDirectoryPath)),
                store.MemoryDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            RecordOutcome(parent.Id, forkPoint.Id, fork.Id, outcome, turn);
            return outcome == "completed" ? forkPoint.Id : null;
        }
        finally
        {
            await DeleteForkAsync(fork.Id).ConfigureAwait(false);
        }
    }

    private async Task<(string Outcome, SessionTurn? Turn)> RunForkAsync(
        string forkId,
        string message,
        string storeDirectory,
        CancellationToken cancellationToken)
    {
        using var guard = contributions.Add<IToolPolicyEvaluator>(
            new MemoryExtractionToolPolicy(storeDirectory),
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
}
