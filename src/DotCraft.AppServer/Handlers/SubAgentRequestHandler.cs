using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

internal sealed class SubAgentRequestHandler(
    ISessionService sessionService,
    Func<ThreadHistorySnapshot, CancellationToken, Task<SessionWireThread>> projectThreadHeaderAsync) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentChildrenList, HandleSubAgentChildrenListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentFollowupTask, HandleSubAgentFollowupTaskAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentClose, HandleSubAgentCloseAsync);
    }

    private async Task<AppServerTypedResult<Contract.SubAgentChildrenListResult>> HandleSubAgentChildrenListAsync(
        AppServerTypedRequest<Contract.SubAgentChildrenListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        if (string.IsNullOrWhiteSpace(parentThreadId))
            throw AppServerErrors.InvalidParams("'parentThreadId' is required.");

        var edges = await sessionService.ListSubAgentChildrenAsync(
            parentThreadId.Trim(),
            SubAgentContractMapper.Read(p.IncludeClosed) ?? false,
            ct);
        var data = new List<Contract.SubAgentChild>();
        foreach (var edge in edges)
        {
            Contract.SessionThread? thread = null;
            if (SubAgentContractMapper.Read(p.IncludeThreads) == true)
            {
                var snapshot = await sessionService.ReadThreadSnapshotAsync(edge.ChildThreadId, ct);
                thread = AppServerContractMapper.ToContract(await projectThreadHeaderAsync(snapshot, ct));
            }

            data.Add(new Contract.SubAgentChild
            {
                Edge = SubAgentContractMapper.ToContract(edge),
                Thread = thread is null
                    ? default
                    : Protocol.Optional<Contract.SessionThread?>.FromValue(thread)
            });
        }

        return AppServerTypedResult<Contract.SubAgentChildrenListResult>.FromResult(
            new Contract.SubAgentChildrenListResult { Data = data });
    }

    private async Task<AppServerTypedResult<Contract.SubAgentControlResult>> HandleSubAgentFollowupTaskAsync(
        AppServerTypedRequest<Contract.SubAgentTargetMessageParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        var target = SubAgentContractMapper.Read(p.Target);
        var message = SubAgentContractMapper.Read(p.Message);
        if (string.IsNullOrWhiteSpace(parentThreadId) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(parentThreadId.Trim(), ct);
        var result = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            target.Trim(),
            message,
            ct,
            ParseDeliveryMode(SubAgentContractMapper.Read(p.DeliveryMode)));
        return AppServerTypedResult<Contract.SubAgentControlResult>.FromResult(SubAgentContractMapper.ToContract(result));
    }

    private async Task<AppServerTypedResult<Contract.SubAgentControlResult>> HandleSubAgentCloseAsync(
        AppServerTypedRequest<Contract.SubAgentTargetParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        var target = SubAgentContractMapper.Read(p.Target);
        if (string.IsNullOrWhiteSpace(parentThreadId) || string.IsNullOrWhiteSpace(target))
            throw AppServerErrors.InvalidParams("'parentThreadId' and 'target' are required.");

        var context = await BuildSubAgentControlContextAsync(parentThreadId.Trim(), ct);
        var result = await SubAgentSessionControl.CloseAgentAsync(context, target.Trim(), ct);
        return AppServerTypedResult<Contract.SubAgentControlResult>.FromResult(SubAgentContractMapper.ToContract(result));
    }

    private async Task<SubAgentSessionContext> BuildSubAgentControlContextAsync(string parentThreadId, CancellationToken ct)
    {
        var parent = await sessionService.GetThreadAsync(parentThreadId, ct);
        var source = parent.Source.SubAgent;
        return new SubAgentSessionContext
        {
            SessionService = sessionService,
            ParentThread = parent,
            ParentTurnId = string.Empty,
            RootThreadId = string.IsNullOrWhiteSpace(source?.RootThreadId) ? parent.Id : source.RootThreadId,
            Depth = source?.Depth ?? 0,
            LifecycleHook = sessionService is SessionService service
                ? service.RunSubAgentLifecycleHookAsync
                : null
        };
    }

    private static SubAgentFollowupDeliveryMode ParseDeliveryMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SubAgentFollowupDeliveryMode.Queue;
        if (Enum.TryParse<SubAgentFollowupDeliveryMode>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw AppServerErrors.InvalidParams("'deliveryMode' must be 'queue' or 'steer'.");
    }
}
