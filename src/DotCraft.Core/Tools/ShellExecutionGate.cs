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
    private const string StdinNotice =
        "The text is written to a running terminal whose own directory and state may have moved since it started.";

    public ShellAssessment Assess(string command, string? shellSelector, string workingDirectory) =>
        kernel.Evaluate(Request(command, workingDirectory, shellSelector: shellSelector));

    private ShellSafetyRequest Request(
        string command,
        string workingDirectory,
        string? shellSelector = null,
        ShellIdentity? resolvedShell = null,
        bool workingDirectoryIsKnown = true) => new()
        {
            Command = command,
            ShellSelector = shellSelector,
            ResolvedShell = resolvedShell,
            WorkingDirectory = workingDirectory,
            WorkingDirectoryIsKnown = workingDirectoryIsKnown,
            Workspace = workspace,
            Policy = policy.Current,
            Blacklist = blacklist,
            RequireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace,
            AutoApprovesPrompts = ApprovalServiceChain.AutoApproves(approvalService, ApprovalContextScope.Current)
        };

    public Task<ShellGateResult> AuthorizeAsync(
        string command,
        string? shellSelector,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return DecideAsync(
            Assess(command, shellSelector, workingDirectory),
            command,
            workingDirectory,
            notice: null,
            rejection: "Error: Command execution was rejected by user.",
            cancellationToken);
    }

    public async Task<ShellGateResult> AuthorizeStdinAsync(
        ShellStdinSession session,
        string input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workingDirectory, workingDirectoryIsKnown) = session.Location;
        var assessment = kernel.Evaluate(Request(
            input,
            workingDirectory,
            resolvedShell: session.Shell,
            workingDirectoryIsKnown: workingDirectoryIsKnown));
        var result = await DecideAsync(
            assessment,
            input,
            workingDirectory,
            StdinNotice,
            rejection: "Error: Terminal input was rejected by user.",
            cancellationToken).ConfigureAwait(false);
        if (result.IsAllowed)
            session.TrackWorkingDirectory(assessment.WorkingDirectoryAfter);
        return result;
    }

    private async Task<ShellGateResult> DecideAsync(
        ShellAssessment assessment,
        string command,
        string workingDirectory,
        string? notice,
        string rejection,
        CancellationToken cancellationToken)
    {
        var reasonText = notice is null ? assessment.ReasonText : $"{assessment.ReasonText} {notice}";
        switch (assessment.Decision)
        {
            case ShellDecision.Forbidden:
                return ShellGateResult.Denied($"Error: {reasonText}", assessment);
            case ShellDecision.Prompt:
                if (approvalService is null)
                {
                    return ShellGateResult.Denied(
                        $"Error: Command requires approval but no approval service is available. {reasonText}",
                        assessment);
                }

                var context = ApprovalContextScope.Current;
                var request = ShellApprovalRequest.FromAssessment(assessment, command, workingDirectory);
                if (notice is not null)
                    request = request with { Reasons = [.. assessment.Reasons, notice] };
                var approved = await approvalService
                    .RequestShellApprovalAsync(request, context)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!approved)
                    return ShellGateResult.Denied(rejection, assessment);
                break;
        }

        return ShellGateResult.Allowed(assessment);
    }
}
