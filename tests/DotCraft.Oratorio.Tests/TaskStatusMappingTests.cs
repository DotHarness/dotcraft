using DotCraft.Oratorio.Domain;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Tests;

public sealed class TaskStatusMappingTests
{
    public static TheoryData<ItemState, BoardTaskStatus> Cases => new()
    {
        { ItemState.Discovered, BoardTaskStatus.Todo },
        { ItemState.Dispatching, BoardTaskStatus.InProgress },
        { ItemState.Running, BoardTaskStatus.InProgress },
        { ItemState.AwaitingReview, BoardTaskStatus.InReview },
        { ItemState.Approved, BoardTaskStatus.Done },
        { ItemState.Rejected, BoardTaskStatus.Cancelled },
        { ItemState.Failed, BoardTaskStatus.InProgress },
        { ItemState.Archived, BoardTaskStatus.Cancelled }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Project_MapsDomainItemState_ToBoardTaskStatus(ItemState state, BoardTaskStatus expected)
    {
        Assert.Equal(expected, TaskStatusMapping.Project(state));
    }
}
