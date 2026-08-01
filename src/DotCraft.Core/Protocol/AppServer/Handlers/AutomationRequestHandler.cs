namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Routes the <c>automation/*</c> wire methods to the host-provided
/// <see cref="IAutomationsRequestHandler"/>. The Automations module owns the actual task/template
/// logic; this handler is a thin pass-through that surfaces a method-not-found error when no
/// automations handler is registered.
/// </summary>
internal sealed class AutomationRequestHandler(IAutomationsRequestHandler? automationsHandler) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskList, (msg, ct) => Route(h => h.HandleTaskListAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskRead, (msg, ct) => Route(h => h.HandleTaskReadAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskCreate, (msg, ct) => Route(h => h.HandleTaskCreateAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskRun, (msg, ct) => Route(h => h.HandleTaskRunAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskDelete, (msg, ct) => Route(h => h.HandleTaskDeleteAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskDiscardWorktree, (msg, ct) => Route(h => h.HandleTaskDiscardWorktreeAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTaskUpdateBinding, (msg, ct) => Route(h => h.HandleTaskUpdateBindingAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTemplateList, (msg, ct) => Route(h => h.HandleTemplateListAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTemplateSave, (msg, ct) => Route(h => h.HandleTemplateSaveAsync(msg, ct)));
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.AutomationTemplateDelete, (msg, ct) => Route(h => h.HandleTemplateDeleteAsync(msg, ct)));
    }

    private Task<object?> Route(Func<IAutomationsRequestHandler, Task<object?>> action)
    {
        if (automationsHandler == null)
            throw AppServerErrors.MethodNotFound("automation/*");
        return action(automationsHandler);
    }
}
