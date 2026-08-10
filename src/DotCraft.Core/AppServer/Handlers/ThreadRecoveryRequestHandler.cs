using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class ThreadRecoveryRequestHandler(
    ISessionService sessionService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.ThreadRecoveryExport, HandleExportAsync);
        table.Map(Contract.AppServerRpc.ThreadRecoveryRestore, HandleRestoreAsync);
    }

    private async Task<AppServerTypedResult<Contract.ThreadRecoveryExportResult>> HandleExportAsync(
        AppServerTypedRequest<Contract.ThreadRecoveryExportParams> request,
        CancellationToken ct)
    {
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        try
        {
            var package = await sessionService.ExportThreadRecoveryAsync(threadId, ct);
            return AppServerTypedResult<Contract.ThreadRecoveryExportResult>.FromResult(new()
            {
                PackagePath = package.PackagePath,
                ThreadId = package.ThreadId,
                TerminalTurnId = package.TerminalTurnId,
                FormatVersion = package.FormatVersion,
                ByteLength = package.ByteLength,
                Sha256 = package.Sha256
            });
        }
        catch (KeyNotFoundException)
        {
            throw AppServerErrors.ThreadNotFound(threadId);
        }
        catch (InvalidOperationException)
        {
            throw AppServerErrors.TurnInProgress(threadId);
        }
        catch (ThreadRecoveryException ex)
        {
            throw AppServerErrors.ThreadRecovery(ex.Code, ex.Message);
        }
    }

    private async Task<AppServerTypedResult<Contract.ThreadRecoveryRestoreResult>> HandleRestoreAsync(
        AppServerTypedRequest<Contract.ThreadRecoveryRestoreParams> request,
        CancellationToken ct)
    {
        var packagePath = Require(request.Params.PackagePath, "'packagePath' is required.");
        var expectedThreadId = Require(request.Params.ExpectedThreadId, "'expectedThreadId' is required.");
        try
        {
            var threadId = await sessionService.RestoreThreadRecoveryAsync(packagePath, expectedThreadId, ct);
            return AppServerTypedResult<Contract.ThreadRecoveryRestoreResult>.FromResult(new()
            {
                ThreadId = threadId
            });
        }
        catch (ThreadRecoveryException ex)
        {
            throw AppServerErrors.ThreadRecovery(ex.Code, ex.Message);
        }
    }

    private static string Require(string? value, string detail)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams(detail);
        return value.Trim();
    }
}
