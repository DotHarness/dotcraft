using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Dreams;
using Contract = DotCraft.Protocol.AppServer;
using DreamsRunState = DotCraft.Dreams.DreamsRunState;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>dreams/*</c> wire methods: status, run, create, get, list, cancel, archive, apply, discard.
/// </summary>
internal sealed class DreamsRequestHandler(
    DreamsService? dreamsService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.DreamsStatus, HandleDreamsStatusAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsRun, HandleDreamsRunAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsCreate, HandleDreamsCreateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsGet, HandleDreamsGetAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsList, HandleDreamsListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsCancel, HandleDreamsCancelAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsArchive, HandleDreamsArchiveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsApply, HandleDreamsApplyAsync);
        table.Map(Protocol.AppServer.AppServerRpc.DreamsDiscard, HandleDreamsDiscardAsync);
    }

    private Task<object?> HandleDreamsStatusAsync(AppServerTypedRequest<Contract.DreamsStatusParams> request, CancellationToken ct)
    {
        _ = ct;
        var service = RequireService();
        ValidateEmptyObjectParams(request.Message, Protocol.AppServer.AppServerMethodNames.DreamsStatus);
        return Task.FromResult<object?>(DreamsContractMapper.ToStatus(service, appConfigMonitor?.Current.Dreams ?? new DreamsConfig()));
    }

    private async Task<object?> HandleDreamsRunAsync(AppServerTypedRequest<Contract.DreamsRunParams> request, CancellationToken ct)
    {
        var service = RequireService();
        ValidateEmptyObjectParams(request.Message, Protocol.AppServer.AppServerMethodNames.DreamsRun);
        await service.RequestRunAsync(ct).ConfigureAwait(false);
        return DreamsContractMapper.ToStatus(service, appConfigMonitor?.Current.Dreams ?? new DreamsConfig());
    }

    private async Task<object?> HandleDreamsCreateAsync(AppServerTypedRequest<Contract.DreamsCreateParams> request, CancellationToken ct)
    {
        var service = RequireService();
        var p = request.Params;
        DreamsRunState? state;
        try
        {
            state = await service.CreateRunAsync(
                    new DreamsRunRequest(
                        ValueOrDefault(p.Scope),
                        ValueOrDefault(p.ThreadIds)?.ToList(),
                        ValueOrDefault(p.Instructions),
                        ValueOrDefault(p.Model)),
                    ct)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        if (state == null)
            throw AppServerErrors.InvalidRequest("Dreams and memory must both be enabled to start a run.");
        return new Contract.DreamsRunResult { Run = DreamsContractMapper.ToContract(state) };
    }

    private Task<object?> HandleDreamsGetAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        var review = RequireService().GetRunReview(NormalizeRunId(ValueOrDefault(request.Params.RunId)))
                     ?? throw AppServerErrors.InvalidParams("Dream run not found.");
        return Task.FromResult<object?>(DreamsContractMapper.ToContract(review));
    }

    private Task<object?> HandleDreamsListAsync(AppServerTypedRequest<Contract.DreamsListParams> request, CancellationToken ct)
    {
        _ = ct;
        var service = RequireService();
        return Task.FromResult<object?>(new Contract.DreamsListResult
        {
            Runs = new Protocol.Optional<IReadOnlyList<Contract.DreamsRunState>>(
                service.ListRuns(ValueOrDefault(request.Params.IncludeArchived)).Select(DreamsContractMapper.ToContract).ToArray())
        });
    }

    private async Task<object?> HandleDreamsCancelAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        var state = await RequireService().CancelRunAsync(NormalizeRunId(ValueOrDefault(request.Params.RunId)), ct).ConfigureAwait(false);
        return RunResult(state);
    }

    private Task<object?> HandleDreamsArchiveAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        return Task.FromResult(Review(request, static (service, runId) => service.ArchiveRun(runId)));
    }

    private Task<object?> HandleDreamsApplyAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        try
        {
            return Task.FromResult(Review(request, static (service, runId) => service.ApplyRun(runId)));
        }
        catch (DreamsStoreChangedException ex)
        {
            throw AppServerErrors.DreamsStoreChanged(ex.Message);
        }
    }

    private Task<object?> HandleDreamsDiscardAsync(AppServerTypedRequest<Contract.DreamsRunIdParams> request, CancellationToken ct)
    {
        _ = ct;
        return Task.FromResult(Review(request, static (service, runId) => service.DiscardRun(runId)));
    }

    private object? Review(
        AppServerTypedRequest<Contract.DreamsRunIdParams> request,
        Func<DreamsService, string, DreamsRunState?> action)
    {
        var service = RequireService();
        var runId = NormalizeRunId(ValueOrDefault(request.Params.RunId));
        try
        {
            return RunResult(action(service, runId));
        }
        catch (InvalidOperationException ex) when (ex is not DreamsStoreChangedException)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }
    }

    private static object RunResult(DreamsRunState? state) =>
        state == null
            ? throw AppServerErrors.InvalidParams("Dream run not found.")
            : new Contract.DreamsRunResult { Run = DreamsContractMapper.ToContract(state) };

    private DreamsService RequireService() =>
        dreamsService != null && !string.IsNullOrWhiteSpace(workspaceCraftPath)
            ? dreamsService
            : throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.DreamsStatus);

    private static string NormalizeRunId(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw AppServerErrors.InvalidParams("'runId' is required.");
        return runId.Trim();
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static void ValidateEmptyObjectParams(AppServerIncomingMessage msg, string method)
    {
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind is not JsonValueKind.Null
                and not JsonValueKind.Object
                and not JsonValueKind.Undefined)
        {
            throw AppServerErrors.InvalidParams($"{method} accepts omitted, null, or empty-object params.");
        }
    }
}
