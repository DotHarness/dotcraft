using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class AgentMessageBoardStore
{
    public object Read(BoardActor actor, BoardRequest request, int scale = 1)
    {
        BoardText.Require(request.Limit > 0 && request.MaxCharsPerPost > 0 && request.LimitChars > 0 && request.OffsetChars >= 0,
            "Limits must be positive and offsets must be nonnegative.");
        var limit = Math.Max(1, Math.Min(request.Limit, 50) / scale);
        var preview = Math.Max(1, Math.Min(request.MaxCharsPerPost, 20000) / scale);
        var offset = DecodeCursor(request.Cursor);
        using var connection = Open();
        CheckDeleted(connection, null, actor.RootId);
        if (request.Operation == "readPost")
        {
            var post = LoadPost(connection, null, actor.RootId, request.MessageId);
            var text = BoardText.Slice(post.Text, request.OffsetChars, Math.Max(1, Math.Min(request.LimitChars, 20000) / scale));
            return new { post.Metadata.MessageId, post.Metadata.ChannelName, post.Metadata.Author, post.Metadata.ThreadId,
                post.Metadata.CreatedAt, Text = text, NChars = BoardText.Count(post.Text), NextOffsetChars = request.OffsetChars + BoardText.Count(text) };
        }
        if (request.Operation == "getChannels") return Channels(connection, actor.RootId, request, limit, offset);
        if (request.Operation == "listThreads") return Threads(connection, actor.RootId, request, limit, offset, preview);
        if (request.Operation == "readThread")
        {
            var root = LoadPost(connection, null, actor.RootId, request.ThreadId);
            BoardText.Require(root.Metadata.ThreadId == request.ThreadId, "threadId must identify a top-level post.");
            var replies = Posts(connection, actor.RootId, "topic=$topic AND id<>$topic", "DESC", limit, offset, ("$topic", request.ThreadId));
            var page = Page(replies.Select(post => BoardText.Preview(post, preview)).ToList(), limit, offset);
            return new { RootPost = BoardText.Preview(root, preview), page.Results, page.NReturned, page.HasMore, page.NextCursor };
        }
        if (request.Operation != "searchPosts") throw new BoardException("board_invalid_argument", "Unknown board operation.");
        long after = 0;
        if (request.AfterMessageId != null)
        {
            _ = LoadPost(connection, null, actor.RootId, request.AfterMessageId);
            using var find = Command(connection, null, "SELECT seq FROM agent_board_posts WHERE root_id=$root AND id=$id",
                ("$root", actor.RootId), ("$id", request.AfterMessageId));
            after = (long)find.ExecuteScalar()!;
        }
        var posts = Posts(connection, actor.RootId,
            "($channel IS NULL OR channel=$channel) AND ($author IS NULL OR author=$author) AND seq>$after AND instr(body_search,$query)>0",
            "DESC", limit, offset, ("$channel", request.ChannelName), ("$author", request.Author),
            ("$after", after), ("$query", (request.Query ?? "").ToUpperInvariant()));
        return Page(posts.Select(post => BoardText.Preview(post, preview)).ToList(), limit, offset);
    }

    private static object Channels(SqliteConnection connection, string root, BoardRequest request, int limit, int offset)
    {
        connection.CreateFunction<string, string>("board_upper", value => value.ToUpperInvariant());
        var direction = request.RecentFirst ? "DESC" : "ASC";
        using var command = Command(connection, null, $"""
            SELECT c.name,c.description,c.author,c.created_at,
                (SELECT count(*) FROM agent_board_posts p WHERE p.root_id=c.root_id AND p.channel=c.name),
                (SELECT id FROM agent_board_posts p WHERE p.root_id=c.root_id AND p.channel=c.name ORDER BY seq DESC LIMIT 1),
                COALESCE((SELECT max(created_at) FROM agent_board_posts p WHERE p.root_id=c.root_id AND p.channel=c.name),c.created_at) activity
            FROM agent_board_channels c WHERE c.root_id=$root AND instr(board_upper(c.name),$query)>0
            ORDER BY activity {direction},c.name {direction} LIMIT $limit OFFSET $offset
            """, ("$root", root), ("$query", (request.Query ?? "").ToUpperInvariant()), ("$limit", limit + 1), ("$offset", offset));
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read()) rows.Add(new { ChannelName = reader.GetString(0), Description = reader.IsDBNull(1) ? null : reader.GetString(1),
            CreatedBy = reader.GetString(2), CreatedAt = reader.GetString(3), MessageCount = reader.GetInt64(4),
            LastMessageId = reader.IsDBNull(5) ? null : reader.GetString(5) });
        return Page(rows, limit, offset);
    }

    private static object Threads(SqliteConnection connection, string root, BoardRequest request, int limit, int offset, int preview)
    {
        BoardText.Require(request.ChannelName != null, "channelName is required.");
        BoardText.Require(request.Sort is null or "created" or "activity", "sort must be created or activity.");
        RequireChannel(connection, null, root, request.ChannelName!);
        var direction = request.RecentFirst ? "DESC" : "ASC";
        var order = request.Sort == "activity" ? "(SELECT max(seq) FROM agent_board_posts q WHERE q.root_id=p.root_id AND q.topic=p.id)" : "seq";
        using var command = Command(connection, null, $"""
            SELECT {PostColumns} FROM agent_board_posts p WHERE root_id=$root AND channel=$channel AND id=topic
            ORDER BY {order} {direction} LIMIT $limit OFFSET $offset
            """, ("$root", root), ("$channel", request.ChannelName), ("$limit", limit + 1), ("$offset", offset));
        var roots = new List<BoardPost>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) roots.Add(ReadPost(reader));
        var rows = new List<object>();
        foreach (var post in roots)
        {
            using var count = Command(connection, null, "SELECT count(*) FROM agent_board_posts WHERE root_id=$root AND topic=$topic AND id<>$topic",
                ("$root", root), ("$topic", post.Metadata.MessageId));
            var replies = (long)count.ExecuteScalar()!;
            var last = Posts(connection, root, "topic=$topic AND id<>$topic", "DESC", 1, 0, ("$topic", post.Metadata.MessageId)).FirstOrDefault();
            rows.Add(new { ThreadId = post.Metadata.MessageId, RootPost = BoardText.Preview(post, preview), ReplyCount = replies,
                LastActivityAt = last?.Metadata.CreatedAt ?? post.Metadata.CreatedAt, LatestReply = last == null ? null : BoardText.Preview(last, preview) });
        }
        return Page(rows, limit, offset);
    }

    private static List<BoardPost> Posts(SqliteConnection connection, string root, string filter, string direction,
        int limit, int offset, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, null,
            $"SELECT {PostColumns} FROM agent_board_posts WHERE root_id=$root AND {filter} ORDER BY seq {direction} LIMIT $limit OFFSET $offset",
            [("$root", root), ("$limit", limit + 1), ("$offset", offset), .. parameters]);
        using var reader = command.ExecuteReader();
        var result = new List<BoardPost>();
        while (reader.Read()) result.Add(ReadPost(reader));
        return result;
    }

    private sealed record BoardPage(List<object> Results, int NReturned, bool HasMore, string? NextCursor);
    private static BoardPage Page(List<object> results, int limit, int offset)
    {
        var more = results.Count > limit;
        if (more) results.RemoveRange(limit, results.Count - limit);
        return new(results, results.Count, more, more ? Convert.ToBase64String(Encoding.UTF8.GetBytes(checked(offset + results.Count).ToString(CultureInfo.InvariantCulture))) : null);
    }

    private static int DecodeCursor(string? cursor)
    {
        if (cursor == null) return 0;
        try
        {
            if (cursor.Length > 32 || !int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)),
                NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0 || offset > int.MaxValue - 50)
                throw new FormatException();
            return offset;
        }
        catch (FormatException) { throw new BoardException("board_invalid_argument", "Invalid board cursor."); }
    }
}
