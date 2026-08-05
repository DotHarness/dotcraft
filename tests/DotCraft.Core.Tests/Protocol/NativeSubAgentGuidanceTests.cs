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

        var first = NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);
        history.Add(new ChatMessage(ChatRole.User, "child task"));
        history.Add(new ChatMessage(ChatRole.Assistant, "child answer"));
        var second = NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);

        Assert.Equal(NativeSubAgentGuidanceChange.Added, first);
        Assert.Equal(NativeSubAgentGuidanceChange.None, second);
        Assert.Equal(
            ["user", "assistant", "developer", "user", "assistant"],
            history.Select(message => message.Role.Value));
        Assert.Single(history, message => message.Role.Value == "developer");
    }

    [Fact]
    public void Reconcile_ReplacesAndClearsExistingGuidanceInPlace()
    {
        var thread = CreateNativeThread("Old guidance.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "parent task")
        };
        NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);
        history.Add(new ChatMessage(ChatRole.User, "child task"));

        thread.Configuration!.RoleInstructions = "New guidance.";
        var replaced = NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);

        Assert.Equal(NativeSubAgentGuidanceChange.Replaced, replaced);
        Assert.Equal("New guidance.", history[1].Text);

        thread.Configuration.RoleInstructions = null;
        var removed = NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);

        Assert.Equal(NativeSubAgentGuidanceChange.Removed, removed);
        Assert.DoesNotContain(history, message => message.Role.Value == "developer");
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

        var change = NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);

        Assert.Equal(NativeSubAgentGuidanceChange.None, change);
        Assert.Single(history);
    }

    [Fact]
    public void Reconcile_WhenDisabled_DoesNotMaterializeGuidance()
    {
        var thread = CreateNativeThread("System-prompt guidance.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "task")
        };

        var change = NativeSubAgentGuidance.Reconcile(thread, history, enabled: false);

        Assert.Equal(NativeSubAgentGuidanceChange.None, change);
        Assert.DoesNotContain(history, message => message.Role.Value == "developer");
    }

    [Fact]
    public void Reconcile_WhenDisabled_RemovesMaterializedGuidance()
    {
        var thread = CreateNativeThread("Protocol-specific guidance.");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "parent task")
        };
        NativeSubAgentGuidance.Reconcile(thread, history, enabled: true);
        history.Add(new ChatMessage(ChatRole.User, "child task"));

        var change = NativeSubAgentGuidance.Reconcile(thread, history, enabled: false);

        Assert.Equal(NativeSubAgentGuidanceChange.Removed, change);
        Assert.Equal(["user", "user"], history.Select(message => message.Role.Value));
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
