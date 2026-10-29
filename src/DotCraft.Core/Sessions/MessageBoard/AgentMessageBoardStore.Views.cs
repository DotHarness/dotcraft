using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class AgentMessageBoardStore
{
    private const int TopicPreviewChars = 500;
    private const int MaxTopicPosts = 500;

    public BoardTopicPage ListTopics(string root, string? channel, string? cursor, int limit)
    {
        var offset = DecodeCursor(cursor);
        using var connection = Open();
        if (IsDeleted(connection, null, root)) return new([], [], null);
        var channels = new List<BoardChannelView>();
        using (var command = Command(connection, null, """
            SELECT c.name,c.description,c.author,c.created_at,
                (SELECT count(*) FROM agent_board_posts p WHERE p.root_id=c.root_id AND p.channel=c.name AND p.id=p.topic)
            FROM agent_board_channels c WHERE c.root_id=$root ORDER BY c.created_at,c.name
            """, ("$root", root)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                channels.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
                    ParseTime(reader.GetString(3)), reader.GetInt32(4)));
        }
        var topics = Topics(connection, root, "($channel IS NULL OR channel=$channel)", limit + 1, offset, ("$channel", channel));
        var more = topics.Count > limit;
        if (more) topics.RemoveRange(limit, topics.Count - limit);
        return new(channels, topics, more ? EncodeCursor(offset + limit) : null);
    }

    public BoardTopicPosts ReadTopic(string root, string topicId)
    {
        using var connection = Open();
        if (IsDeleted(connection, null, root)) return new(null, [], false);
        var topic = Topics(connection, root, "id=$topic", 1, 0, ("$topic", topicId)).FirstOrDefault();
        if (topic == null) return new(null, [], false);
        using var command = Command(connection, null,
            $"SELECT {PostColumns},request_json FROM agent_board_posts WHERE root_id=$root AND topic=$topic ORDER BY seq LIMIT $limit",
            ("$root", root), ("$topic", topicId), ("$limit", MaxTopicPosts + 1));
        using var reader = command.ExecuteReader();
        var posts = new List<BoardPostView>();
        while (reader.Read()) posts.Add(View(ReadPost(reader), reader.GetString(6), null));
        var more = posts.Count > MaxTopicPosts;
        if (more) posts.RemoveRange(MaxTopicPosts, posts.Count - MaxTopicPosts);
        return new(topic, posts, more);
    }

    private static List<BoardTopicView> Topics(SqliteConnection connection, string root, string filter, int limit, int offset,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, null, $"""
            SELECT {PostColumns},request_json,
                (SELECT count(*) FROM agent_board_posts r WHERE r.root_id=p.root_id AND r.topic=p.id AND r.id<>p.id),
                (SELECT created_at FROM agent_board_posts r WHERE r.root_id=p.root_id AND r.topic=p.id ORDER BY r.seq DESC LIMIT 1),
                (SELECT max(r.seq) FROM agent_board_posts r WHERE r.root_id=p.root_id AND r.topic=p.id) activity
            FROM agent_board_posts p WHERE root_id=$root AND id=topic AND {filter}
            ORDER BY activity DESC LIMIT $limit OFFSET $offset
            """, [("$root", root), ("$limit", limit), ("$offset", offset), .. parameters]);
        using var reader = command.ExecuteReader();
        var topics = new List<BoardTopicView>();
        while (reader.Read())
        {
            var post = ReadPost(reader);
            topics.Add(new(post.Metadata.MessageId, post.Metadata.ChannelName, View(post, reader.GetString(6), TopicPreviewChars),
                reader.GetInt32(7), ParseTime(reader.GetString(8))));
        }
        return topics;
    }

    private static BoardPostView View(BoardPost post, string requestJson, int? maxChars)
    {
        var author = AgentPath.Parse(post.Metadata.Author);
        var notified = (JsonSerializer.Deserialize<BoardRequest>(requestJson)?.AgentsToNotify ?? [])
            .Select(target => author.Resolve(target).Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var text = maxChars is { } max ? BoardText.Slice(post.Text, 0, max) : post.Text;
        return new(post.Metadata.MessageId, post.Metadata.ChannelName, post.Metadata.ThreadId, post.Metadata.Author,
            post.Metadata.CreatedAt, text, notified);
    }

    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
