namespace Oratorio.Server.Domain;

public static class TaskStatusMapping
{
    public static TaskStatus Project(ItemState state) =>
        state switch
        {
            ItemState.Discovered => TaskStatus.Todo,
            ItemState.Dispatching or ItemState.Running or ItemState.Failed => TaskStatus.InProgress,
            ItemState.AwaitingReview => TaskStatus.InReview,
            ItemState.Approved => TaskStatus.Done,
            ItemState.Rejected or ItemState.Archived => TaskStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported item state.")
        };
}
