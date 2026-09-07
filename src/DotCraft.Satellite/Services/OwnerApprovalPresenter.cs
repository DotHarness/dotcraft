using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using DotCraft.Satellite.Consent;

namespace DotCraft.Satellite.Services;

internal sealed class OwnerApprovalPresenter(DispatcherQueue dispatcher, SatelliteStrings strings)
    : IRemoteToolApprovalPresenter, IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);

    public void Dispose() => _queue.Dispose();

    public async Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken)
    {
        await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Window? window = null;
            using var registration = cancellationToken.Register(() =>
            {
                result.TrySetCanceled(cancellationToken);
                dispatcher.TryEnqueue(() => window?.Close());
            });
            if (!dispatcher.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                window = new OwnerApprovalWindow(request, strings, accepted =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                        result.TrySetResult(accepted);
                });
                window.Activate();
            }))
                return false;
            return await result.Task.ConfigureAwait(false);
        }
        finally { _queue.Release(); }
    }
}
