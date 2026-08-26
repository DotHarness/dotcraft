using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class AgentInstructionsHistoryTests
{
    [Fact]
    public void Reconcile_AlwaysCreatesOnePlainUserPrefix()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "ordinary user message")
        };

        var change = AgentInstructionsHistory.Reconcile(history, "project instructions");

        Assert.Equal(AgentInstructionsHistoryChange.Added, change);
        Assert.Equal("user", history[0].Role.Value);
        Assert.Equal("project instructions", history[0].Text);
        Assert.DoesNotContain("system-reminder", history[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(AgentInstructionsHistory.IsInstructions(history[0]));
        Assert.Single(history, AgentInstructionsHistory.IsInstructions);
    }

    [Fact]
    public void Reconcile_ReplacesInPlace()
    {
        var history = new List<ChatMessage>();
        AgentInstructionsHistory.Reconcile(history, "old");
        history.Add(new ChatMessage(ChatRole.User, "conversation"));

        var change = AgentInstructionsHistory.Reconcile(history, "new");

        Assert.Equal(AgentInstructionsHistoryChange.Replaced, change);
        Assert.Equal("new", history[0].Text);
        Assert.Single(history, AgentInstructionsHistory.IsInstructions);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void Reconcile_EmptyContentRemovesInstructionItem()
    {
        var history = new List<ChatMessage>();
        AgentInstructionsHistory.Reconcile(history, "old");
        history.Add(new ChatMessage(ChatRole.User, "conversation"));

        var change = AgentInstructionsHistory.Reconcile(history, string.Empty);

        Assert.Equal(AgentInstructionsHistoryChange.Removed, change);
        var remaining = Assert.Single(history);
        Assert.Equal("conversation", remaining.Text);
    }
}
