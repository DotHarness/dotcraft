using DotCraft.Agents;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Result of a full-history compaction run.
/// </summary>
public sealed record FullCompactResult(
    IReadOnlyList<ChatMessage> SummarizedMessages,
    IReadOnlyList<ChatMessage> PreservedTail,
    string FormattedSummary,
    string RawSummary,
    int EstimatedTokensBefore,
    int EstimatedTokensAfter);

/// <summary>
/// Result envelope for a full compaction attempt.
/// </summary>
public sealed record FullCompactAttempt(FullCompactResult? Result, string? Reason)
{
    public static FullCompactAttempt Succeeded(FullCompactResult result) => new(result, null);

    public static FullCompactAttempt Unavailable(string reason) => new(null, reason);
}

/// <summary>
/// Manual fallback compactor that summarizes the entire visible history when
/// there is no older prefix for partial compaction to replace.
/// </summary>
public sealed class FullCompactor
{
    private const int MaxPromptTooLongRetries = 3;
    private readonly IChatClient _chatClient;
    private readonly MaintenanceForkRunner? _maintenanceForkRunner;
    private readonly TraceCollector? _traceCollector;
    private readonly CompactionConfig _config;

    public FullCompactor(
        IChatClient chatClient,
        MaintenanceForkRunner? maintenanceForkRunner = null,
        TraceCollector? traceCollector = null,
        CompactionConfig? config = null)
    {
        _chatClient = chatClient;
        _maintenanceForkRunner = maintenanceForkRunner;
        _traceCollector = traceCollector;
        _config = config ?? new CompactionConfig();
    }

