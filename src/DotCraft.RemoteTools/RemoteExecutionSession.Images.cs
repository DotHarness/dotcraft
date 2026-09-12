using DotCraft.Tools;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    public async ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId,
        byte[] bytes, CancellationToken cancellationToken = default)
    {
        using var operation = _operations.Enter(cancellationToken);
        cancellationToken = operation.Token;
        var lease = RequireLease(route);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await SendAsync<RemoteImageWriteRequest, RemoteImageWriteResponse>(
                lease.Session.Client, RemoteImageWriteRequest.Method,
                new(route.LeaseId, route.WorkspaceId, threadId, callId, Convert.ToBase64String(bytes)),
                cancellationToken).ConfigureAwait(false);
            return result.SavedPath;
        }
        catch (RemoteToolHostException) { throw; }
        catch (Exception ex)
        {
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteOutcomeUnknown,
                "The remote image write outcome is unknown; it was not retried.", callId, ex);
        }
    }
}
