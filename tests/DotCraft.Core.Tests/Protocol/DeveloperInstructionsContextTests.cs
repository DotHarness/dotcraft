using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class DeveloperInstructionsContextTests
{
    [Fact]
    public void Reconcile_WithDeveloperCarrier_AppendsOneDeveloperItem()
    {
        var history = new List<ChatMessage>();

        var first = ThreadContextItems.ReconcileDeveloperInstructions(history, "Reply only through SendMessage.", ThreadContextCarrier.DeveloperMessage);
        history.Add(new ChatMessage(ChatRole.User, "task"));
        var second = ThreadContextItems.ReconcileDeveloperInstructions(history, "Reply only through SendMessage.", ThreadContextCarrier.DeveloperMessage);

        Assert.True(first);
        Assert.False(second);
        var item = Assert.Single(history, message => ThreadContextItems.IsKind(message, ThreadContextItems.DeveloperInstructionsKind));
        Assert.Equal("developer", item.Role.Value);
        Assert.Equal("Reply only through SendMessage.", item.Text);
        Assert.Equal(["developer", "user"], history.Select(message => message.Role.Value));
    }

    [Fact]
    public void Reconcile_WithReminderCarrier_LeavesHistoryToTheBaseSection()
    {
        var history = new List<ChatMessage>();

        var changed = ThreadContextItems.ReconcileDeveloperInstructions(history, "Reply only through SendMessage.", ThreadContextCarrier.UserReminder);

        Assert.False(changed);
        Assert.Empty(history);
    }
}
