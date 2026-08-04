using DotCraft.Tools.BackgroundTerminals;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>terminal/*</c> wire methods: list, read, write-stdin, stop, and clean for the
/// background terminal sessions bound to a thread.
/// </summary>
internal sealed class TerminalRequestHandler(
    IBackgroundTerminalService? backgroundTerminalService,
    ISessionService sessionService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.TerminalList, HandleTerminalListAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.TerminalRead, HandleTerminalReadAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.TerminalWrite, HandleTerminalWriteAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.TerminalStop, HandleTerminalStopAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.TerminalClean, HandleTerminalCleanAsync);
    }

    private async Task<AppServerTypedResult<Contract.TerminalListResult>> HandleTerminalListAsync(
        AppServerTypedRequest<Contract.TerminalListParams> request,
        CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var threadId = request.Params.ThreadId.IsSet ? request.Params.ThreadId.Value : null;
        var sessions = await service.ListAsync(threadId, ct);
        return AppServerTypedResult<Contract.TerminalListResult>.FromResult(new Contract.TerminalListResult
        {
            Terminals = sessions.Select(TerminalContractMapper.ToContract).ToList()
        });
    }

    private async Task<AppServerTypedResult<Contract.TerminalReadResult>> HandleTerminalReadAsync(
        AppServerTypedRequest<Contract.TerminalReadParams> request,
        CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = request.Params;
        var sessionId = p.SessionId.IsSet ? p.SessionId.Value : null;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.ReadAsync(
            sessionId,
            p.WaitMs.IsSet ? p.WaitMs.Value ?? 0 : 0,
            p.MaxOutputChars.IsSet ? p.MaxOutputChars.Value : null,
            ct);
        return AppServerTypedResult<Contract.TerminalReadResult>.FromResult(
            new Contract.TerminalReadResult { Terminal = TerminalContractMapper.ToContract(terminal) });
    }

    private async Task<AppServerTypedResult<Contract.TerminalWriteResult>> HandleTerminalWriteAsync(
        AppServerTypedRequest<Contract.TerminalWriteParams> request,
        CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = request.Params;
        var sessionId = p.SessionId.IsSet ? p.SessionId.Value : null;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.WriteStdinAsync(
            sessionId,
            p.Input.IsSet ? p.Input.Value ?? string.Empty : string.Empty,
            p.YieldTimeMs.IsSet ? p.YieldTimeMs.Value ?? 1000 : 1000,
            p.MaxOutputChars.IsSet ? p.MaxOutputChars.Value : null,
            ct);
        return AppServerTypedResult<Contract.TerminalWriteResult>.FromResult(
            new Contract.TerminalWriteResult { Terminal = TerminalContractMapper.ToContract(terminal) });
    }

    private async Task<AppServerTypedResult<Contract.TerminalStopResult>> HandleTerminalStopAsync(
        AppServerTypedRequest<Contract.TerminalStopParams> request,
        CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var sessionId = request.Params.SessionId.IsSet ? request.Params.SessionId.Value : null;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.StopAsync(sessionId, ct);
        return AppServerTypedResult<Contract.TerminalStopResult>.FromResult(
            new Contract.TerminalStopResult { Terminal = TerminalContractMapper.ToContract(terminal) });
    }

    private async Task<AppServerTypedResult<Contract.TerminalCleanResult>> HandleTerminalCleanAsync(
        AppServerTypedRequest<Contract.TerminalCleanParams> request,
        CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var threadId = request.Params.ThreadId.IsSet ? request.Params.ThreadId.Value : null;
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        await sessionService.CleanBackgroundTerminalsAsync(threadId, ct);
        var terminals = await service.ListAsync(threadId, ct);
        return AppServerTypedResult<Contract.TerminalCleanResult>.FromResult(new Contract.TerminalCleanResult
        {
            Terminals = terminals.Select(TerminalContractMapper.ToContract).ToList()
        });
    }

    private IBackgroundTerminalService RequireBackgroundTerminals()
        => backgroundTerminalService ?? throw AppServerErrors.MethodNotFound("terminal/*");
}
