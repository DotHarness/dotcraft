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
    public void ReconcileClientContext_AppendsBoundSectionOnce()
    {
        var provider = new FakeProvider { Section = "# Inline Visualizations\n\ndirectory-v1" };
        var history = new List<ChatMessage> { new(ChatRole.User, "task") };

        var first = ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.DeveloperMessage);
        var second = ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.DeveloperMessage);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2, history.Count);
        Assert.True(ThreadContextItems.IsKind(history[1], ThreadContextItems.ClientContextKind));
        Assert.Contains("directory-v1", history[1].Text);
    }

    [Fact]
    public void ReconcileClientContext_AppendsUpdateInsteadOfRewritingSentItem()
    {
        var provider = new FakeProvider { Section = "context-v1" };
        var history = new List<ChatMessage>();
        ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.DeveloperMessage);
        var sentItem = history[0];

        provider.Section = "context-v2";
        var changed = ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.DeveloperMessage);

        // Rewriting an item the provider already saw would invalidate the cached prefix.
        Assert.True(changed);
        Assert.Equal(2, history.Count);
        Assert.Same(sentItem, history[0]);
        Assert.Contains("context-v1", history[0].Text);
        Assert.Contains("context-v2", history[1].Text);
    }

    [Fact]
    public void ReconcileClientContext_AppendsClearingItemWhenBindingDisappears()
    {
        var provider = new FakeProvider { Section = "context-v1" };
        var history = new List<ChatMessage>();
        ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.UserReminder);

        provider.Section = null;
        var cleared = ThreadContextItems.ReconcileClientContext(
            history,
            [provider],
            Context,
            ThreadContextCarrier.UserReminder);

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

        var changed = ThreadContextItems.ReconcileClientContext(
            history,
            [baseInstructionsProvider],
            Context,
            ThreadContextCarrier.DeveloperMessage);

        Assert.False(changed);
        Assert.Single(history);
    }

    private sealed class FakeProvider : IThreadSystemPromptContextProvider
    {
        public string? Section { get; set; }

        public ThreadPromptPlacement Placement { get; set; } = ThreadPromptPlacement.ThreadContextItem;

        public ContextPageKey ContextPageKey => new("runtime", "fake", "");

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) => Section;
    }
}
