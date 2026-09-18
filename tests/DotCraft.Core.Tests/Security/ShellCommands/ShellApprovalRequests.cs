using DotCraft.Security.ShellCommands;

namespace DotCraft.Tests.Security.ShellCommands;

internal static class ShellApprovalRequests
{
    public static ShellApprovalRequest For(string command, string? workingDirectory = null)
    {
        var cwd = Path.GetFullPath(string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory);
        var shell = new ShellIdentity(ShellKind.Bash, "/bin/bash");
        var lowering = LoweredScript.Opaque(ShellFamily.Posix, "test fixture");
        return new ShellApprovalRequest
        {
            Command = command,
            WorkingDirectory = cwd,
            Shell = shell,
            Commands = [],
            IsPlain = false,
            LoweringRejectReason = "test fixture",
            Risk = ShellRiskLevel.OutsideWorkspace,
            Reasons = ["test fixture"],
            ApprovalKey = ShellApprovalKey.Create(shell, cwd, lowering, command, "fixture"),
            Remember = ShellRememberProposal.ExactKeyOnly
        };
    }
}
