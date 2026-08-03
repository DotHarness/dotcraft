using DotCraft.Protocol;
using DotCraft.Sessions;

namespace DotCraft.Tools;

/// <summary>
/// Runtime context used by model-visible goal tools to operate on the active Session Core thread.
/// </summary>
public sealed record GoalToolRuntimeContext(
    ISessionService SessionService,
    string ThreadId,
    string TurnId);

/// <summary>
/// Async-local scope for goal tools. Session Core sets this while executing a main-thread turn.
/// </summary>
public static class GoalToolRuntimeScope
{
    private static readonly AsyncLocal<GoalToolRuntimeContext?> CurrentContext = new();

    public static GoalToolRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(GoalToolRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(GoalToolRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
