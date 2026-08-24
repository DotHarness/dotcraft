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

    /// <summary>Peels every <see cref="IApprovalServiceDecorator"/> so decoration order cannot change the resolved mode.</summary>
    private static IApprovalService? Unwrap(IApprovalService? approvalService, ApprovalContext? context)
    {
        var current = approvalService;
        for (var guard = 0; current is IApprovalServiceDecorator decorator && guard < 8; guard++)
        {
            var inner = decorator.GetInnerApprovalService(context);
            if (inner == null || ReferenceEquals(inner, current))
                return current;
            current = inner;
        }

        return current;
    }
}
