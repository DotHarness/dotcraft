using Microsoft.Extensions.AI;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;

namespace DotCraft.Sessions;

internal sealed class PreSamplingCompactionRuntimeContext
{
    public required Func<
        IReadOnlyList<ChatMessage>,
        ChatOptions?,
        CancellationToken,
        Task<CompactionExecutionResult?>> TryCompactAsync { get; init; }

    public Func<
        IReadOnlyList<ChatMessage>,
        PromptRequestSnapshot,
        ChatOptions?,
        CancellationToken,
        Task<CompactionExecutionResult?>>? TryCompactWithSnapshotAsync { get; init; }

    public string? ProviderId { get; init; }

    public string? Mode { get; init; }

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public int? EstimatedInputTokens { get; init; }

    public Func<PromptRequestSnapshot, CancellationToken, Task>? CaptureSnapshotAsync { get; init; }
}

internal static class PreSamplingCompactionRuntimeScope
{
    private static readonly AsyncLocal<PreSamplingCompactionRuntimeContext?> CurrentContext = new();

    public static PreSamplingCompactionRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(PreSamplingCompactionRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        var foundationScope = StreamingSamplingRuntimeScope.Set(
            (messages, options, cancellationToken) => PrepareAsync(context, messages, options, cancellationToken));
        return new Scope(previous, foundationScope);
    }

    private static async Task<StreamingSamplingPreparation> PrepareAsync(
        PreSamplingCompactionRuntimeContext compaction,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var snapshotBeforeCompaction = PromptRequestSnapshot.Capture(
            messages,
            options,
            compaction.ProviderId,
            compaction.Mode,
            compaction.ThreadId,
            compaction.TurnId,
            compaction.EstimatedInputTokens);
        var execution = compaction.TryCompactWithSnapshotAsync is { } compactWithSnapshot
            ? await compactWithSnapshot(messages, snapshotBeforeCompaction, options, cancellationToken)
            : await compaction.TryCompactAsync(messages, options, cancellationToken);
        var neutralReplacement = execution?.Replacement as CompactionReplacement.Neutral;
        var preparedMessages = ModelRequestHistorySanitizer.Sanitize(
            neutralReplacement?.Messages ?? messages);
        if (compaction.CaptureSnapshotAsync is { } capture)
        {
            var snapshot = PromptRequestSnapshot.Capture(
                preparedMessages,
                options,
                compaction.ProviderId,
                compaction.Mode,
                compaction.ThreadId,
                compaction.TurnId,
                compaction.EstimatedInputTokens);
            await capture(snapshot, cancellationToken);
        }

        return new StreamingSamplingPreparation(
            preparedMessages,
            NeutralHistoryWasReplaced: neutralReplacement != null,
            HistoryWasReplaced: execution?.Replacement != null);
    }

    private sealed class Scope(
        PreSamplingCompactionRuntimeContext? previous,
        IDisposable foundationScope) : IDisposable
    {
        public void Dispose()
        {
            foundationScope.Dispose();
            CurrentContext.Value = previous;
        }
    }
}
