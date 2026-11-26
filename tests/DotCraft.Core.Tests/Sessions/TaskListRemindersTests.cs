using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class TaskListRemindersTests
{
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void IsStale_CountsAssistantMessagesSinceTheLatestTodoWrite(int sinceWrite, bool stale)
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "TodoWrite")]),
            .. Assistant(sinceWrite)
        ];

        Assert.Equal(stale, TaskListReminders.IsStale(history));
    }

    [Fact]
    public void IsStale_WaitsForAnotherTenAssistantMessagesAfterAReminder()
    {
        List<ChatMessage> history =
        [
            .. Assistant(12),
            ThreadContextItems.Create(ThreadContextCarrier.UserReminder, TaskListReminders.StaleKind, "stale"),
            .. Assistant(9)
        ];

        Assert.False(TaskListReminders.IsStale(history));
        history.Add(new ChatMessage(ChatRole.Assistant, "step"));
        Assert.True(TaskListReminders.IsStale(history));
    }

    private static IEnumerable<ChatMessage> Assistant(int count) =>
        Enumerable.Range(0, count).Select(static _ => new ChatMessage(ChatRole.Assistant, "step"));
}
