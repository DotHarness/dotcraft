namespace DotCraft.Security;

/// <summary>Result returned by a host-provided interactive approval prompt.</summary>
public enum InteractiveApprovalDecision
{
    Once,
    Session,
    Always,
    Reject
}

/// <summary>
/// Host boundary for interactive approval presentation. Core owns the approval
/// policy while an application owns terminal rendering and input.
/// </summary>
public interface IInteractiveApprovalPrompt
{
    InteractiveApprovalDecision RequestFileApproval(string operation, string path);

    InteractiveApprovalDecision RequestShellApproval(string command, string? workingDirectory);
}
