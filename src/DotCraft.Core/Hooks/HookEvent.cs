namespace DotCraft.Hooks;

/// <summary>
/// Lifecycle events at which hooks can be triggered.
/// </summary>
public enum HookEvent
{
    /// <summary>
    /// Fired when a session is created or resumed.
    /// Hook stdout is injected as additional context.
    /// </summary>
    SessionStart,

    /// <summary>
    /// Fired before a tool executes.
    /// Exit code 2 blocks the tool call; stderr becomes the block reason.
    /// </summary>
    PreToolUse,

    /// <summary>
    /// Fired after a tool executes successfully.
    /// </summary>
    PostToolUse,

    /// <summary>
    /// Fired after a tool execution fails with an exception.
    /// </summary>
    PostToolUseFailure,

    /// <summary>
    /// Fired before a user prompt is sent to the agent.
    /// Exit code 2 blocks the prompt; stderr becomes the block reason.
    /// </summary>
    PrePrompt,

    /// <summary>
    /// Fired after the agent finishes responding to a prompt.
    /// </summary>
    Stop
}
