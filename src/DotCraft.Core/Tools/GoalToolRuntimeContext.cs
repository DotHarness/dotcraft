using DotCraft.Sessions;

namespace DotCraft.Tools;

/// <summary>
/// Runtime context used by model-visible goal tools to operate on the active Session Core thread.
/// </summary>
public sealed record GoalToolRuntimeContext(
    ISessionService SessionService,
    string ThreadId,
    string TurnId);
