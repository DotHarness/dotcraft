using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceHistorySummaryTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(TurnStatus.Running)]
    [InlineData(TurnStatus.WaitingApproval)]
    [InlineData(TurnStatus.WaitingInput)]
    public async Task ColdSummaries_DoNotLoadRuntimeOrPersistInterruptedStatus(TurnStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var turn = new SessionTurn
        {
            Id = "turn_001", ThreadId = "cold-summary", Status = status, StartedAt = now,
            Items =
            [
                new SessionItem
                {
                    Id = "user", TurnId = "turn_001", Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed, CreatedAt = now,
                    Payload = new UserMessagePayload { Text = "question" }
                },
                new SessionItem
                {
                    Id = "reasoning", TurnId = "turn_001", Type = ItemType.ReasoningContent,
                    Status = ItemStatus.Completed, CreatedAt = now,
                    Payload = new ReasoningContentPayload { Text = "work in progress" }
                }
            ]
        };
        var thread = new SessionThread
        {
            Id = turn.ThreadId, WorkspacePath = _tempDir, OriginChannel = "test",
            CreatedAt = now, LastActiveAt = now, Turns = [turn]
        };
        await using var store = new ThreadStore(_tempDir);
        await store.SaveThreadAsync(thread);
        var model = new RecordingChatClient("unused");
        await using var factory = CreateAgentFactory(model);
        var service = CreateService(factory, model);

        var page = await service.ListThreadSummariesAsync(" cold-summary ", null, 1,
            ThreadHistorySortDirection.Descending, []);

        var summary = Assert.Single(page.Data);
        Assert.Equal(TurnStatus.Cancelled, summary.Turn.Status);
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal("user", Assert.Single(summary.Turn.Items).Id);
        Assert.Null(service.TryGetLoadedThread(thread.Id));
        Assert.Empty(model.LastMessages);
        var persisted = await store.ListThreadTurnsAsync(thread.Id, null, 1, ThreadHistorySortDirection.Descending);
        Assert.Equal(status, Assert.Single(persisted.Data).Status);
    }
}
