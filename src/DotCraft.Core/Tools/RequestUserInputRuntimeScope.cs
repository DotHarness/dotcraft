using DotCraft.Protocol;

namespace DotCraft.Tools;

/// <summary>
/// Runtime context used by the RequestUserInput tool to pause the active Session Core turn.
/// </summary>
public sealed record RequestUserInputRuntimeContext(
    Func<IReadOnlyList<RequestUserInputQuestion>, Task<RequestUserInputResponse>> RequestAsync);

/// <summary>
/// Async-local scope for model-initiated user input requests.
/// </summary>
public static class RequestUserInputRuntimeScope
{
    private static readonly AsyncLocal<RequestUserInputRuntimeContext?> CurrentContext = new();

    public static RequestUserInputRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(RequestUserInputRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(RequestUserInputRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
