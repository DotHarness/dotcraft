using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class BoardTurnInboxTests
{
    [Fact]
    public void ClosingAdmissionDiscardsLateNotices_ResumedAdmissionDoesNotReplayThem()
    {
        using var inbox = new BoardTurnInbox("turn-1", CancellationToken.None);
        Assert.True(inbox.TryAdd(Notice("first")));
        inbox.SetAdmission(false);
        Assert.False(inbox.TryAdd(Notice("late")));
        Assert.Empty(inbox.Drain());
        inbox.SetAdmission(true);
        Assert.True(inbox.TryAdd(Notice("new guidance")));
        Assert.Equal("new guidance", Assert.Single(inbox.Drain()).Message);
        inbox.Dispose();
        inbox.SetAdmission(true);
        Assert.False(inbox.TryAdd(Notice("disposed")));
    }

    [Fact]
    public async Task CancellationAndTerminalRacesCannotLeakIntoAnotherTurn()
    {
        using var cts = new CancellationTokenSource();
        using var first = new BoardTurnInbox("turn-1", cts.Token);
        var writers = Enumerable.Range(0, 100).Select(i => Task.Run(() => first.TryAdd(Notice(i.ToString())))).ToArray();
        cts.Cancel();
        first.Dispose();
        await Task.WhenAll(writers);
        Assert.Empty(first.Drain());
        Assert.False(first.TryAdd(Notice("late")));
    }

    private static SubAgentMailboxEntry Notice(string text) => new()
    {
        Id = Guid.NewGuid().ToString(), RootThreadId = "root", SenderAgentPath = "/root/worker", TargetAgentPath = "/root", Message = text
    };
}
