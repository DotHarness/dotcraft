using DotCraft.Protocol;
using DotCraft.Security;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed class NodeReplProtocolExtension(WireNodeReplProxy proxy) : IAppServerContractExtension
{
    private const string ComputerUseApprovalType = "computerUse";

    public IReadOnlyCollection<string> Methods { get; } =
        [Contract.AppServerRpc.ExtNodeReplRequestApproval.Name];

    public IReadOnlyCollection<IRpcMethodDescriptor> ContractMethods { get; } =
        [Contract.AppServerRpc.ExtNodeReplRequestApproval];

    public void ContributeCapabilities(AppServerCapabilityBuilder builder)
    {
    }

    public async Task<object?> HandleContractAsync(
        IRpcMethodDescriptor descriptor,
        object parameters,
        AppServerIncomingMessage message,
        AppServerExtensionContext context)
    {
        var request = (Contract.NodeReplRequestApprovalParams)parameters;
        var threadId = Required(request.ThreadId, "threadId");
        var evaluationId = Required(request.EvaluationId, "evaluationId");
        var approvalType = Required(request.ApprovalType, "approvalType");
        if (!string.Equals(approvalType, ComputerUseApprovalType, StringComparison.Ordinal))
            throw AppServerErrors.InvalidParams($"approvalType '{approvalType}' is not supported.");

        var approval = new ResourceApprovalRequest(
            approvalType,
            Required(request.Operation, "operation"),
            Required(request.Target, "target"))
        {
            TargetLabel = request.TargetLabel.IsSet ? request.TargetLabel.Value : null,
            PersistAcceptAlways = false
        };

        try
        {
            var approved = await proxy
                .RequestApprovalAsync(context.Connection, threadId, evaluationId, approval)
                .ConfigureAwait(false);
            return new Contract.NodeReplRequestApprovalResult { Approved = approved };
        }
        catch (KeyNotFoundException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
    }

    private static string Required(Optional<string> value, string name) =>
        value.IsSet && !string.IsNullOrWhiteSpace(value.Value)
            ? value.Value
            : throw AppServerErrors.InvalidParams($"{name} is required.");
}
