using DotCraft.Protocol;
using DotCraft.Security;

namespace DotCraft.Agents;

internal static class SubAgentApprovalModeResolver
{
    public const string InteractiveMode = "interactive";
    public const string AutoApproveMode = "auto-approve";
    public const string RestrictedMode = "restricted";

    public static string Resolve(IApprovalService? approvalService, ApprovalContext? context)
    {
        var effectiveService = Unwrap(approvalService, context);
        return effectiveService switch
        {
            null => RestrictedMode,
            AutoApproveApprovalService => AutoApproveMode,
            InterruptOnApprovalService => RestrictedMode,
            SessionApprovalService => InteractiveMode,
            ConsoleApprovalService => InteractiveMode,
            _ => RestrictedMode
        };
    }

    private static IApprovalService? Unwrap(IApprovalService? approvalService, ApprovalContext? context)
    {
        var current = approvalService;
        var guard = 0;
        while (current != null && guard++ < 8)
        {
            switch (current)
            {
                case SessionScopedApprovalService scoped:
                    current = scoped.GetEffectiveService();
                    continue;
                case ChannelRoutingApprovalService routed:
                    current = routed.ResolveForContext(context);
                    continue;
                default:
                    return current;
            }
        }

        return current;
    }
}
