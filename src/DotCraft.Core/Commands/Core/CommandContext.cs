using DotCraft.Agents;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Protocol;
using DotCraft.Sessions;

namespace DotCraft.Commands.Core;

/// <summary>
/// Provides context information for command handlers.
/// </summary>
public sealed record CommandContext
{
    /// <summary>
    /// The session identifier for the current conversation.
    /// </summary>
    public required string SessionId { get; init; }
    
    /// <summary>
    /// The raw command text input by the user.
    /// </summary>
    public required string RawText { get; init; }
    
    /// <summary>
    /// The parsed command (first word, lowercase).
    /// </summary>
    public string Command { get; init; } = string.Empty;
    
    /// <summary>
    /// The command arguments (remaining text after the command).
    /// </summary>
    public string[] Arguments { get; init; } = [];
    
    /// <summary>
    /// The user's identifier.
    /// </summary>
    public string UserId { get; init; } = string.Empty;
    
    /// <summary>
    /// The user's display name.
    /// </summary>
    public string UserName { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether the user is an admin.
    /// </summary>
    public bool IsAdmin { get; init; }
    
    /// <summary>
    /// The channel/source type (e.g., "qq", "wecom", "cli").
    /// </summary>
    public string Source { get; init; } = string.Empty;
    
    /// <summary>
    /// Optional group/chat identifier.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// The fully-qualified channel context key matching the format used when the thread was created.
    /// For QQ: "group:{groupId}" for group chats, "user:{userId}" for private chats.
    /// For WeCom: "chat:{chatId}".
    /// Null for channels with no sub-context (CLI, ACP).
    /// </summary>
    public string? ChannelContext { get; init; }
    
    /// <summary>
    /// The workspace path, used to construct a SessionIdentity for thread discovery.
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;

    /// <summary>
    /// The session service for managing conversation threads.
    /// </summary>
    public ISessionService? SessionService { get; init; }
    
    /// <summary>
    /// The heartbeat service (may be null if not enabled).
    /// </summary>
    public HeartbeatService? HeartbeatService { get; init; }
    
    /// <summary>
    /// The cron service (may be null if not enabled).
    /// </summary>
    public CronService? CronService { get; init; }
    
    /// <summary>
    /// The agent factory for token tracking.
    /// </summary>
    public AgentFactory? AgentFactory { get; init; }

    /// <summary>
    /// Registry of active agent runs, used by /stop to cancel an in-flight run.
    /// </summary>
    public ActiveRunRegistry? ActiveRunRegistry { get; init; }

    /// <summary>
    /// Active command registry for dynamic command listing and metadata lookup.
    /// </summary>
    public CommandRegistry? CommandRegistry { get; init; }
}
