using DotCraft.RemoteTools;
using DotCraft.Satellite.ViewModels;
using Microsoft.UI.Dispatching;

namespace DotCraft.Satellite.Services;

/// <summary>
/// Puts an owner request on the island and waits for the answer there. A request the island cannot
/// receive is denied rather than left to block the tool call.
/// </summary>
internal sealed class OwnerApprovalPresenter(DispatcherQueue dispatcher, IslandApprovalQueue queue)
    : IRemoteToolApprovalPresenter
{
    public async Task<bool> RequestAsync(
        RemoteToolApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var entry = new IslandApprovalEntry(request, DateTimeOffset.Now);
        if (!dispatcher.TryEnqueue(() => queue.Add(entry)))
            return false;
        try
        {
            return await entry.Decision.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            dispatcher.TryEnqueue(() => queue.Remove(entry));
        }
    }
}
