using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Protocol;

public sealed class McpAppTransientContextStoreTests
{
    [Fact]
    public void Set_IsLastWriteWins_AndTakeConsumesOnce()
    {
        var store = new McpAppTransientContextStore();
        store.Set("view-1", "thread-1", [new TextContent("old")]);
        store.Set("view-1", "thread-1", [new TextContent("new")]);

        var first = store.TakeForView("view-1", "thread-1");

        Assert.Equal("new", Assert.IsType<TextContent>(Assert.Single(first)).Text);
        Assert.Empty(store.TakeForView("view-1", "thread-1"));
    }

    [Fact]
    public void TakeForThread_ConsumesAllViewsForOnlyThatThread()
    {
        var store = new McpAppTransientContextStore();
        store.Set("view-1", "thread-1", [new TextContent("one")]);
        store.Set("view-2", "thread-1", [new TextContent("two")]);
        store.Set("view-3", "thread-2", [new TextContent("other")]);

        var selected = store.TakeForThread("thread-1");

        Assert.Equal(2, selected.Count);
        Assert.Empty(store.TakeForThread("thread-1"));
        Assert.Equal("other", Assert.IsType<TextContent>(Assert.Single(store.TakeForThread("thread-2"))).Text);
    }

    [Fact]
    public void CaptureForQueuedInput_ConsumesOnlyOriginatingView()
    {
        var store = new McpAppTransientContextStore();
        store.Set("view-1", "thread-1", [new TextContent("message context")]);
        store.Set("view-2", "thread-1", [new TextContent("ordinary context")]);

        store.CaptureForQueuedInput("view-1", "thread-1", "queued-1");

        Assert.Equal(
            "message context",
            Assert.IsType<TextContent>(Assert.Single(store.TakeForQueuedInput("queued-1"))).Text);
        Assert.Equal(
            "ordinary context",
            Assert.IsType<TextContent>(Assert.Single(store.TakeForThread("thread-1"))).Text);
        Assert.Empty(store.TakeForQueuedInput("queued-1"));
    }

    [Fact]
    public void Clear_RemovesViewQueuedAndThreadSidecars()
    {
        var store = new McpAppTransientContextStore();
        store.Set("view-1", "thread-1", [new TextContent("queued")]);
        store.CaptureForQueuedInput("view-1", "thread-1", "queued-1");
        store.Set("view-2", "thread-1", [new TextContent("pending")]);

        Assert.True(store.ClearQueuedInput("queued-1"));
        store.ClearThread("thread-1");

        Assert.Empty(store.TakeForQueuedInput("queued-1"));
        Assert.Empty(store.TakeForThread("thread-1"));
    }
}
