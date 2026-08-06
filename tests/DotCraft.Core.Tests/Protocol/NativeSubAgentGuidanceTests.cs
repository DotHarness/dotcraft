using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class NativeSubAgentGuidanceTests
{
    [Fact]
    public void Reconcile_MaterializesGuidanceOnceAtForkBoundary()
    {
        var thread = CreateNativeThread("Explore carefully.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "parent task"),
            new(ChatRole.Assistant, "parent answer")
        };

        var first = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);
        history.Add(new ChatMessage(ChatRole.User, "child task"));
        history.Add(new ChatMessage(ChatRole.Assistant, "child answer"));
        var second = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);

        Assert.Equal(NativeSubAgentGuidanceChange.Added, first);
        Assert.Equal(NativeSubAgentGuidanceChange.None, second);
        Assert.Equal(
            ["user", "assistant", "developer", "user", "assistant"],
            history.Select(message => message.Role.Value));
        Assert.Single(history, message => ThreadContextItems.IsKind(message, ThreadContextItems.SubAgentRoleKind));
        Assert.Contains("Explore carefully.", history[2].Text);
    }

    [Fact]
    public void Reconcile_ReplacesExistingGuidanceInPlace()
    {
        var thread = CreateNativeThread("Old guidance.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "parent task")
        };
        NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);
        history.Add(new ChatMessage(ChatRole.User, "child task"));

        thread.Configuration!.RoleInstructions = "New guidance.";
        var replaced = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);

        Assert.Equal(NativeSubAgentGuidanceChange.Replaced, replaced);
        Assert.Contains("New guidance.", history[1].Text);
        Assert.DoesNotContain("Old guidance.", history[1].Text);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void Reconcile_WithoutRoleInstructions_KeepsSubAgentFraming()
    {
        var thread = CreateNativeThread(instructions: null);
        var history = new List<ChatMessage>();

        var change = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);

        Assert.Equal(NativeSubAgentGuidanceChange.Added, change);
        Assert.Contains("SubAgent", Assert.Single(history).Text);
        Assert.DoesNotContain("## Role Instructions", history[0].Text);
    }

    [Fact]
    public void Reconcile_WithReminderCarrier_UsesUserMessage()
    {
        var thread = CreateNativeThread("Reminder guidance.");
        var history = new List<ChatMessage>();

        NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.UserReminder);

        var item = Assert.Single(history);
        Assert.Equal("user", item.Role.Value);
        Assert.Contains("<system-reminder>", item.Text);
        Assert.Contains("Reminder guidance.", item.Text);
        Assert.True(ThreadContextItems.IsKind(item, ThreadContextItems.SubAgentRoleKind));

        var repeat = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.UserReminder);
        Assert.Equal(NativeSubAgentGuidanceChange.None, repeat);
        Assert.Single(history);
    }

    [Fact]
    public void Reconcile_AdoptsLegacyUnmarkedDeveloperGuidance()
    {
        var thread = CreateNativeThread("Current guidance.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "parent task"),
            new(new ChatRole("developer"), "Legacy guidance."),
            new(ChatRole.User, "child task")
        };

        var change = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);

        Assert.Equal(NativeSubAgentGuidanceChange.Replaced, change);
        Assert.Equal(3, history.Count);
        Assert.Contains("Current guidance.", history[1].Text);
        Assert.True(ThreadContextItems.IsKind(history[1], ThreadContextItems.SubAgentRoleKind));
    }

    [Fact]
    public void Reconcile_DoesNotChangeExternalSubAgentHistory()
    {
        var thread = CreateNativeThread("Native-only guidance.");
        thread.Source.SubAgent!.RuntimeType = CliOneshotRuntime.RuntimeTypeName;
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "task")
        };

        var change = NativeSubAgentGuidance.Reconcile(thread, history, ThreadContextCarrier.DeveloperMessage);

        Assert.Equal(NativeSubAgentGuidanceChange.None, change);
        Assert.Single(history);
    }

    private static SessionThread CreateNativeThread(string? instructions) => new()
    {
        Id = "thread_child",
        Configuration = new ThreadConfiguration { RoleInstructions = instructions },
        Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            RuntimeType = NativeSubAgentRuntime.RuntimeTypeName
        })
    };
}
