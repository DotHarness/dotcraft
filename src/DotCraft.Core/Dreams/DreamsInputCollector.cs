using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;

namespace DotCraft.Dreams;

/// <summary>
/// Collects read-only workspace evidence for a Dream run.
/// </summary>
public sealed class DreamsInputCollector(
    AppConfig config,
    string workspacePath,
    MemoryStore memoryStore,
    DreamStore dreamStore,
    ThreadStore threadStore)
{
    private const int MaxPreviewChars = 24_000;

    public async Task<DreamsRunInput> CollectAsync(
        DreamsRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var explicitMemory = TrimPreview(memoryStore.ReadLongTerm(), MaxPreviewChars);
        var hasMemoryHistory = !string.IsNullOrWhiteSpace(memoryStore.ReadHistory());
        var existingDream = TrimPreview(dreamStore.ReadDream(), MaxPreviewChars);
        var topicFiles = dreamStore.ListTopicFiles();
        var threads = new List<DreamsThreadInput>();
        var requestedThreadIds = request?.ThreadIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var lookback = Math.Max(1, request?.ThreadLookbackCount ?? config.Dreams.ThreadLookbackCount);

        var summaries = await threadStore.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var candidates = summaries.Where(IsEligibleSummary);
        if (requestedThreadIds is { Count: > 0 })
        {
            candidates = candidates.Where(summary => requestedThreadIds.Contains(summary.Id));
        }
        else
        {
            candidates = candidates
                .OrderByDescending(static summary => summary.LastActiveAt)
                .Take(lookback);
        }

        foreach (var summary in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var thread = await threadStore.LoadThreadAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (thread == null || !IsEligibleThread(thread))
                continue;

            var completedTurns = thread.Turns.Where(static turn => turn.Status == TurnStatus.Completed).ToArray();
            threads.Add(new DreamsThreadInput(
                thread.Id,
                thread.DisplayName,
                thread.OriginChannel,
                thread.Status,
                thread.CreatedAt,
                thread.LastActiveAt,
                completedTurns.Length));
        }

        return new DreamsRunInput(
            explicitMemory,
            existingDream,
            topicFiles,
            threads,
            threads.Sum(static thread => thread.CompletedTurnCount),
            hasMemoryHistory,
            NormalizeInstructions(request?.Instructions));
    }

    private bool IsEligibleSummary(ThreadSummary summary)
    {
        if (!string.Equals(
                Path.GetFullPath(summary.WorkspacePath),
                Path.GetFullPath(workspacePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ThreadVisibility.IsInternal(summary))
            return false;

        return !IsSubAgent(summary.Source, summary.OriginChannel);
    }

    private bool IsEligibleThread(SessionThread thread)
    {
        if (!string.Equals(
                Path.GetFullPath(thread.WorkspacePath),
                Path.GetFullPath(workspacePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (thread.HistoryMode != HistoryMode.Server)
            return false;

        if (ThreadVisibility.IsInternal(thread))
            return false;

        return !IsSubAgent(thread.Source, thread.OriginChannel);
    }

    private static bool IsSubAgent(ThreadSource source, string? originChannel) =>
        string.Equals(source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(originChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static string TrimPreview(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars == 0)
            return string.Empty;
        return value.Length <= maxChars
            ? value
            : value[..maxChars] + "\n[trimmed]";
    }

    private static string? NormalizeInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            return null;

        var trimmed = instructions.Trim();
        return trimmed.Length <= 4_000 ? trimmed : trimmed[..4_000] + "\n[trimmed]";
    }
}
