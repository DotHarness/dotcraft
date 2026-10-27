using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DotCraft.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private readonly ConcurrentDictionary<string, BoardTurnInbox> _boardInboxes = new(StringComparer.Ordinal);

    private BoardTurnInbox RegisterBoardInbox(SessionThread thread, string turnId, CancellationToken ct)
    {
        var inbox = new BoardTurnInbox(turnId, ct);
        if (_boardInboxes.TryGetValue(thread.Id, out var previous)) previous.Dispose();
        _boardInboxes[thread.Id] = inbox;
        return inbox;
    }

    internal async Task<string> ExecuteBoardAsync(SubAgentSessionContext context, BoardRequest request, string? callId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var thread = context.ParentThread;
            var root = thread.Source.SubAgent?.RootThreadId ?? thread.Id;
            var path = thread.Source.SubAgent?.AgentPath ?? AgentPath.Root;
            await SubAgentSessionControl.ResolveBoardAgentAsync(context, path, ct);
            var rootThread = await GetThreadAsync(root, ct);
            if (rootThread.Ephemeral)
                throw new BoardException("board_unavailable", "The root thread must be persistent.");
            var actor = new BoardActor(root, thread.Id, path);
            var store = new AgentMessageBoardStore(Persistence.StateDatabase);
            object result;
            switch (request.Operation)
            {
                case "createChannel":
                    result = store.CreateChannel(actor, request);
                    break;
                case "subscribe":
                    var target = AgentPath.Parse(path).Resolve(request.TargetAgent ?? path).Value;
                    await SubAgentSessionControl.ResolveBoardAgentAsync(context, target, ct);
                    result = store.ChangeSubscription(actor, request, target);
                    break;
                case "post":
                    BoardText.Require(!string.IsNullOrWhiteSpace(callId), "Post requires a runtime invocation identity.");
                    BoardText.Require((request.AgentsToNotify?.Length ?? 0) <= 256, "At most 256 explicit recipients are allowed.");
                    var invocationId = context.ParentTurnId + ":" + callId;
                    var retry = store.FindPostRetry(actor, request, invocationId);
                    if (retry != null)
                    {
                        result = retry.Metadata;
                        break;
                    }
                    var recipients = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var recipient in request.AgentsToNotify ?? [])
                    {
                        var absolute = AgentPath.Parse(path).Resolve(recipient).Value;
                        await SubAgentSessionControl.ResolveBoardAgentAsync(context, absolute, ct);
                        recipients.Add(absolute);
                    }
                    var posted = store.Post(actor, request, invocationId, recipients.ToArray());
                    if (posted.Created)
                    {
                        foreach (var recipient in posted.Recipients)
                        {
                            try { NotifyBoardRecipient(actor, recipient, posted.Metadata, request.Text!); }
                            catch (Exception ex) { Logger?.LogWarning(ex, "Board notification failed for {RootId} {AgentPath}", root, recipient); }
                        }
                    }
                    result = posted.Metadata;
                    break;
                default:
                    for (var scale = 1; scale <= 32768; scale *= 2)
                    {
                        result = store.Read(actor, request, scale);
                        var json = JsonSerializer.Serialize(result, BoardText.JsonOptions);
                        if (Encoding.UTF8.GetByteCount(json) <= 8000) return json;
                    }
                    throw new BoardException("board_output_budget", "Result exceeds the output budget. Request a smaller page or post slice.");
            }
            return JsonSerializer.Serialize(result, BoardText.JsonOptions);
        }
        catch (BoardException ex) { return BoardError(ex.Code, ex.Message); }
        catch (KeyNotFoundException) { return BoardError("board_not_found", "Agent or root thread was not found in this team."); }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            Logger?.LogWarning(ex, "Message board storage operation failed");
            return BoardError("board_storage_error", "Message board storage is unavailable. Existing data was not reset.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BoardError("board_invalid_argument", ex.Message);
        }
    }

    private static string BoardError(string code, string message) =>
        JsonSerializer.Serialize(new { error = new { code, message = BoardText.Slice(message, 0, 500) } }, BoardText.JsonOptions);

    private void NotifyBoardRecipient(BoardActor actor, string path, BoardPostMetadata metadata, string text)
    {
        foreach (var pair in _boardInboxes)
        {
            if (!_runtimeRegistry.TryGetRuntime(pair.Key, out var runtime)) continue;
            var thread = runtime.Thread;
            if (thread.Ephemeral || ToolPlanningThreadClassifier.Classify(thread) == ToolPlanningThreadKind.Internal) continue;
            if ((thread.Source.SubAgent?.RootThreadId ?? thread.Id) != actor.RootId
                || (thread.Source.SubAgent?.AgentPath ?? AgentPath.Root) != path
                || !thread.Turns.Any(turn => turn.Id == pair.Value.TurnId
                    && turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)) continue;
            var payload = JsonSerializer.Serialize(new
            {
                kind = "agent_message_board", metadata.MessageId, metadata.ChannelName, metadata.ThreadId,
                metadata.Author, metadata.CreatedAt, textPreview = BoardText.Slice(text, 0, 150)
            }, BoardText.JsonOptions);
            if (pair.Value.TryAdd(new SubAgentMailboxEntry
            {
                Id = "board:" + metadata.MessageId, RootThreadId = actor.RootId,
                SenderAgentPath = actor.Path, TargetAgentPath = path, Message = payload,
                CreatedAt = metadata.CreatedAt
            })) _subAgentCommunicationRuntime.PublishMailbox(actor.RootId, path);
            return;
        }
    }
}
