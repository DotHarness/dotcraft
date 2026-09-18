namespace DotCraft.Security.ShellCommands;

public sealed record ShellApprovalRequest
{
    public required string Command { get; init; }

    public required string WorkingDirectory { get; init; }

    public required ShellIdentity Shell { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> Commands { get; init; }

    public required bool IsPlain { get; init; }

    public string? LoweringRejectReason { get; init; }

    public required ShellRiskLevel Risk { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public required ShellApprovalKey ApprovalKey { get; init; }

    public required ShellRememberProposal Remember { get; init; }

    public string? Label { get; init; }

    public static ShellApprovalRequest FromAssessment(ShellAssessment assessment, string command, string workingDirectory) =>
        new()
        {
            Command = command,
            WorkingDirectory = workingDirectory,
            Shell = assessment.Shell!,
            Commands = assessment.Lowering!.PlainCommands ?? [],
            IsPlain = assessment.Lowering.IsPlain,
            LoweringRejectReason = assessment.Lowering.PlainRejectReason,
            Risk = assessment.Risk,
            Reasons = assessment.Reasons,
            ApprovalKey = assessment.ApprovalKey!,
            Remember = assessment.Remember
        };

    public string ReasonText => string.Join(" ", Reasons);
}
