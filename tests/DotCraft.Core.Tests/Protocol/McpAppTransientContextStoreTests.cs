using DotCraft.Protocol;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using Xunit;

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

    [Fact]
    public async Task TakeForThread_RacingSet_HasOneAtomicCut()
    {
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var store = new McpAppTransientContextStore();
            store.Set("existing", "thread-1", [new TextContent("existing")]);
            using var start = new ManualResetEventSlim();

            var set = Task.Run(() =>
            {
                start.Wait();
                store.Set("racing", "thread-1", [new TextContent("racing")]);
            });
            var take = Task.Run(() =>
            {
                start.Wait();
                return store.TakeForThread("thread-1");
            });

            start.Set();
            await Task.WhenAll(set, take);
            var first = await take;
            var second = store.TakeForThread("thread-1");
            var observed = first.Concat(second)
                .Cast<TextContent>()
                .Select(static content => content.Text)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(["existing", "racing"], observed);
            Assert.Empty(store.TakeForThread("thread-1"));
        }
    }
}
