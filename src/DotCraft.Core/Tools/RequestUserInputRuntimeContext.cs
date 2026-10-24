using DotCraft.Sessions;

namespace DotCraft.Tools;

/// <summary>
/// Runtime context used by the RequestUserInput tool to pause the active Session Core turn.
/// </summary>
public sealed record RequestUserInputRuntimeContext(
    Func<IReadOnlyList<RequestUserInputQuestion>, Task<RequestUserInputResponse>> RequestAsync);
