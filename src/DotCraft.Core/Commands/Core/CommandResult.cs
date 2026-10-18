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

    public string? ThreadId { get; init; }

    public IReadOnlyList<CommandChoice>? Choices { get; init; }

    /// <summary>
    /// Creates a result indicating the command was handled.
    /// </summary>
    public static CommandResult HandledResult(
        string? message = null,
        bool isMarkdown = false,
        IReadOnlyList<CommandChoice>? choices = null)
        => new() { Handled = true, Message = message, IsMarkdown = isMarkdown, Choices = choices is { Count: > 0 } ? choices : null };

    /// <summary>
    /// Creates a result indicating the command expanded into an agent prompt.
    /// </summary>
    public static CommandResult PromptExpansion(string expandedPrompt)
        => new() { Handled = true, ExpandedPrompt = expandedPrompt };

    public static CommandResult SwitchThread(string threadId, string? message = null)
        => new() { Handled = true, Message = message, ThreadId = threadId };

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

public sealed record CommandChoice(string Label, string Command);
