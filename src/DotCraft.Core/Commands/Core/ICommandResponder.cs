namespace DotCraft.Commands.Core;

/// <summary>
/// Defines the response interface for command handlers.
/// Provides a channel-agnostic way to send messages back to users.
/// </summary>
public interface ICommandResponder
{
    /// <summary>
    /// Sends a text message to the user.
    /// </summary>
    /// <param name="message">The message to send.</param>
    Task SendTextAsync(string message);
    
    /// <summary>
    /// Sends a markdown-formatted message to the user.
    /// </summary>
    /// <param name="markdown">The markdown content to send.</param>
    Task SendMarkdownAsync(string markdown);
}

/// <summary>
/// Extension methods for ICommandResponder.
/// </summary>
public static class CommandResponderExtensions
{
    /// <summary>
    /// Sends a message (automatically chooses text or markdown based on content).
    /// </summary>
    public static Task SendMessageAsync(this ICommandResponder responder, string message)
    {
        // Simple heuristic: if message contains newlines or markdown-like syntax, use markdown
        if (message.Contains('\n') || message.Contains("**") || message.Contains('`'))
        {
            return responder.SendMarkdownAsync(message);
        }
        return responder.SendTextAsync(message);
    }
}
