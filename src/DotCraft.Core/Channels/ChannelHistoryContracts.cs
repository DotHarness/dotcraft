namespace DotCraft.Channels;

public sealed class ChannelHistoryCapabilitySnapshot
{
    public int MaxMessages { get; set; }
}

public sealed record ChannelHistoryRequest(string Target, int Limit, string? Before);

public sealed record ChannelHistoryEntry(
    string Text,
    string? MessageId = null,
    string? SenderId = null,
    string? SenderName = null,
    string? SentAt = null,
    bool? FromBot = null);

public sealed class ChannelHistoryResult
{
    public IReadOnlyList<ChannelHistoryEntry> Messages { get; init; } = [];

    /// <summary>Cursor for the page before <see cref="Messages"/>; null when no older messages exist.</summary>
    public string? Before { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Succeeded => ErrorCode == null;

    public static ChannelHistoryResult Fail(string code, string message) => new() { ErrorCode = code, ErrorMessage = message };
}
