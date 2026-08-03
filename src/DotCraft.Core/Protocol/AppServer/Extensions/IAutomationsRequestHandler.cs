namespace DotCraft.AppServer;

using Contract = DotCraft.Protocol.AppServer;

/// <summary>
/// Interface for the automations request handler, allowing <see cref="AppServerRequestHandler"/>
/// in DotCraft.Core to dispatch <c>automation/*</c> methods without referencing DotCraft.Automations.
/// </summary>
public interface IAutomationsRequestHandler
{
    Task<Contract.AutomationTaskListResult> HandleTaskListAsync(Contract.AutomationTaskListParams parameters, CancellationToken ct);
    Task<Contract.AutomationTask> HandleTaskReadAsync(Contract.AutomationTaskReadParams parameters, CancellationToken ct);
    Task<Contract.AutomationTaskCreateResult> HandleTaskCreateAsync(Contract.AutomationTaskCreateParams parameters, CancellationToken ct);
    Task<Contract.AutomationTaskRunResult> HandleTaskRunAsync(Contract.AutomationTaskRunParams parameters, CancellationToken ct);
    Task<Contract.AutomationTaskDeleteResult> HandleTaskDeleteAsync(Contract.AutomationTaskDeleteParams parameters, CancellationToken ct);
    Task<Contract.AutomationTaskDiscardWorktreeResult> HandleTaskDiscardWorktreeAsync(Contract.AutomationTaskDiscardWorktreeParams parameters, CancellationToken ct);
    Task<Contract.AutomationTaskUpdateBindingResult> HandleTaskUpdateBindingAsync(Contract.AutomationTaskUpdateBindingParams parameters, CancellationToken ct);
    Task<Contract.AutomationTemplateListResult> HandleTemplateListAsync(Contract.AutomationTemplateListParams parameters, CancellationToken ct);
    Task<Contract.AutomationTemplateSaveResult> HandleTemplateSaveAsync(Contract.AutomationTemplateSaveParams parameters, CancellationToken ct);
    Task<Contract.AutomationTemplateDeleteResult> HandleTemplateDeleteAsync(Contract.AutomationTemplateDeleteParams parameters, CancellationToken ct);
}
