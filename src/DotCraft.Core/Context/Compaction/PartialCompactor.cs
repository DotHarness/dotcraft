using DotCraft.Agents;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Result of a partial compaction run.
/// </summary>
public sealed record PartialCompactResult(
    IReadOnlyList<ChatMessage> SummarizedPrefix,
    IReadOnlyList<ChatMessage> PreservedTail,
    string FormattedSummary,
    string RawSummary,
    int PrefixEstimatedTokens,
    int TailEstimatedTokens);

/// <summary>
/// Result envelope for a partial compaction attempt, including a machine-readable
/// reason when no summary can be produced.
/// </summary>
public sealed record PartialCompactAttempt(PartialCompactResult? Result, string? Reason)
{
    public static PartialCompactAttempt Succeeded(PartialCompactResult result) => new(result, null);

    public static PartialCompactAttempt Unavailable(string reason) => new(null, reason);
}

/// <summary>
/// Summarizes the older portion of a conversation while preserving a tail of
/// recent API-round groups verbatim.
/// </summary>
public sealed class PartialCompactor
{
    private const int MaxPromptTooLongRetries = 3;
    private readonly IChatClient _chatClient;
    private readonly CompactionConfig _config;
    private readonly MaintenanceForkRunner? _maintenanceForkRunner;
    private readonly TraceCollector? _traceCollector;

    public PartialCompactor(
        IChatClient chatClient,
        CompactionConfig config,
        MaintenanceForkRunner? maintenanceForkRunner = null,
        TraceCollector? traceCollector = null)
    {
        _chatClient = chatClient;
        _config = config;
        _maintenanceForkRunner = maintenanceForkRunner;
        _traceCollector = traceCollector;
    }

    /// <summary>
    /// Picks the split point in <paramref name="messages"/> that preserves
    /// <paramref name="config"/>.KeepRecent*... as the tail, returning the
    /// index of the first preserved message. Visible for tests.
    /// </summary>
    public static int CalculateSplitIndex(
        IReadOnlyList<ChatMessage> messages,
        CompactionConfig config)
    {
        if (messages.Count == 0)
            return 0;

        var groups = MessageGrouper.GroupByApiRound(messages);
        if (groups.Count == 0)
            return 0;

        long tailTokens = 0;
        int tailGroups = 0;
        int splitGroupIndex = groups.Count;

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            var nextTokens = tailTokens + group.EstimatedTokens;
            if (nextTokens > config.KeepRecentMaxTokens && tailGroups > 0)
                break;

            tailTokens = nextTokens;
            tailGroups++;
            splitGroupIndex = i;

            if (tailTokens >= config.KeepRecentMinTokens && tailGroups >= config.KeepRecentMinGroups)
                break;
        }

        // All groups may be tail; in that case there's nothing to summarize.
        if (splitGroupIndex == 0)
            return 0;

        var splitMessageIndex = 0;
        for (var i = 0; i < splitGroupIndex; i++)
            splitMessageIndex += groups[i].Messages.Count;

