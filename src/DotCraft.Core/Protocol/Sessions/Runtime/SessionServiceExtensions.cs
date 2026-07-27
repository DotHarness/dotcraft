using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Convenience extension methods for <see cref="ISessionService"/>.
/// </summary>
public static class SessionServiceExtensions
{
    /// <summary>
    /// Submits plain-text user input to a Thread, starting a new Turn.
    /// Wraps the text into a single <see cref="TextContent"/> and delegates
    /// to <see cref="ISessionService.SubmitInputAsync"/>.
    /// </summary>
    public static IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        this ISessionService service,
        string threadId,
        string text,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default)
        => service.SubmitInputAsync(
            threadId,
            [new TextContent(text)],
            sender, messages, ct: ct);
}
