using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Dreams;

/// <summary>
/// Runs a Dreams model generation through Session Core so traces are visible in Dashboard.
/// </summary>
public interface IDreamsRunner
{
    string ModelId { get; }

    Task<DreamsGenerationResult> GenerateAsync(
        DreamsRunInput input,
        string runId,
        string trigger,
        string? outputStoreId = null,
        string? modelId = null,
        Action<DreamsRunSessionBinding>? onSessionBinding = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates one internal Session Thread for each actual Dreams model run.
/// </summary>
public sealed class DreamsSessionRunner(
    ISessionService sessionService,
    SessionPersistenceService persistence,
    ChatClientRegistry chatClientRegistry,
    AppConfig config,
    string workspacePath,
    DreamsRunRegistry runRegistry,
    DreamStore dreamStore,
    ILogger<DreamsSessionRunner>? logger = null) : IDreamsRunner
{
    public string ModelId => chatClientRegistry.ResolveConsolidationModel(config);

    public async Task<DreamsGenerationResult> GenerateAsync(
        DreamsRunInput input,
        string runId,
        string trigger,
        string? outputStoreId = null,
        string? modelId = null,
        Action<DreamsRunSessionBinding>? onSessionBinding = null,
        CancellationToken cancellationToken = default)
    {
        string? threadId = null;
        string? createdThreadId = null;
        string? registeredThreadId = null;
        string? turnId = null;
        var turnIds = new List<string>();
        var usage = new TokenUsageInfo();
        DreamStoreDescriptor? outputStore = null;
        try
        {
            var effectiveModelId = string.IsNullOrWhiteSpace(modelId) ? ModelId : modelId.Trim();
            outputStore = string.IsNullOrWhiteSpace(outputStoreId)
                ? dreamStore.CreateOutputStore(runId, DateTimeOffset.UtcNow)
                : dreamStore.GetStoreDescriptor(outputStoreId);
            var runWorkspace = await runRegistry.PrepareRunWorkspaceAsync(
                    runId,
                    input,
                    outputStore,
                    workspacePath,
                    cancellationToken)
                .ConfigureAwait(false);
            registeredThreadId = SessionIdGenerator.NewThreadId();
            runRegistry.Register(registeredThreadId, runWorkspace, input);

            var thread = await CreateRunThreadAsync(runId, trigger, effectiveModelId, registeredThreadId, cancellationToken)
                .ConfigureAwait(false);
            threadId = thread.Id;
            createdThreadId = thread.Id;
            onSessionBinding?.Invoke(new DreamsRunSessionBinding(threadId, null));

            var pruning = await RunPassAsync(
                    threadId,
                    BuildPruningPrompt(input, runId, trigger, runWorkspace),
                    onSessionBinding,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(pruning.TurnId))
            {
                turnId = pruning.TurnId;
                turnIds.Add(pruning.TurnId);
            }
            if (pruning.Usage != null)
                usage += pruning.Usage;
            if (!pruning.Succeeded)
            {
                return DreamsGenerationResult.Failed(
                    pruning.Message ?? "dream_pruning_failed",
                    threadId,
                    turnId,
                    runRegistry.GetDiagnostics(threadId ?? registeredThreadId),
                    usage,
                    turnIds,
                    pruning.ErrorType ?? "pruning_failed",
                    outputStore.StoreId);
            }

            var consolidation = await RunPassAsync(
                    threadId,
                    BuildConsolidationPrompt(input, runId, trigger, runWorkspace),
                    onSessionBinding,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(consolidation.TurnId))
            {
                turnId = consolidation.TurnId;
                turnIds.Add(consolidation.TurnId);
            }
            if (consolidation.Usage != null)
                usage += consolidation.Usage;
            if (!consolidation.Succeeded)
            {
                return DreamsGenerationResult.Failed(
                    consolidation.Message ?? "dream_consolidation_failed",
                    threadId,
                    turnId,
                    runRegistry.GetDiagnostics(threadId ?? registeredThreadId),
                    usage,
                    turnIds,
                    consolidation.ErrorType ?? "consolidation_failed",
                    outputStore.StoreId);
            }

            var index = dreamStore.ValidateStore(outputStore.StoreId);
            return DreamsGenerationResult.Success(
                index,
                historyEntry: null,
                threadId,
                turnId,
                diagnostics: runRegistry.GetDiagnostics(threadId ?? registeredThreadId),
                usage: usage,
                outputStoreId: outputStore.StoreId,
                turnIds: turnIds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Dreams session-backed generation failed.");
            return DreamsGenerationResult.Failed(
                ex.Message,
                threadId,
                turnId,
                runRegistry.GetDiagnostics(threadId ?? registeredThreadId),
                usage,
                turnIds,
                "internal_error",
                outputStore?.StoreId);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(createdThreadId))
            {
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await sessionService.ArchiveThreadAsync(createdThreadId, cleanupCts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogDebug(ex, "Failed to archive Dreams run thread {ThreadId}.", createdThreadId);
                }
            }

            if (!string.IsNullOrWhiteSpace(registeredThreadId))
                runRegistry.Unregister(registeredThreadId);
        }
    }

    private async Task<SessionThread> CreateRunThreadAsync(
        string runId,
        string trigger,
        string modelId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var identity = new SessionIdentity
        {
            ChannelName = DreamsConstants.ChannelName,
            UserId = DreamsConstants.InternalUserId,
            WorkspacePath = workspacePath,
            ChannelContext = $"dreams:{runId}"
        };

        var thread = await sessionService.CreateThreadAsync(
                identity,
                new ThreadConfiguration
                {
                    Mode = "agent",
                    Model = modelId,
                    ToolProfile = DreamsConstants.ToolProfileName,
                    UseToolProfileOnly = true,
                    ApprovalPolicy = ApprovalPolicy.AutoApprove,
                    AgentInstructions = DreamsSessionInstructions.SystemPrompt
                },
                HistoryMode.Server,
                threadId: threadId,
                displayName: $"[internal] Dreams {trigger} run",
                ct: cancellationToken)
            .ConfigureAwait(false);

        thread.Metadata[ThreadVisibility.InternalMetadataKey] = DreamsConstants.InternalMetadataValue;
        thread.Metadata[DreamsConstants.RunIdMetadataKey] = runId;
        thread.Metadata[DreamsConstants.TriggerMetadataKey] = trigger;
        await persistence.SaveThreadAsync(thread, cancellationToken).ConfigureAwait(false);
        return thread;
    }

    private async Task<DreamPassResult> RunPassAsync(
        string threadId,
        string prompt,
        Action<DreamsRunSessionBinding>? onSessionBinding,
        CancellationToken cancellationToken)
    {
        var completed = false;
        string? turnId = null;
        string? terminalFailure = null;
        string? errorType = null;
        TokenUsageInfo? usage = null;
        await foreach (var evt in sessionService.SubmitInputAsync(
                           threadId,
                           [new TextContent(prompt)],
                           ct: cancellationToken).ConfigureAwait(false))
        {
            if (evt.EventType == SessionEventType.TurnStarted)
            {
                turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                onSessionBinding?.Invoke(new DreamsRunSessionBinding(threadId, turnId));
                continue;
            }

            if (evt.EventType == SessionEventType.TurnFailed)
            {
                turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                terminalFailure = evt.TurnFailedPayload?.Error ?? "dream_session_turn_failed";
                errorType = "turn_failed";
                continue;
            }

            if (evt.EventType == SessionEventType.TurnCancelled)
            {
                turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                terminalFailure = evt.TurnCancelledPayload?.Reason ?? "dream_session_turn_cancelled";
                errorType = "turn_cancelled";
                continue;
            }

            if (evt.EventType == SessionEventType.TurnCompleted)
            {
                turnId = evt.TurnPayload?.Id ?? evt.TurnId ?? turnId;
                usage = evt.TurnPayload?.TokenUsage ?? usage;
                completed = true;
            }
        }

        return completed && string.IsNullOrWhiteSpace(terminalFailure)
            ? new DreamPassResult(true, turnId, null, null, usage)
            : new DreamPassResult(false, turnId, terminalFailure ?? "dream_session_turn_incomplete", errorType ?? "turn_incomplete", usage);
    }

    private static string BuildPruningPrompt(
        DreamsRunInput input,
        string runId,
        string trigger,
        DreamsRunWorkspace workspace)
    {
        return $$"""
Dream Run pruning pass:
- runId: {{runId}}
- trigger: {{trigger}}
- currentDateUtc: {{DateTimeOffset.UtcNow:yyyy-MM-dd}}

Read the manifest first:
{{workspace.ManifestPath}}

Read-only evidence roots:
- input snapshot: {{workspace.InputPath}}
- repository: use absolute paths under the workspace when needed

Writable output store:
- root: {{workspace.OutputStorePath}}
- write pruning notes to: PRUNING_NOTES.md

Task:
1. Inspect only enough evidence to identify stale, duplicated, contradictory, unsupported, or low-signal Dream memory.
2. Use grep/find/read tools with narrow queries. Do not exhaustively read every transcript.
3. Write PRUNING_NOTES.md in the writable output store. Include keep/remove/update guidance and concise source references.

Do not write INDEX.md during this pass.
Do not write outside the writable output store.
{{FormatAdditionalInstructions(input)}}
""";
    }

    private static string BuildConsolidationPrompt(
        DreamsRunInput input,
        string runId,
        string trigger,
        DreamsRunWorkspace workspace)
    {
        return $$"""
Dream Run consolidation pass:
- runId: {{runId}}
- trigger: {{trigger}}
- currentDateUtc: {{DateTimeOffset.UtcNow:yyyy-MM-dd}}

Read the manifest and PRUNING_NOTES.md:
- {{workspace.ManifestPath}}
- {{Path.Combine(workspace.OutputStorePath, "PRUNING_NOTES.md")}}

Writable output store:
- INDEX.md: complete compact Dream memory index
- memory/*.md: optional top-level topic files with durable passive details

Task:
1. Produce a complete candidate Dream memory store. The store is pending review and will not affect future sessions until applied.
2. INDEX.md must begin with "# Dream Memory", stay under 200 lines / 25 KB, and act as an index, not a transcript dump.
3. Preserve useful passive context, remove stale/low-signal items, and convert relative dates to absolute dates.
4. Put durable details in safe top-level topic files under memory/*.md only when the index would otherwise become too long.
5. Avoid secrets, credentials, raw logs, large code excerpts, sensitive personal profiling, and unsupported certainty.

Do not write outside the writable output store.
{{FormatAdditionalInstructions(input)}}
""";
    }

    private static string FormatAdditionalInstructions(DreamsRunInput input)
    {
        if (string.IsNullOrWhiteSpace(input.AdditionalInstructions))
            return string.Empty;

        return $"""

Additional user instructions for this Dream run:
{input.AdditionalInstructions.Trim()}
""";
    }

    private sealed record DreamPassResult(
        bool Succeeded,
        string? TurnId,
        string? Message,
        string? ErrorType,
        TokenUsageInfo? Usage);
}
