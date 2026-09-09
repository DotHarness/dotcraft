using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Host projection of the unified automation service.</summary>
public interface IAutomationsRequestHandler
{
    Task<Contract.AutomationRunsResult> HandleRunsReadAsync(Contract.AutomationRunReadParams parameters, CancellationToken ct);
    Task<Contract.AutomationListResult> HandleListAsync(global::DotCraft.Protocol.RpcEmpty parameters, CancellationToken ct);
    Task<Contract.AutomationReadResult> HandleReadAsync(Contract.AutomationIdParams parameters, CancellationToken ct);
    Task<Contract.AutomationReadResult> HandleCreateAsync(Contract.AutomationCreateParams parameters, CancellationToken ct);
    Task<Contract.AutomationReadResult> HandleUpdateAsync(Contract.AutomationUpdateParams parameters, CancellationToken ct);
    Task<Contract.AutomationDeleteResult> HandleDeleteAsync(Contract.AutomationIdParams parameters, CancellationToken ct);
    Task<Contract.AutomationRunResult> HandleRunAsync(Contract.AutomationIdParams parameters, CancellationToken ct);
    Task<Contract.AutomationRunsResult> HandleRunsListAsync(Contract.AutomationIdParams parameters, CancellationToken ct);
    Task<Contract.AutomationPresetsResult> HandlePresetsListAsync(Contract.AutomationPresetsParams parameters, CancellationToken ct);
}
