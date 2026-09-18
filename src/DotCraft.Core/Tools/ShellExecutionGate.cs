using DotCraft.Security;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Tools;

public sealed record ShellGateResult(string? Error, ShellIdentity? Shell, ShellAssessment Assessment)
{
    public bool IsAllowed => Error is null;

    public static ShellGateResult Denied(string error, ShellAssessment assessment) => new(error, null, assessment);

    public static ShellGateResult Allowed(ShellAssessment assessment) => new(null, assessment.Shell, assessment);
}

public sealed class ShellExecutionGate(
    ShellCommandSafetyKernel kernel,
    WorkspaceBoundary workspace,
    ShellPolicySource policy,
    PathBlacklist? blacklist,
    bool requireApprovalOutsideWorkspace,
    IApprovalService? approvalService)
{
    public ShellAssessment Assess(string command, string? shellSelector, string workingDirectory) =>
        kernel.Evaluate(new ShellSafetyRequest
        {
            Command = command,
            ShellSelector = shellSelector,
            WorkingDirectory = workingDirectory,
            Workspace = workspace,
            Policy = policy.Current,
            Blacklist = blacklist,
            RequireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace,
            AutoApprovesPrompts = ApprovalServiceChain.AutoApproves(approvalService, ApprovalContextScope.Current)
        });

    public async Task<ShellGateResult> AuthorizeAsync(
        string command,
        string? shellSelector,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assessment = Assess(command, shellSelector, workingDirectory);
        switch (assessment.Decision)
        {
            case ShellDecision.Forbidden:
                return ShellGateResult.Denied($"Error: {assessment.ReasonText}", assessment);
            case ShellDecision.Prompt:
                if (approvalService is null)
                {
                    return ShellGateResult.Denied(
                        $"Error: Command requires approval but no approval service is available. {assessment.ReasonText}",
                        assessment);
                }

                var context = ApprovalContextScope.Current;
                var request = ShellApprovalRequest.FromAssessment(assessment, command, workingDirectory);
                var approved = await approvalService
                    .RequestShellApprovalAsync(request, context)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!approved)
                    return ShellGateResult.Denied("Error: Command execution was rejected by user.", assessment);
                break;
        }

        return ShellGateResult.Allowed(assessment);
    }
}
