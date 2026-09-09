namespace DotCraft.AppServer;

using Contract = DotCraft.Protocol.AppServer;

/// <summary>
/// Routes the <c>automation/*</c> wire methods to the host-provided
/// <see cref="IAutomationsRequestHandler"/>. The Automations module owns the definition and run
/// logic; this handler is a thin pass-through that surfaces a method-not-found error when no
/// automations handler is registered.
/// </summary>
internal sealed class AutomationRequestHandler(IAutomationsRequestHandler? automationsHandler) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.AutomationRunsRead, (request, ct) => Route(request, (h, p) => h.HandleRunsReadAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationList, (request, ct) => Route(request, (h, p) => h.HandleListAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationRead, (request, ct) => Route(request, (h, p) => h.HandleReadAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationCreate, (request, ct) => Route(request, (h, p) => h.HandleCreateAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationUpdate, (request, ct) => Route(request, (h, p) => h.HandleUpdateAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationDelete, (request, ct) => Route(request, (h, p) => h.HandleDeleteAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationRun, (request, ct) => Route(request, (h, p) => h.HandleRunAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationRunsList, (request, ct) => Route(request, (h, p) => h.HandleRunsListAsync(p, ct)));
        table.Map(Contract.AppServerRpc.AutomationPresetsList, (request, ct) => Route(request, (h, p) => h.HandlePresetsListAsync(p, ct)));

    }

    private async Task<AppServerTypedResult<TResult>> Route<TParams, TResult>(
        AppServerTypedRequest<TParams> request,
        Func<IAutomationsRequestHandler, TParams, Task<TResult>> action)
        where TParams : class
        where TResult : class
    {
        if (automationsHandler == null)
            throw AppServerErrors.MethodNotFound("automation/*");
        try
        {
            return AppServerTypedResult<TResult>.FromResult(await action(automationsHandler, request.Params));
        }
        catch (KeyNotFoundException) { throw AppServerErrors.AutomationFailure("automation.notFound", "Automation not found."); }
        catch (ArgumentException ex) when (ex.Message.StartsWith("automation.", StringComparison.Ordinal))
        { throw AppServerErrors.AutomationValidationFailure(ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("automation.", StringComparison.Ordinal))
        {
            throw AppServerErrors.AutomationFailure(ex.Message, ex.Message switch
            {
                "automation.versionConflict" => "This automation changed. Reload it before saving.",
                "automation.completedReadOnly" => "Completed automations are read-only.",
                "automation.running" or "automation.alreadyRunning" => "This automation is already running.",
                "automation.hostOffline" => "The automation execution host is offline.",
                _ => "The automation operation is unavailable."
            });
        }
    }
}
