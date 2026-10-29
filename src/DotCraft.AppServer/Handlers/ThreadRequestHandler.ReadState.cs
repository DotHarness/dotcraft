using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class ThreadRequestHandler
{
    private async Task<AppServerTypedResult<Contract.ThreadReadStateUpdateResult>> HandleThreadReadStateUpdateAsync(
        AppServerTypedRequest<Contract.ThreadReadStateUpdateParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var operation = p.Operation.Type switch
        {
            "read" => ThreadReadStateOperation.Read,
            "unread" => ThreadReadStateOperation.Unread,
            _ => throw AppServerErrors.InvalidParams("'operation.type' must be 'read' or 'unread'.")
        };
        try
        {
            var state = await sessionService.UpdateThreadReadStateAsync(p.ThreadId, p.ExpectedRevision, operation, ct);
            return AppServerTypedResult<Contract.ThreadReadStateUpdateResult>.FromResult(new()
            {
                ReadState = AppServerContractMapper.ToContract(state)
            });
        }
        catch (ThreadReadStateUnavailableException ex)
        {
            throw AppServerErrors.ThreadReadStateUnavailable(ex.ThreadId);
        }
        catch (ThreadReadStateConflictException ex)
        {
            throw AppServerErrors.ThreadReadStateConflict(AppServerContractMapper.ToContract(ex.Current));
        }
    }
}
