namespace DotCraft.Protocol;

/// <summary>
/// Per-thread override for how tool approvals are handled.
/// Serialized as camelCase strings (default, autoApprove, interrupt) via
/// <see cref="ThreadConfiguration.ApprovalPolicy"/> property converter.
/// </summary>
public enum ApprovalPolicy
{
    /// <summary>Default process-level behaviour (typically interactive prompt).</summary>
    Default,

    /// <summary>All tool calls are auto-approved; no user prompt is shown.</summary>
    AutoApprove,

    /// <summary>
    /// Tool calls that require approval cancel the current turn instead of prompting.
    /// </summary>
    Interrupt
}
