namespace DotCraft.Security;

public static class ApprovalServiceChain
{
    public static IApprovalService? Unwrap(IApprovalService? approvalService, ApprovalContext? context)
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

    public static bool AutoApproves(IApprovalService? approvalService, ApprovalContext? context) =>
        Unwrap(approvalService, context) is AutoApproveApprovalService;
}
