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
    /// Fired when a user prompt is submitted, before prompt assembly and model execution.
    /// Exit code 2 or a blocking JSON decision blocks the prompt.
    /// </summary>
    UserPromptSubmit,

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
    /// Fired before a permission request is shown.
    /// </summary>
    PermissionRequest,

    /// <summary>
    /// Fired before context compaction.
    /// </summary>
    PreCompact,

    /// <summary>
    /// Fired after context compaction.
    /// </summary>
    PostCompact,

    /// <summary>
    /// Fired before a subagent starts.
    /// </summary>
    SubagentStart,

    /// <summary>
    /// Fired after a subagent stops.
    /// </summary>
    SubagentStop,

    /// <summary>
    /// Fired after the agent finishes responding to a prompt.
    /// </summary>
    Stop,

    /// <summary>
    /// Fired after Stop hook execution or continuation fails.
    /// </summary>
    StopFailure
}