    /// <summary>
    /// Summarizes the whole history and returns a single continuation summary
    /// message. The snapshot fork is used only when it covers the supplied
    /// history; manual snapshots often predate the latest assistant response.
    /// </summary>
    public async Task<FullCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        CancellationToken cancellationToken = default)
    {
        return await CompactAsync(messages, snapshot, threadId: null, cancellationToken);
    }

    public async Task<FullCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        string? threadId,
        CancellationToken cancellationToken = default)
    {
        return await CompactAsync(messages, snapshot, threadId, fallbackTools: null, cancellationToken);
    }

    public async Task<FullCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        string? threadId,
        IReadOnlyList<AITool>? fallbackTools,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return FullCompactAttempt.Unavailable("empty_history");

        var conversation = CompactionHistoryPreparation.Conversation(messages);
        var paired = MessageGrouper.EnsurePairing(conversation);
        if (paired.Count == 0)
            return FullCompactAttempt.Unavailable("no_summarizable_history");

        string? rawSummary;
        var selectionReason = CompactionHistoryPreparation.SnapshotFailure(snapshot, messages, _maintenanceForkRunner is not null);
        try
        {
            if (selectionReason is null)
            {
                var remainder = messages.Skip(snapshot!.Messages.Count).ToArray();
                var fork = await RunSnapshotForkAsync(snapshot, remainder, cancellationToken);
                if (fork.FallbackReason is null)
                    rawSummary = fork.Text;
                else if (MaintenanceForkFallbackReasons.ShouldFallbackToTrimmedCompaction(fork.FallbackReason))
                    rawSummary = await RunLegacySummaryAsync(paired, snapshot, fallbackTools, threadId, fork.FallbackReason, cancellationToken);
                else
                    return FullCompactAttempt.Unavailable(fork.FallbackReason);
            }
            else
                rawSummary = await RunLegacySummaryAsync(paired, snapshot, fallbackTools, threadId, selectionReason, cancellationToken);
        }
        catch (CompactionSummaryException ex)
        {
            return FullCompactAttempt.Unavailable(ex.Message);
        }

        if (string.IsNullOrWhiteSpace(rawSummary))
            return FullCompactAttempt.Unavailable("summary_unavailable");

        if (CompactionSummaryBudget.ExceedsMaxOutputTokens(rawSummary, _config))
            return FullCompactAttempt.Unavailable(CompactionSummaryBudget.TooLongReason);

        var formatted = CompactionPrompts.GetCompactUserSummaryMessage(
            rawSummary,
            transcriptPath: null,
            recentMessagesPreserved: false);
        var replacement = CompactionSummaryMessage.Create(formatted);

        return FullCompactAttempt.Succeeded(new FullCompactResult(
            SummarizedMessages: conversation,
            PreservedTail: [],
            FormattedSummary: formatted,
            RawSummary: rawSummary,
            EstimatedTokensBefore: MessageTokenEstimator.Estimate(messages),
            EstimatedTokensAfter: MessageTokenEstimator.Estimate([replacement])));
    }

    private async Task<MaintenanceForkResult> RunSnapshotForkAsync(
        PromptRequestSnapshot snapshot,
        IReadOnlyList<ChatMessage> messagesBeforeTask,
        CancellationToken cancellationToken)
    {
        var result = await _maintenanceForkRunner!.RunAsync(
            snapshot,
            new MaintenanceForkTask(
                MaintenanceForkTaskKind.ContextCompaction,
                BuildFullContextCompactionTaskInstructions(CompactionSummaryBudget.ResolveSummaryLimit(_config)))
            {
                InputBudgetTokens = CompactionSummaryBudget.ResolveSnapshotInputBudget(_config, snapshot),
                InputBudgetSource = "compaction.context_window_minus_summary_output",
                MaxOutputTokensOverride = CompactionSummaryBudget.ResolveMaxOutputTokens(_config, snapshot)
            },
            messagesBeforeTask,
            cancellationToken);

        return result;
    }

    private async Task<string?> RunLegacySummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        string? threadId,
        string selectionReason,
        CancellationToken cancellationToken)
    {
        var summaryPrompt = BuildFullContextCompactionTaskInstructions(CompactionSummaryBudget.ResolveSummaryLimit(_config));
        var taskMessage = BuildLegacyContextCompactionTaskMessage();
        var candidate = CompactionPreflightTrimmer.TrimSummaryInput(
            CompactionHistoryPreparation.Fallback(messages, _chatClient),
            summaryPrompt,
            _config,
            taskMessage);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await RunLegacySummaryOnceAsync(candidate, snapshot, fallbackTools, threadId, selectionReason, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    throw new InvalidOperationException("provider_timeout");

                throw;
            }
            catch (Exception ex) when (CompactionErrors.IsPromptTooLong(ex))
            {
                if (attempt == MaxPromptTooLongRetries)
                    throw new CompactionSummaryException("provider_context_window_exceeded", ex);

                candidate = CompactionMessageTruncator.TryTruncateOldestToolResult(candidate, out var toolTrimmed)
                    ? toolTrimmed
                    : CompactionMessageTruncator.TruncateOldestGroups(candidate);
                if (candidate.Count == 0)
                    throw new CompactionSummaryException("provider_context_window_exceeded", ex);
            }
            catch (Exception ex)
            {
                throw new CompactionSummaryException(CompactionProviderFailure.Reason(_chatClient, ex), ex);
            }
        }

    }

    private async Task<string?> RunLegacySummaryOnceAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        string? threadId,
        string selectionReason,
        CancellationToken cancellationToken)
    {
        var summaryPrompt = BuildFullContextCompactionTaskInstructions(CompactionSummaryBudget.ResolveSummaryLimit(_config));
        var summaryMessages = new List<ChatMessage>(messages.Count + 2)
        {
            new(ChatRole.System, summaryPrompt)
        };
        summaryMessages.AddRange(MessageGrouper.NormalizeFunctionCallArguments(messages));
        summaryMessages.Add(BuildLegacyContextCompactionTaskMessage());

        var sessionKey = CompactionTrace.ResolveSessionKey(threadId);
        _traceCollector?.RecordMaintenanceForkRequest(
            sessionKey,
            MaintenanceForkTaskKind.ContextCompaction,
            summaryPrompt,
            threadId,
            turnId: null,
            mode: "legacy",
            modelId: null,
            providerId: null,
            snapshotMessageCount: messages.Count,
            extraTailMessageCount: 0,
            tools: SelectLegacyTools(snapshot, fallbackTools),
            baseInstructionsFingerprint: null,
            toolFingerprint: null,
            snapshotSource: snapshot?.SnapshotSource,
            snapshotInvalidReason: snapshot?.SnapshotInvalidReason,
            requestPath: "fallback",
            selectionReason: selectionReason);

        try
        {
            var response = await _chatClient.GetResponseAsync(
                summaryMessages,
                BuildLegacyOptions(snapshot, fallbackTools, _config),
                cancellationToken);
            _traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                MaintenanceForkTaskKind.ContextCompaction,
                response,
                CompactionTrace.ClassifyFallbackReason(response));
            return response?.Text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                MaintenanceForkTaskKind.ContextCompaction,
                "provider_timeout");
            throw;
        }
        catch (OperationCanceledException)
        {
            _traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                MaintenanceForkTaskKind.ContextCompaction,
                "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                MaintenanceForkTaskKind.ContextCompaction,
                CompactionProviderFailure.Reason(_chatClient, ex),
                providerError: ex.Message);
            throw;
        }
    }

    private static ChatMessage BuildLegacyContextCompactionTaskMessage() =>
        MaintenanceForkRunner.BuildTaskMessage(new MaintenanceForkTask(
            MaintenanceForkTaskKind.ContextCompaction,
            "Summarize the complete conversation visible above according to the system instructions. "
            + "Return only the requested handoff summary. Do not call tools."));

    private static ChatOptions BuildLegacyOptions(
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        CompactionConfig config)
    {
        var tools = SelectLegacyTools(snapshot, fallbackTools);
        var options = new ChatOptions
        {
            Tools = tools,
            MaxOutputTokens = CompactionSummaryBudget.ResolveMaxOutputTokens(config, snapshot)
        };
        if (tools is { Count: > 0 })
            ChatOptionsToolChoice.DisableOpenAIToolChoice(options);
        return options;
    }

    private static List<AITool>? SelectLegacyTools(
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools)
    {
        if (snapshot?.Tools is { Count: > 0 })
            return snapshot.Tools.ToList();

        return fallbackTools is { Count: > 0 }
            ? fallbackTools.ToList()
            : null;
    }

    private static string BuildFullContextCompactionTaskInstructions(int maxOutputTokens)
    {
        var prompt = CompactionPrompts.GetCompactPrompt(maxOutputTokens);
        return $"""
{prompt}

Compaction boundary:
- Summarize the complete conversation visible above.
- The resulting summary will replace the current model-visible history.
- Workspace instruction messages are supplied separately; do not copy them into the summary.
- Return only the requested handoff summary.
""";
    }
}
