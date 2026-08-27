using DotCraft.Context;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class ContextPageManagerTests
{
    [Fact]
    public void GetOrAdd_KeepsStablePageUntilReleased()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("scope", "name", "variant");
        var value = "v1";

        Assert.Equal("v1", manager.GetOrAdd("thread", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);

        value = "v2";
        manager.MarkDirty(key);

        Assert.Equal("v1", manager.GetOrAdd("thread", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);

        manager.ReleaseStablePages("thread");

        Assert.Equal("v2", manager.GetOrAdd("thread", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
    }

    [Fact]
    public void GetOrAdd_CachesPerThread()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("scope", "name", "variant");
        var value = "v1";

        Assert.Equal("v1", manager.GetOrAdd("thread-a", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);

        value = "v2";

        Assert.Equal("v1", manager.GetOrAdd("thread-a", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
        Assert.Equal("v2", manager.GetOrAdd("thread-b", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
    }

    [Fact]
    public void ForgetThread_RemovesStablePages()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("scope", "name", "variant");
        var value = "v1";

        Assert.Equal("v1", manager.GetOrAdd("thread", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);

        value = "v2";
        manager.ForgetThread("thread");

        Assert.Equal("v2", manager.GetOrAdd("thread", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
    }

    [Fact]
    public void ReleaseStablePage_RemovesOnlyMatchingPage()
    {
        var manager = new ContextPageManager();
        var releasedKey = new ContextPageKey("scope", "released", "variant");
        var pinnedKey = new ContextPageKey("scope", "pinned", "variant");
        var releasedValue = "released-v1";
        var pinnedValue = "pinned-v1";

        Assert.Equal("released-v1", manager.GetOrAdd("thread", releasedKey, ContextPageLifecycle.StableUntilCompaction, () => releasedValue).Content);
        Assert.Equal("pinned-v1", manager.GetOrAdd("thread", pinnedKey, ContextPageLifecycle.StableUntilCompaction, () => pinnedValue).Content);

        releasedValue = "released-v2";
        pinnedValue = "pinned-v2";
        manager.ReleaseStablePage("thread", releasedKey);

        Assert.Equal("released-v2", manager.GetOrAdd("thread", releasedKey, ContextPageLifecycle.StableUntilCompaction, () => releasedValue).Content);
        Assert.Equal("pinned-v1", manager.GetOrAdd("thread", pinnedKey, ContextPageLifecycle.StableUntilCompaction, () => pinnedValue).Content);
    }

    [Fact]
    public void ImmediateLifecycle_AlwaysReloads()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("scope", "name", "variant");
        var value = "v1";

        Assert.Equal("v1", manager.GetOrAdd("thread", key, ContextPageLifecycle.Immediate, () => value).Content);

        value = "v2";

        Assert.Equal("v2", manager.GetOrAdd("thread", key, ContextPageLifecycle.Immediate, () => value).Content);
    }

    [Fact]
    public void FullFork_CopiesPinnedPageSnapshotOnce()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("scope", "name", "variant");
        var value = "parent-v1";
        manager.GetOrAdd("parent", key, ContextPageLifecycle.StableUntilCompaction, () => value);

        Assert.True(manager.TryForkStablePages("parent", "child"));
        value = "parent-v2";
        manager.ReleaseStablePages("parent");

        Assert.Equal(
            "parent-v1",
            manager.GetOrAdd("child", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
        Assert.Equal(
            "parent-v2",
            manager.GetOrAdd("parent", key, ContextPageLifecycle.StableUntilCompaction, () => value).Content);
    }

    [Fact]
    public void GetOrAdd_CachesSourcesWithTheSameStableSnapshot()
    {
        var manager = new ContextPageManager();
        var key = new ContextPageKey("agentsMd", "instructions", "cwd");
        var document = new ContextPageDocument("v1", ["/repo/AGENTS.md"]);

        var first = manager.GetOrAdd(
            "thread",
            key,
            ContextPageLifecycle.StableUntilCompaction,
            () => document);
        document = new ContextPageDocument("v2", ["/repo/sub/AGENTS.md"]);
        var stable = manager.GetOrAdd(
            "thread",
            key,
            ContextPageLifecycle.StableUntilCompaction,
            () => document);

        Assert.Equal("v1", stable.Content);
        Assert.Equal(["/repo/AGENTS.md"], stable.Sources);
        Assert.Equal(first.Fingerprint, stable.Fingerprint);
    }
}
