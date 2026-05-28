namespace DotCraft.Commands.Core;

/// <summary>
/// Represents the result of a command execution.
/// </summary>
public sealed class CommandResult
{
    /// <summary>
    /// Whether the command was handled.
    /// </summary>
    public bool Handled { get; init; }
    
    /// <summary>
    /// Optional message to send back to the user.
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// Whether to send the message as markdown.
    /// </summary>
    public bool IsMarkdown { get; init; }
    
    /// <summary>
    /// When set, the command was a custom command that expanded into a prompt.
    /// The caller should feed this prompt to the agent instead of replying directly.
    /// </summary>
    public string? ExpandedPrompt { get; init; }

    /// <summary>
    /// True when handling this command reset the active conversation state.
    /// </summary>
    public bool SessionReset { get; init; }

    /// <summary>
    /// Optional new thread id created by a session reset command.
    /// </summary>
    public string? NewThreadId { get; init; }

    /// <summary>
    /// Optional list of thread ids archived by a session reset command.
    /// </summary>
    public IReadOnlyList<string>? ArchivedThreadIds { get; init; }

    /// <summary>
    /// Whether the new thread is lazily materialized on disk.
    /// </summary>
    public bool? CreatedLazily { get; init; }
    
    /// <summary>
    /// Creates a result indicating the command was handled.
    /// </summary>
    public static CommandResult HandledResult(string? message = null, bool isMarkdown = false)
        => new() { Handled = true, Message = message, IsMarkdown = isMarkdown };
    
    /// <summary>
    /// Creates a result indicating the command expanded into an agent prompt.
    /// </summary>
    public static CommandResult PromptExpansion(string expandedPrompt)
        => new() { Handled = true, ExpandedPrompt = expandedPrompt };

    /// <summary>
    /// Creates a result for commands that reset the current conversation (for example <c>/new</c>).
    /// </summary>
    public static CommandResult SessionResetResult(
        string newThreadId,
        IReadOnlyList<string> archivedThreadIds,
        bool createdLazily,
        string? message = null,
        bool isMarkdown = false)
        => new()
        {
            Handled = true,
            Message = message,
            IsMarkdown = isMarkdown,
            SessionReset = true,
            NewThreadId = newThreadId,
            ArchivedThreadIds = archivedThreadIds,
            CreatedLazily = createdLazily
        };
    
    /// <summary>
    /// Creates a result indicating the command was not handled.
    /// </summary>
    public static CommandResult NotHandled()
        => new() { Handled = false };
    
    /// <summary>
    /// Implicit conversion from bool to CommandResult.
    /// </summary>
    public static implicit operator CommandResult(bool handled)
        => handled ? HandledResult() : NotHandled();
}
