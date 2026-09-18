namespace DotCraft.Security.ShellCommands;

public sealed class ShellSafetyRequest
{
    public required string Command { get; init; }

    public string? ShellSelector { get; init; }

    /// <summary>Identity of an already running shell; when set the selector is not resolved.</summary>
    public ShellIdentity? ResolvedShell { get; init; }

    public required string WorkingDirectory { get; init; }

    /// <summary>False when the caller already knows <see cref="WorkingDirectory"/> is stale.</summary>
    public bool WorkingDirectoryIsKnown { get; init; } = true;

    public required WorkspaceBoundary Workspace { get; init; }

    public ShellPolicy Policy { get; init; } = ShellPolicy.Empty;

    public PathBlacklist? Blacklist { get; init; }

    public bool RequireApprovalOutsideWorkspace { get; init; } = true;

    public bool AutoApprovesPrompts { get; init; }
}

public enum ShellRiskLevel
{
    None,
    Rule,
    OutsideWorkspace,
    Dangerous
}

public sealed record ShellRememberProposal(IReadOnlyList<ShellPrefixRule> Rules, bool ExactKeyFallback)
{
    public static ShellRememberProposal ExactKeyOnly { get; } = new([], ExactKeyFallback: true);
}

public sealed class ShellAssessment
{
    public required ShellDecision Decision { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public ShellIdentity? Shell { get; init; }

    public LoweredScript? Lowering { get; init; }

    public IReadOnlyList<ShellRuleMatch> Matches { get; init; } = [];

    public ShellApprovalKey? ApprovalKey { get; init; }

    public ShellRiskLevel Risk { get; init; }

    /// <summary>Directory tracked through the last command, or null when it ended undeterminable.</summary>
    public string? WorkingDirectoryAfter { get; init; }

    public ShellRememberProposal Remember { get; init; } = ShellRememberProposal.ExactKeyOnly;

    public string ReasonText => string.Join(" ", Reasons);
}
