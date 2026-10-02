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

    public async ValueTask<byte[]> ReadImageAsync(RemoteToolRoute route, string callId, string path,
        CancellationToken cancellationToken = default)
    {
        using var operation = _operations.Enter(cancellationToken);
        cancellationToken = operation.Token;
        var lease = RequireLease(route);
        RemoteImageReadResponse result;
        try
        {
            result = await SendAsync<RemoteImageReadRequest, RemoteImageReadResponse>(
                lease.Session.Client, RemoteImageReadRequest.Method,
                new(route.LeaseId, route.WorkspaceId, callId, path),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw MapConnectionError(ex, callId, null);
        }

        try
        {
            return Convert.FromBase64String(result.ImageBase64);
        }
        catch (FormatException ex)
        {
            throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch,
                "The remote image read returned invalid image data.", callId, ex);
        }
    }
}