        return splitMessageIndex;
    }

    /// <summary>
    /// Runs the partial summary and returns either a summary result or a
    /// machine-readable reason explaining why no summary can be produced. The
    /// caller is responsible for replacing the prefix in the session's chat
    /// history when the attempt succeeds.
    /// </summary>
    public async Task<PartialCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return await CompactAsync(messages, snapshot: null, cancellationToken);
    }

    /// <summary>
    /// Runs partial summary using a captured request snapshot when available.
    /// The snapshot path preserves the main request prefix and appends only a
    /// maintenance task at the tail.
    /// </summary>
    public async Task<PartialCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        CancellationToken cancellationToken = default)
    {
        return await CompactAsync(messages, snapshot, threadId: null, cancellationToken);
    }

    public async Task<PartialCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        string? threadId,
        CancellationToken cancellationToken = default)
    {
        return await CompactAsync(messages, snapshot, threadId, fallbackTools: null, cancellationToken);
    }

    public async Task<PartialCompactAttempt> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        PromptRequestSnapshot? snapshot,
        string? threadId,
        IReadOnlyList<AITool>? fallbackTools,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return PartialCompactAttempt.Unavailable("empty_history");

        var splitIndex = CalculateSplitIndex(messages, _config);
        if (splitIndex <= 0)
            return PartialCompactAttempt.Unavailable("no_summarizable_prefix");

        var prefix = messages.Take(splitIndex).ToList();
        var tail = messages.Skip(splitIndex).ToList();

        if (prefix.Count == 0)
            return PartialCompactAttempt.Unavailable("no_summarizable_prefix");

        var paired = MessageGrouper.EnsurePairing(prefix);
        if (paired.Count == 0)
            return PartialCompactAttempt.Unavailable("no_summarizable_prefix");

        var prefixTokens = MessageTokenEstimator.Estimate(prefix);
        var tailTokens = MessageTokenEstimator.Estimate(tail);

        string? rawSummary;
        if (snapshot is not null
            && _maintenanceForkRunner is not null
            && TryBuildSnapshotRemainder(snapshot, messages, out var messagesBeforeTask))
        {
            var fork = await RunSnapshotForkAsync(snapshot, splitIndex, tail, messagesBeforeTask, cancellationToken);
            if (fork.FallbackReason is not null)
            {
                if (MaintenanceForkFallbackReasons.ShouldFallbackToTrimmedCompaction(fork.FallbackReason))
                    rawSummary = await RunLegacySummaryAsync(paired, snapshot, fallbackTools, threadId, cancellationToken);
                else
                    return PartialCompactAttempt.Unavailable(fork.FallbackReason);
            }
            else
            {
                rawSummary = fork.Text;
            }
        }
        else
        {
            rawSummary = await RunLegacySummaryAsync(paired, snapshot, fallbackTools, threadId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawSummary))
            return PartialCompactAttempt.Unavailable("summary_unavailable");

        if (CompactionSummaryBudget.ExceedsMaxOutputTokens(rawSummary, _config))
            return PartialCompactAttempt.Unavailable(CompactionSummaryBudget.TooLongReason);

        var formatted = CompactionPrompts.GetCompactUserSummaryMessage(
            rawSummary,
            transcriptPath: null,
            recentMessagesPreserved: tail.Count > 0);

        return PartialCompactAttempt.Succeeded(new PartialCompactResult(
            SummarizedPrefix: prefix,
            PreservedTail: tail,
            FormattedSummary: formatted,
            RawSummary: rawSummary,
            PrefixEstimatedTokens: prefixTokens,
            TailEstimatedTokens: tailTokens));
    }

    private async Task<MaintenanceForkResult> RunSnapshotForkAsync(
        PromptRequestSnapshot snapshot,
        int splitIndex,
        IReadOnlyList<ChatMessage> tail,
        IReadOnlyList<ChatMessage> messagesBeforeTask,
        CancellationToken cancellationToken)
    {
        var instructions = BuildContextCompactionTaskInstructions(splitIndex, tail);
        var result = await _maintenanceForkRunner!.RunAsync(
            snapshot,
            new MaintenanceForkTask(
                MaintenanceForkTaskKind.ContextCompaction,
                instructions)
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
        IReadOnlyList<ChatMessage> paired,
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        string? threadId,
        CancellationToken cancellationToken)
    {
        var candidate = paired.ToList();
        var taskMessage = BuildLegacyContextCompactionTaskMessage();
        candidate = CompactionPreflightTrimmer.TrimSummaryInput(
            candidate,
            CompactionPrompts.GetPartialCompactPrompt(),
            _config,
            taskMessage);
        for (var attempt = 0; attempt <= MaxPromptTooLongRetries; attempt++)
        {
            try
            {
                return await RunLegacySummaryOnceAsync(candidate, snapshot, fallbackTools, threadId, cancellationToken);
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
                    return null;

                candidate = CompactionMessageTruncator.TryTruncateOldestToolResult(candidate, out var toolTrimmed)
                    ? toolTrimmed
                    : CompactionMessageTruncator.TruncateOldestGroups(candidate);
                if (candidate.Count == 0)
                    return null;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private async Task<string?> RunLegacySummaryOnceAsync(
        IReadOnlyList<ChatMessage> paired,
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        string? threadId,
        CancellationToken cancellationToken)
    {
        var summaryPrompt = CompactionPrompts.GetPartialCompactPrompt();
        var summaryMessages = new List<ChatMessage>(paired.Count + 2)
        {
            new(ChatRole.System, summaryPrompt)
        };
        summaryMessages.AddRange(MessageGrouper.NormalizeFunctionCallArguments(paired));
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
            snapshotMessageCount: paired.Count,
            extraTailMessageCount: 0,
            tools: SelectLegacyTools(snapshot, fallbackTools),
            baseInstructionsFingerprint: null,
            toolFingerprint: null);

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
                ex.Message);
            throw;
        }
    }

    private static ChatMessage BuildLegacyContextCompactionTaskMessage() =>
        MaintenanceForkRunner.BuildTaskMessage(new MaintenanceForkTask(
            MaintenanceForkTaskKind.ContextCompaction,
            "Summarize the conversation visible above according to the system instructions. "
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

    private static bool TryBuildSnapshotRemainder(
        PromptRequestSnapshot snapshot,
        IReadOnlyList<ChatMessage> messages,
        out IReadOnlyList<ChatMessage> remainder)
    {
        remainder = [];
        if (snapshot.Messages.Count > messages.Count)
            return false;

        var candidateFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(messages, snapshot.Messages.Count);
        if (!string.Equals(candidateFingerprint, snapshot.MessageFingerprint, StringComparison.Ordinal))
            return false;

        remainder = messages
            .Skip(snapshot.Messages.Count)
            .Select(message => message.Clone())
            .ToArray();
        return true;
    }

    private static string BuildContextCompactionTaskInstructions(
        int splitIndex,
        IReadOnlyList<ChatMessage> tail)
    {
        var prompt = CompactionPrompts.GetPartialCompactPrompt();
        var firstTail = tail.FirstOrDefault();
        var tailHint = firstTail is null
            ? "No recent tail messages are preserved."
            : $"The preserved recent tail starts at message index {splitIndex} with role '{firstTail.Role}'. Do not summarize the preserved tail as completed work; it will remain verbatim after the summary.";

        return $"""
{prompt}

Compaction boundary:
- Summarize the conversation before message index {splitIndex}.
- {tailHint}
- Return only the requested handoff summary.
""";
    }
}
