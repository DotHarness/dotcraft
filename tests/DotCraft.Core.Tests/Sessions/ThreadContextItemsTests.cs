using DotCraft.Context;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class ThreadContextItemsTests
{
    private static readonly ThreadSystemPromptContext Context =
        new("thread-a", "/workspace", "dotcraft-desktop");

    [Fact]
    public void ReconcileClientContext_AppendsBoundSectionOnceThenAppendsUpdatesWithoutRewriting()
    {
        var provider = new FakeProvider { Section = "context-v1" };
        var history = new List<ChatMessage> { new(ChatRole.User, "task") };

        Assert.True(Reconcile(history, provider, ThreadContextCarrier.DeveloperMessage));
        Assert.False(Reconcile(history, provider, ThreadContextCarrier.DeveloperMessage));
        Assert.Equal(2, history.Count);
        Assert.True(ThreadContextItems.IsKind(history[1], ThreadContextItems.ClientContextKind));
        var sentItem = history[1];

        provider.Section = "context-v2";

        Assert.True(Reconcile(history, provider, ThreadContextCarrier.DeveloperMessage));
        Assert.Equal(3, history.Count);
        Assert.Same(sentItem, history[1]);
        Assert.Contains("context-v1", history[1].Text);
        Assert.Contains("context-v2", history[2].Text);
    }

    [Fact]
    public void ReconcileClientContext_AppendsClearingItemWhenBindingDisappears()
    {
        var provider = new FakeProvider { Section = "context-v1" };
        var history = new List<ChatMessage>();
        Reconcile(history, provider, ThreadContextCarrier.UserReminder);

        provider.Section = null;
        var cleared = Reconcile(history, provider, ThreadContextCarrier.UserReminder);

        Assert.True(cleared);
        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[1].Role.Value);
        Assert.Contains("no longer active", history[1].Text);
    }

    [Fact]
    public void ReconcileClientContext_WithoutBoundProviders_AddsNothing()
    {
        var baseInstructionsProvider = new FakeProvider
        {
            Section = "# Runtime Context",
            Placement = ThreadPromptPlacement.BaseInstructions
        };
        var history = new List<ChatMessage> { new(ChatRole.User, "task") };

        var changed = Reconcile(history, baseInstructionsProvider, ThreadContextCarrier.DeveloperMessage);

        Assert.False(changed);
        Assert.Single(history);
    }

    private static bool Reconcile(List<ChatMessage> history, FakeProvider provider, ThreadContextCarrier carrier) =>
        ThreadContextItems.ReconcileClientContext(history, [provider], Context, carrier);

    private sealed class FakeProvider : IThreadSystemPromptContextProvider
    {
        public string? Section { get; set; }

        public ThreadPromptPlacement Placement { get; set; } = ThreadPromptPlacement.ThreadContextItem;

        public ContextPageKey ContextPageKey => new("runtime", "fake", "");

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) => Section;
    }
}
