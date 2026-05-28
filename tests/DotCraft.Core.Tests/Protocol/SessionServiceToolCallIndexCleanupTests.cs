using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceToolCallIndexCleanupTests
{
    [Fact]
    public void TryRemoveStreamingToolCallIndexByItemReference_RemovesMatchingReference()
    {
        var item = new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.ToolCall,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var indexMap = new Dictionary<int, SessionItem>
        {
            [0] = item
        };

        var removed = SessionService.TryRemoveStreamingToolCallIndexByItemReference(indexMap, item);

        Assert.True(removed);
        Assert.Empty(indexMap);
    }

    [Fact]
    public void TryRemoveStreamingToolCallIndexByItemReference_DoesNotRemoveDifferentInstance()
    {
        var trackedItem = new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.ToolCall,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var sameValueDifferentReference = new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.ToolCall,
            Status = ItemStatus.Streaming,
            CreatedAt = trackedItem.CreatedAt
        };
        var indexMap = new Dictionary<int, SessionItem>
        {
            [0] = trackedItem
        };

        var removed = SessionService.TryRemoveStreamingToolCallIndexByItemReference(
            indexMap,
            sameValueDifferentReference);

        Assert.False(removed);
        Assert.Single(indexMap);
        Assert.Same(trackedItem, indexMap[0]);
    }
}
