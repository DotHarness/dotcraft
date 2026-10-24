namespace DotCraft.Tools;

/// <summary>Operations of the invoking thread's active Turn, handed to tools through <see cref="ToolInvocationContext"/>.</summary>
public sealed record TurnToolServices
{
    public required bool IsTopLevelThread { get; init; }

    public UserCoordinationRuntimeContext? UserCoordination { get; init; }

    public RequestUserInputRuntimeContext? RequestUserInput { get; init; }

    public WorktreeToolRuntimeContext? Worktree { get; init; }

    public GoalToolRuntimeContext? Goal { get; init; }
}
