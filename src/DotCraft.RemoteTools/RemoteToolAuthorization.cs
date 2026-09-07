using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

/// <summary>Owner-selected authorization modes persisted with each pairing.</summary>
public static class RemoteToolAuthorization
{
    public const string WorkspacePreferred = "workspacePreferred";
    public const string FullAccess = "fullAccess";

    /// <summary>Whether a stored mode represents an explicit owner decision.</summary>
    public static bool IsValid(string? mode) => mode is WorkspacePreferred or FullAccess;
}

/// <summary>A local owner decision bound to one authenticated invocation.</summary>
public sealed record RemoteToolApprovalRequest(
    string PeerId, string InviterName, long AuthorizationRevision, string InvocationId,
    string Kind, string Operation, string Target, string WorkspacePath);

/// <summary>Presents a one-time request on the machine executing the tools.</summary>
public interface IRemoteToolApprovalPresenter
{
    /// <summary>Returns false on dismissal; must honor cancellation without executing a decision later.</summary>
    Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken);
}

internal sealed class HostInvocationApprovalService : IApprovalService
{
    private static readonly AsyncLocal<Invocation?> Current = new();

    public static IDisposable Begin(Invocation invocation)
    {
        var previous = Current.Value;
        Current.Value = invocation;
        return new Scope(previous);
    }

    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
        RequestAsync("file", operation, path);
    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) =>
        RequestAsync("shell", "execute", command);
    public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
        RequestAsync(kind, operation, target);

    private static Task<bool> RequestAsync(string kind, string operation, string target) =>
        Current.Value?.RequestAsync(kind, operation, target) ?? Task.FromResult(false);

    internal sealed class Invocation(
        RemoteToolHubPeer peer, string invocationId, string workspacePath,
        IRemoteToolApprovalPresenter? presenter, CancellationToken cancellationToken)
    {
        private bool _toolApproved;
        private readonly HashSet<(string, string, string)> _approved = [];

        public async Task<bool> RequestAsync(string kind, string operation, string target, bool force = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_toolApproved) return true;
            if (!force && peer.AuthorizationMode == RemoteToolAuthorization.FullAccess)
                return true;
            if (_approved.Contains((kind, operation, target)))
                return true;
            if (presenter is null)
                throw Denied(RemoteToolErrorCodes.ApprovalDeclined, "No local owner approval interface is available.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            bool accepted;
            try
            {
                accepted = await presenter.RequestAsync(new RemoteToolApprovalRequest(
                    peer.PeerId, peer.HubLabel, peer.AuthorizationRevision, invocationId,
                    kind, operation, target, workspacePath), timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Denied(RemoteToolErrorCodes.ApprovalTimedOut, "The machine owner did not answer within two minutes.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
                throw Denied(RemoteToolErrorCodes.ApprovalDeclined, "The machine owner declined this operation. Do not retry without a new owner instruction.");
            _approved.Add((kind, operation, target));
            if (force) _toolApproved = true;
            return true;
        }

        private RemoteToolHostException Denied(string code, string message) => new(code, message, invocationId);
    }

    private sealed class Scope(Invocation? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
