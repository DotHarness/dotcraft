using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;

namespace DotCraft.Commands.Core;

/// <summary>
/// Provides context information for command handlers.
/// </summary>
public sealed record CommandContext
{
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

    public bool IsAdmin { get; init; }

    /// <summary>
    /// The session service for managing conversation threads.
    /// </summary>
    public ISessionService? SessionService { get; init; }

    public CommandRegistry? CommandRegistry { get; init; }

    public AppConfig? Config { get; init; }

    public ModelProviderRegistry? ModelProviders { get; init; }
}
