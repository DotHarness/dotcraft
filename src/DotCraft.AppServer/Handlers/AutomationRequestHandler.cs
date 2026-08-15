namespace DotCraft.AppServer;

using Contract = DotCraft.Protocol.AppServer;

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
        table.Map(Contract.AppServerRpc.AutomationTaskList, (request, ct) => Route(request, (h, p) => h.HandleTaskListAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskRead, (request, ct) => Route(request, (h, p) => h.HandleTaskReadAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskCreate, (request, ct) => Route(request, (h, p) => h.HandleTaskCreateAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskRun, (request, ct) => Route(request, (h, p) => h.HandleTaskRunAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskDelete, (request, ct) => Route(request, (h, p) => h.HandleTaskDeleteAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskDiscardWorktree, (request, ct) => Route(request, (h, p) => h.HandleTaskDiscardWorktreeAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTaskUpdateBinding, (request, ct) => Route(request, (h, p) => h.HandleTaskUpdateBindingAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTemplateList, (request, ct) => Route(request, (h, p) => h.HandleTemplateListAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTemplateSave, (request, ct) => Route(request, (h, p) => h.HandleTemplateSaveAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationTemplateDelete, (request, ct) => Route(request, (h, p) => h.HandleTemplateDeleteAsync(p, ct)));
    }

    private async Task<AppServerTypedResult<TResult>> Route<TParams, TResult>(
        AppServerTypedRequest<TParams> request,
        Func<IAutomationsRequestHandler, TParams, Task<TResult>> action)
        where TParams : class
        where TResult : class
    {
        if (automationsHandler == null)
            throw AppServerErrors.MethodNotFound("automation/*");
        return AppServerTypedResult<TResult>.FromResult(await action(automationsHandler, request.Params));
    }
}
