using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>terminal/*</c> wire methods: list, read, write-stdin, stop, and clean for the
/// background terminal sessions bound to a thread. Extracted from
/// <see cref="AppServerRequestHandler"/> as part of the Core architecture refactor (M3).
/// </summary>
internal sealed class TerminalRequestHandler(
    IBackgroundTerminalService? backgroundTerminalService,
    ISessionService sessionService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.TerminalList, HandleTerminalListAsync);
        table.Map(AppServerMethods.TerminalRead, HandleTerminalReadAsync);
        table.Map(AppServerMethods.TerminalWrite, HandleTerminalWriteAsync);
        table.Map(AppServerMethods.TerminalStop, HandleTerminalStopAsync);
        table.Map(AppServerMethods.TerminalClean, HandleTerminalCleanAsync);
    }

    private async Task<object?> HandleTerminalListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = AppServerParams.Get<TerminalListParams>(msg);
        var sessions = await service.ListAsync(p.ThreadId, ct);
        return new { terminals = sessions };
    }

    private async Task<object?> HandleTerminalReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = AppServerParams.Get<TerminalReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.ReadAsync(p.SessionId, p.WaitMs ?? 0, p.MaxOutputChars, ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalWriteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = AppServerParams.Get<TerminalWriteParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.WriteStdinAsync(
            p.SessionId,
            p.Input,
            p.YieldTimeMs ?? 1000,
            p.MaxOutputChars,
            ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalStopAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = AppServerParams.Get<TerminalStopParams>(msg);
        if (string.IsNullOrWhiteSpace(p.SessionId))
            throw AppServerErrors.InvalidParams("'sessionId' is required.");

        var terminal = await service.StopAsync(p.SessionId, ct);
        return new { terminal };
    }

    private async Task<object?> HandleTerminalCleanAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var service = RequireBackgroundTerminals();
        var p = AppServerParams.Get<TerminalCleanParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        await sessionService.CleanBackgroundTerminalsAsync(p.ThreadId, ct);
        var terminals = await service.ListAsync(p.ThreadId, ct);
        return new { terminals };
    }

    private IBackgroundTerminalService RequireBackgroundTerminals()
        => backgroundTerminalService ?? throw AppServerErrors.MethodNotFound("terminal/*");
}
