using System.Text;
using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed record BoardActor(string RootId, string ThreadId, string Path);
internal sealed record BoardPostMetadata(string MessageId, string ChannelName, string Author, string ThreadId, DateTimeOffset CreatedAt);
internal sealed record BoardPost(BoardPostMetadata Metadata, string Text);
internal sealed record BoardWriteResult(BoardPostMetadata Metadata, string[] Recipients, bool Created);
internal sealed record BoardRequest(
    string Operation,
    string? ChannelName = null,
    string? ThreadId = null,
    string? MessageId = null,
    string? Text = null,
    string? Description = null,
    string? NewChannelName = null,
    string? Query = null,
    string? Author = null,
    string? AfterMessageId = null,
    string? Cursor = null,
    string? Sort = null,
    bool RecentFirst = true,
    int Limit = 20,
    int MaxCharsPerPost = 1000,
    int OffsetChars = 0,
    int LimitChars = 20000,
    bool Subscribe = true,
    string? TargetAgent = null,
    string[]? AgentsToNotify = null);

internal sealed class BoardException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static class BoardText
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void RequireFits(object value)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions).Length > 8000)
            throw new BoardException("board_output_budget", "Result metadata exceeds the output budget.");
    }

    public static int Count(string value) => value.EnumerateRunes().Count();
    public static string Slice(string value, int offset, int count) =>
        string.Concat(value.EnumerateRunes().Skip(offset).Take(count).Select(rune => rune.ToString()));

    public static object Preview(BoardPost post, int count) => new
    {
        post.Metadata.MessageId, post.Metadata.ChannelName, post.Metadata.Author,
        post.Metadata.ThreadId, post.Metadata.CreatedAt,
        TextPreview = Slice(post.Text, 0, count), NChars = Count(post.Text), Truncated = Count(post.Text) > count
    };

    public static void Channel(string? name)
    {
        if (string.IsNullOrEmpty(name) || Encoding.UTF8.GetByteCount(name) > 128
            || name.Trim() != name || name.Any(char.IsControl))
            throw new BoardException("board_invalid_argument", "Channel names require 1–128 UTF-8 bytes without edge whitespace or control characters.");
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new BoardException("board_invalid_argument", message);
    }
}
