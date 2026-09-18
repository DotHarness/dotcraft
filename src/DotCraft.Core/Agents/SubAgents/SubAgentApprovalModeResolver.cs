using DotCraft.Security;
using DotCraft.Sessions;

namespace DotCraft.Agents;

internal static class SubAgentApprovalModeResolver
{
    public const string InteractiveMode = "interactive";
    public const string AutoApproveMode = "auto-approve";
    public const string RestrictedMode = "restricted";

    public static string Resolve(IApprovalService? approvalService, ApprovalContext? context)
    {
        var effectiveService = ApprovalServiceChain.Unwrap(approvalService, context);
        return effectiveService switch
        {
            null => RestrictedMode,
            AutoApproveApprovalService => AutoApproveMode,
            DenyApprovalService => RestrictedMode,
            SessionApprovalService => InteractiveMode,
            ConsoleApprovalService => InteractiveMode,
            _ => RestrictedMode
        };
    }
}
