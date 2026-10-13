using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Dreams;

/// <summary>
/// Runs a Dreams run's model work through Session Core so traces are visible in Dashboard.
/// </summary>
public interface IDreamsRunner
{
    string ModelId { get; }

    Task<DreamsGenerationResult> GenerateAsync(
        DreamsRunInput input,
        string modelId,
        Action<DreamsRunSessionBinding>? onSessionBinding = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs one Turn on a new internal Session Thread for each run that enters model work.
/// </summary>
public sealed class DreamsSessionRunner(
    ISessionService sessionService,
    SessionPersistenceService persistence,
    ChatClientRegistry chatClientRegistry,
    AppConfig config,
    DreamsRunRegistry runRegistry,
    ILogger<DreamsSessionRunner>? logger = null) : IDreamsRunner
{
    public string ModelId => chatClientRegistry.ResolveConsolidationModel(config);

    public async Task<DreamsGenerationResult> GenerateAsync(
        DreamsRunInput input,
        string modelId,
        Action<DreamsRunSessionBinding>? onSessionBinding = null,
        CancellationToken cancellationToken = default)
    {
        var threadId = SessionIdGenerator.NewThreadId();
        var run = runRegistry.Register(threadId, input);
        var created = false;
        string? turnId = null;
        try
        {
            await CreateRunThreadAsync(input, modelId, threadId, cancellationToken).ConfigureAwait(false);
            created = true;
            onSessionBinding?.Invoke(new DreamsRunSessionBinding(threadId, null));

            string? failure = null;
            string? errorType = null;
            SessionTurn? completed = null;
            await foreach (var evt in sessionService.SubmitInputAsync(
                               threadId,
                               [new TextContent(DreamsSessionInstructions.BuildTurnPrompt(input))],
                               ct: cancellationToken).ConfigureAwait(false))
            {
                switch (evt.EventType)
                {
                    case SessionEventType.TurnStarted:
                        turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                        onSessionBinding?.Invoke(new DreamsRunSessionBinding(threadId, turnId));
                        break;
                    case SessionEventType.TurnFailed:
                        turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                        failure = evt.TurnFailedPayload?.Error ?? "dream_session_turn_failed";
                        errorType = "turn_failed";
                        break;
                    case SessionEventType.TurnCancelled:
                        turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                        failure = evt.TurnCancelledPayload?.Reason ?? "dream_session_turn_cancelled";
                        errorType = "turn_cancelled";
                        break;
                    case SessionEventType.TurnCompleted:
                        turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                        completed = evt.TurnPayload;
                        break;
                }
            }

            return completed != null && failure == null
                ? new DreamsGenerationResult(true, Summary(completed), ThreadId: threadId, TurnId: turnId, Diagnostics: run.Diagnostics, Usage: completed.TokenUsage)
                : new DreamsGenerationResult(false, null, failure ?? "dream_session_turn_incomplete", errorType ?? "turn_incomplete", threadId, turnId, run.Diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Dreams session-backed generation failed.");
            return new DreamsGenerationResult(false, null, ex.Message, "internal_error", created ? threadId : null, turnId, run.Diagnostics);
        }
        finally
        {
            if (created)
                await ArchiveAsync(threadId).ConfigureAwait(false);
            runRegistry.Unregister(threadId);
        }
    }

    private async Task CreateRunThreadAsync(DreamsRunInput input, string modelId, string threadId, CancellationToken cancellationToken)
    {
        var thread = await sessionService.CreateThreadAsync(
                new SessionIdentity
                {
                    ChannelName = DreamsConstants.ChannelName,
                    UserId = DreamsConstants.InternalUserId,
                    WorkspacePath = input.WorkspacePath,
                    ChannelContext = $"dreams:{input.RunId}"
                },
                new ThreadConfiguration
                {
                    Mode = "agent",
                    Model = modelId,
                    MemoryEnabled = false,
                    ToolProfile = DreamsConstants.ToolProfileName,
                    UseToolProfileOnly = true,
                    ApprovalPolicy = ApprovalPolicy.AutoApprove,
                    AgentInstructions = DreamsSessionInstructions.SystemPrompt
                },
                HistoryMode.Server,
                threadId: threadId,
                displayName: $"[internal] Dreams {input.Trigger} run",
                ct: cancellationToken)
            .ConfigureAwait(false);

        thread.Metadata[ThreadVisibility.InternalMetadataKey] = DreamsConstants.InternalMetadataValue;
        thread.Metadata[DreamsConstants.RunIdMetadataKey] = input.RunId;
        thread.Metadata[DreamsConstants.TriggerMetadataKey] = input.Trigger;
        await persistence.SaveThreadAsync(thread, cancellationToken).ConfigureAwait(false);
    }

    private static string? Summary(SessionTurn turn) =>
        turn.Items.LastOrDefault(static item => item.Type == ItemType.AgentMessage)?.AsAgentMessage?.Text?.Trim() is { Length: > 0 } text
            ? text
            : null;

    private async Task ArchiveAsync(string threadId)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await sessionService.ArchiveThreadAsync(threadId, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to archive Dreams run thread {ThreadId}.", threadId);
        }
    }
}
