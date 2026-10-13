using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class AgentMessageBoardStore
{
    public object CreateChannel(BoardActor actor, BoardRequest request)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureBoard(connection, transaction, actor.RootId);
        var now = DateTimeOffset.UtcNow;
        InsertChannel(connection, transaction, actor, request.ChannelName!, request.Description, now);
        SetSubscription(connection, transaction, actor.RootId, "channel:" + request.ChannelName, actor.Path, request.Subscribe, true);
        var result = new { request.ChannelName, request.Description, CreatedBy = actor.Path, CreatedAt = now, MessageCount = 0 };
        BoardText.RequireFits(result);
        transaction.Commit();
        return result;
    }

    private static void InsertChannel(SqliteConnection connection, SqliteTransaction transaction, BoardActor actor,
        string name, string? description, DateTimeOffset now)
    {
        BoardText.Channel(name);
        BoardText.Require(description == null || Encoding.UTF8.GetByteCount(description) <= 1024, "Channel description exceeds 1,024 UTF-8 bytes.");
        using var command = Command(connection, transaction, """
            INSERT OR IGNORE INTO agent_board_channels(root_id,name,description,author,created_at)
            VALUES($root,$name,$description,$author,$now)
            """, ("$root", actor.RootId), ("$name", name), ("$description", description), ("$author", actor.Path), ("$now", now.ToString("O")));
        if (command.ExecuteNonQuery() == 0)
            throw new BoardException("board_channel_exists", "Channel already exists.");
    }

    public BoardWriteResult Post(BoardActor actor, BoardRequest request, string invocationId, string[] explicitRecipients)
    {
        BoardText.Require(!string.IsNullOrEmpty(request.Text) && Encoding.UTF8.GetByteCount(request.Text) <= 65536,
            "Post text requires 1–65,536 UTF-8 bytes.");
        BoardText.Require(new[] { request.ChannelName, request.NewChannelName, request.ThreadId }.Count(x => x != null) == 1,
            "Supply exactly one of channelName, newChannelName or threadId.");
        var requestId = actor.ThreadId + ":" + invocationId;
        var requestJson = JsonSerializer.Serialize(request);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureBoard(connection, transaction, actor.RootId);
        var existing = FindPostRetry(connection, transaction, actor, request, invocationId);
        if (existing != null) return existing;

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString();
        var topic = id;
        string channel;
        if (request.ThreadId != null)
        {
            var parent = LoadPost(connection, transaction, actor.RootId, request.ThreadId);
            BoardText.Require(parent.Metadata.ThreadId == request.ThreadId, "threadId must identify a top-level post.");
            topic = request.ThreadId;
            channel = parent.Metadata.ChannelName;
        }
        else if (request.NewChannelName != null)
        {
            channel = request.NewChannelName;
            InsertChannel(connection, transaction, actor, channel, null, now);
            SetSubscription(connection, transaction, actor.RootId, "channel:" + channel, actor.Path, true, false);
        }
        else
        {
            channel = request.ChannelName!;
            RequireChannel(connection, transaction, actor.RootId, channel);
        }

        var recipients = explicitRecipients.ToHashSet(StringComparer.Ordinal);
        using (var subscribers = Command(connection, transaction,
            "SELECT agent FROM agent_board_subscriptions WHERE root_id=$root AND target=$target AND enabled=1",
            ("$root", actor.RootId), ("$target", request.ThreadId == null ? "channel:" + channel : "thread:" + topic)))
        using (var reader = subscribers.ExecuteReader())
            while (reader.Read()) recipients.Add(reader.GetString(0));
        recipients.Remove(actor.Path);

        using var insert = Command(connection, transaction, """
            INSERT INTO agent_board_posts(root_id,id,channel,topic,author,created_at,body,body_search,request_id,request_json)
            VALUES($root,$id,$channel,$topic,$author,$now,$body,$search,$request,$json)
            """, ("$root", actor.RootId), ("$id", id), ("$channel", channel), ("$topic", topic), ("$author", actor.Path),
            ("$now", now.ToString("O")), ("$body", request.Text), ("$search", request.Text!.ToUpperInvariant()),
            ("$request", requestId), ("$json", requestJson));
        insert.ExecuteNonQuery();
        SetSubscription(connection, transaction, actor.RootId, "thread:" + topic, actor.Path, true, false);
        var metadata = new BoardPostMetadata(id, channel, actor.Path, topic, now);
        BoardText.RequireFits(metadata);
        transaction.Commit();
        return new(metadata, recipients.ToArray(), true);
    }

    public BoardWriteResult? FindPostRetry(BoardActor actor, BoardRequest request, string invocationId)
    {
        using var connection = Open();
        CheckDeleted(connection, null, actor.RootId);
        return FindPostRetry(connection, null, actor, request, invocationId);
    }

    private static BoardWriteResult? FindPostRetry(SqliteConnection connection, SqliteTransaction? transaction,
        BoardActor actor, BoardRequest request, string invocationId)
    {
        using var command = Command(connection, transaction,
            $"SELECT {PostColumns},request_json FROM agent_board_posts WHERE root_id=$root AND request_id=$request",
            ("$root", actor.RootId), ("$request", actor.ThreadId + ":" + invocationId));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        BoardText.Require(reader.GetString(6) == JsonSerializer.Serialize(request),
            "Invocation identity was already used with different post arguments.");
        return new(ReadPost(reader).Metadata, [], false);
    }
}
