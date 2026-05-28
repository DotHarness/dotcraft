using Microsoft.Extensions.AI;
using DotCraft.Context;

namespace DotCraft.Protocol;

public sealed class PreSamplingCompactionRuntimeContext
{
    public required Func<
        IReadOnlyList<ChatMessage>,
        CancellationToken,
        Task<IReadOnlyList<ChatMessage>?>> TryCompactAsync { get; init; }

    public Func<
        IReadOnlyList<ChatMessage>,
        PromptRequestSnapshot,
        CancellationToken,
        Task<IReadOnlyList<ChatMessage>?>>? TryCompactWithSnapshotAsync { get; init; }

    public string? ProviderId { get; init; }

    public string? Mode { get; init; }

    public string? ThreadId { get; init; }

    public string? TurnId { get; init; }

    public int? EstimatedInputTokens { get; init; }

    public Func<PromptRequestSnapshot, CancellationToken, Task>? CaptureSnapshotAsync { get; init; }
}

public static class PreSamplingCompactionRuntimeScope
{
    private static readonly AsyncLocal<PreSamplingCompactionRuntimeContext?> CurrentContext = new();

    public static PreSamplingCompactionRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(PreSamplingCompactionRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(PreSamplingCompactionRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
