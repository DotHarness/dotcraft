namespace DotCraft.Sessions;

/// <summary>
/// Per-thread override for how tool approvals are handled.
/// Serialized as camelCase strings (default, prompt, autoApprove, deny) via
/// <see cref="ThreadConfiguration.ApprovalPolicy"/> property converter.
/// </summary>
public enum ApprovalPolicy
{
    /// <summary>Default process-level behaviour: consult the workspace default approval policy.</summary>
    Default,

    /// <summary>All tool calls are auto-approved; no user prompt is shown.</summary>
    AutoApprove,

    /// <summary>
    /// Tool calls that require approval are rejected without prompting. The calling tool
    /// receives the rejection as its result, so the turn continues.
    /// </summary>
    Deny,

    /// <summary>
    /// Always use the interactive approval flow for this thread, regardless of the
    /// workspace default. Forces per-thread review even when the workspace default
    /// is <see cref="AutoApprove"/>.
    /// </summary>
    Prompt
}
