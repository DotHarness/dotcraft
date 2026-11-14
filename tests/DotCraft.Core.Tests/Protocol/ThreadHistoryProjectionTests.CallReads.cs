using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class ThreadHistoryProjectionTests
{
    [Fact]
    public async Task CallItems_UseScopedOrderedIndexWithoutReadingOtherPayloads()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 4);
        foreach (var turn in thread.Turns)
        {
            turn.Items[0].Type = ItemType.ToolCall;
            turn.Items[0].Payload = new ToolCallPayload { CallId = "shared", ToolName = "Exec" };
            turn.Items[1].Type = ItemType.CommandExecution;
            turn.Items[1].Payload = new CommandExecutionPayload { CallId = "shared", Command = "echo ok" };
            turn.Items[2].Type = ItemType.ToolResult;
            turn.Items[2].Payload = new ToolResultPayload { CallId = "shared", Result = "ok", Success = true };
        }
        await _store.SaveThreadAsync(thread);
        using (var database = new WorkspaceStateDatabase(_root))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE thread_items SET item_json = 'not-json' WHERE thread_id = $thread AND item_id = 'item_001_004'";
            command.Parameters.AddWithValue("$thread", thread.Id);
            command.ExecuteNonQuery();
        }

        var read = await _store.ReadThreadCallItemsAsync(thread.Id, "turn_001", ["shared", "missing", "shared"]);
        Assert.Equal(["item_001_001", "item_001_002", "item_001_003"], read.Select(entry => entry.Item.Id));
        Assert.All(read, entry => Assert.Equal("turn_001", entry.TurnId));
        Assert.Equal("echo ok", read[1].Item.AsCommandExecution!.Command);
        Assert.Empty(await _store.ReadThreadCallItemsAsync(thread.Id, "unknown", ["shared"]));
    }
}
