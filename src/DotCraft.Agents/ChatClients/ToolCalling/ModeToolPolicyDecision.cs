namespace DotCraft.Agents;

/// <summary>Result of an operational-mode tool policy decision.</summary>
public sealed record ModeToolPolicyDecision(
    ModeToolPolicyDecisionKind Kind,
    string? Message)
{
    public static ModeToolPolicyDecision Allow { get; } =
        new(ModeToolPolicyDecisionKind.Allow, null);

    public static ModeToolPolicyDecision DenyRecoverable(string message) =>
        new(ModeToolPolicyDecisionKind.DenyRecoverable, message);
}

/// <summary>Classification of a mode tool policy decision.</summary>
public enum ModeToolPolicyDecisionKind
{
    Allow,
    DenyRecoverable,
    DenyRequiresUserOrModeChange
}
