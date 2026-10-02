using System.Text.Json.Nodes;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeStoreTests
{
    [Fact]
    public void ConcurrentCellWrites_CannotPushTheMergedStorePastItsCap()
    {
        var store = new CodeModeStore();
        var half = new string('x', 600 * 1024);

        Assert.True(store.TryApply("thread", new JsonObject { ["first"] = half }, []));
        Assert.False(store.TryApply("thread", new JsonObject { ["second"] = half }, []));
        Assert.True(store.TryApply("thread", new JsonObject { ["second"] = half }, ["first"]));

        var snapshot = store.Snapshot("thread");
        Assert.False(snapshot.ContainsKey("first"));
        Assert.True(snapshot.ContainsKey("second"));
    }
}
