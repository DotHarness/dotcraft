namespace DotCraft.Security;

public sealed class ApprovalContext
{
    public string UserId { get; init; } = "";

    public string UserRole { get; init; } = string.Empty;

    public long GroupId { get; init; }

    /// <summary>
    /// The channel name that originated this approval context.
    /// Used by <see cref="ChannelRoutingApprovalService"/> to route approval requests
    /// to the correct channel-specific approval service.
    /// </summary>
    public string Source { get; init; } = "";

    public bool IsGroupContext => GroupId > 0;
}

/// <summary>
/// Service for requesting user approval for sensitive operations.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Request user approval for a file operation outside workspace.
    /// </summary>
    /// <param name="operation">The operation name (read, write, edit, list)</param>
    /// <param name="path">The file path</param>
    /// <param name="context">Optional approval context with user info</param>
    /// <returns>True if approved, false if rejected</returns>
    Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null);

    /// <summary>
    /// Request user approval for a shell command that accesses paths outside workspace.
    /// </summary>
    /// <param name="command">The shell command</param>
    /// <param name="workingDir">The working directory</param>
    /// <param name="context">Optional approval context with user info</param>
    /// <returns>True if approved, false if rejected</returns>
    Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null);

    /// <summary>
    /// Request user approval for a generic remote-resource operation that is neither a
    /// local-file nor a shell command (e.g. operations on third-party services such as
    /// Feishu docx / wiki nodes, where the approval layer only needs a yes/no gate).
    /// </summary>
    /// <param name="kind">Approval category, e.g. "remoteResource"</param>
    /// <param name="operation">Operation label shown to the user (e.g. "create", "append", "move")</param>
    /// <param name="target">Primary target identifier (document id, node token, URL, etc.)</param>
    /// <param name="context">Optional approval context with user info</param>
    /// <returns>True if approved, false if rejected</returns>
    Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null);
}
