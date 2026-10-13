using System.Globalization;
using System.Text;
using DotCraft.Sessions;

namespace DotCraft.Dreams;

public sealed class DreamsRunSession
{
    private const int MaxSearches = 20;
    private const int MaxReads = 25;
    private const int MaxToolResultChars = 12_000;
    private const int MaxTotalEvidenceChars = 120_000;
    private const int DefaultSearchMatches = 20;
    private const int MaxSearchMatches = 50;
    private const int DefaultReadLimit = 120;
    private const int MaxReadLimit = 300;
    private const int MaxSnippetChars = 500;
    private const int MaxMessageChars = 2_000;

    private readonly ThreadStore _threadStore;
    private readonly object _sync = new();
    private readonly HashSet<string> _evidenceThreadIds = new(StringComparer.Ordinal);
    private int _searchCount;
    private int _readCount;
    private int _evidenceChars;

    internal DreamsRunSession(DreamsRunInput input, ThreadStore threadStore)
    {
        Input = input;
        _threadStore = threadStore;
    }

    public DreamsRunInput Input { get; }

    public DreamsRunDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
                return new DreamsRunDiagnostics(_evidenceThreadIds.Order(StringComparer.Ordinal).ToArray(), _searchCount, _readCount);
        }
    }

    public async Task<string> SearchAsync(
        string query,
        IReadOnlyList<string>? threadIds,
        int maxMatches,
        CancellationToken cancellationToken)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return "Error: query is required.";
        var targets = threadIds is { Count: > 0 } ? threadIds.Distinct(StringComparer.Ordinal).ToArray() : ManifestThreadIds();
        if (targets.FirstOrDefault(id => !IsManifestThread(id)) is { } unknown)
            return NotInManifest(unknown);
        if (!TryConsume(ref _searchCount, MaxSearches))
            return "Error: This run's transcript search budget is used up.";

        maxMatches = Math.Clamp(maxMatches <= 0 ? DefaultSearchMatches : maxMatches, 1, MaxSearchMatches);
        var sb = new StringBuilder();
        var matches = 0;
        foreach (var threadId in targets)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var line in _threadStore.ReadRolloutItemsAsync(threadId, ThreadStore.ContentPrefilter(term), cancellationToken)
                               .ConfigureAwait(false))
            {
                if (FormatMessage(line.Item)?.ReplaceLineEndings(" ") is not { } text)
                    continue;
                var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index < 0 || !seen.Add(line.Item.Id))
                    continue;

                sb.Append(threadId).Append(" turn ").Append(line.TurnId).Append(' ')
                    .Append(FormatDate(line.Item.CreatedAt)).Append(' ')
                    .AppendLine(Excerpt(text, index));
                RecordEvidence(threadId);
                if (++matches >= maxMatches)
                {
                    sb.AppendLine($"[stopped at {maxMatches} matches]");
                    return LimitResult(sb.ToString());
                }
            }
        }

        return matches == 0 ? $"No matches for \"{term}\"." : LimitResult(sb.ToString());
    }

    public async Task<string> ReadAsync(string threadId, int offset, int limit, CancellationToken cancellationToken)
    {
        if (!IsManifestThread(threadId))
            return NotInManifest(threadId);
        if (!TryConsume(ref _readCount, MaxReads))
            return "Error: This run's transcript read budget is used up.";

        offset = Math.Max(1, offset);
        limit = Math.Clamp(limit <= 0 ? DefaultReadLimit : limit, 1, MaxReadLimit);
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        var more = false;
        await foreach (var line in _threadStore.ReadRolloutItemsAsync(threadId, ct: cancellationToken).ConfigureAwait(false))
        {
            if (FormatMessage(line.Item) is not { } text || !seen.Add(line.Item.Id) || ++number < offset)
                continue;
            if (number >= offset + limit)
            {
                more = true;
                break;
            }

            sb.Append(number.ToString(CultureInfo.InvariantCulture)).Append(". ")
                .Append(FormatDate(line.Item.CreatedAt)).Append(' ')
                .AppendLine(Trim(text, MaxMessageChars));
        }

        RecordEvidence(threadId);
        if (sb.Length == 0)
            return number < offset ? $"{threadId} has {number} messages; offset {offset} is past the end." : $"{threadId} has no messages.";
        sb.AppendLine(more ? $"[more: call ReadTranscript with offset={offset + limit}]" : "[end of transcript]");
        return LimitResult($"{threadId} messages from {offset}:\n{sb}");
    }

    private static string? FormatMessage(SessionItem item) => item.Payload switch
    {
        UserMessagePayload { Text: { Length: > 0 } text } when item.Status == ItemStatus.Completed => "USER: " + text.Trim(),
        AgentMessagePayload { Text: { Length: > 0 } text } when item.Status == ItemStatus.Completed => "ASSISTANT: " + text.Trim(),
        ToolCallPayload call => $"TOOL_CALL {call.ToolName}: {Trim(call.Arguments?.ToJsonString() ?? "{}", MaxSnippetChars)}",
        ToolResultPayload { Result: { Length: > 0 } result } toolResult =>
            $"TOOL_RESULT {(toolResult.Success ? "ok" : "failed")}: {Trim(result, MaxSnippetChars)}",
        _ => null
    };

    private static string Excerpt(string text, int index)
    {
        var start = Math.Max(0, index - MaxSnippetChars / 4);
        var end = Math.Min(text.Length, start + MaxSnippetChars);
        return (start > 0 ? "..." : string.Empty) + text[start..end] + (end < text.Length ? "..." : string.Empty);
    }

    private static string Trim(string text, int maxChars)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxChars ? singleLine : singleLine[..maxChars] + "...";
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("[yyyy-MM-dd HH:mm]", CultureInfo.InvariantCulture);

    private string[] ManifestThreadIds() => Input.Threads.Select(static thread => thread.ThreadId).ToArray();

    private bool IsManifestThread(string threadId) =>
        Input.Threads.Any(thread => string.Equals(thread.ThreadId, threadId, StringComparison.Ordinal));

    private static string NotInManifest(string threadId) =>
        $"Error: {threadId} is not a thread in this run's manifest.";

    private bool TryConsume(ref int count, int max)
    {
        lock (_sync)
        {
            if (count >= max)
                return false;
            count++;
            return true;
        }
    }

    private void RecordEvidence(string threadId)
    {
        lock (_sync)
            _evidenceThreadIds.Add(threadId);
    }

    private string LimitResult(string text)
    {
        lock (_sync)
        {
            var remaining = MaxTotalEvidenceChars - _evidenceChars;
            if (remaining <= 0)
                return "Error: This run's transcript evidence budget is used up.";

            var maxChars = Math.Min(MaxToolResultChars, remaining);
            var result = text.Length <= maxChars ? text : text[..maxChars] + "\n[truncated: evidence budget limit reached]";
            _evidenceChars += result.Length;
            return result;
        }
    }
}
