using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private const int SearchSnippetCharsBefore = 48;
    private const int SearchSnippetCharsAfter = 96;

    public async Task<string?> FindContentMatchAsync(string threadId, string searchTerm, CancellationToken ct = default)
    {
        using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var path = _rolloutStore.ResolveExistingPath(threadId);
        if (path == null)
            return null;

        // Rollout strings are JSON-escaped, so raw lines are matched against the term escaped the same way.
        var escapedTerm = JsonSerializer.Serialize(searchTerm, SessionJsonOptions.Default)[1..^1];
        await foreach (var line in RolloutItemReader.ReadAsync(
                           path,
                           rawLine => rawLine.Contains(escapedTerm, StringComparison.OrdinalIgnoreCase),
                           ct))
        {
            var text = line.Item.Type switch
            {
                ItemType.UserMessage => line.Item.AsUserMessage?.Text,
                ItemType.AgentMessage => line.Item.AsAgentMessage?.Text,
                _ => null
            };
            if (text != null && ExcerptAroundMatch(text, searchTerm) is { } snippet)
                return snippet;
        }

        return null;
    }

    private static string? ExcerptAroundMatch(string text, string searchTerm)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var index = normalized.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var start = Math.Max(0, index - SearchSnippetCharsBefore);
        var end = Math.Min(normalized.Length, index + searchTerm.Length + SearchSnippetCharsAfter);
        if (start > 0 && char.IsLowSurrogate(normalized[start]))
            start--;
        if (end < normalized.Length && char.IsLowSurrogate(normalized[end]))
            end++;

        return (start > 0 ? "... " : string.Empty)
            + normalized[start..end].Trim()
            + (end < normalized.Length ? " ..." : string.Empty);
    }
}
