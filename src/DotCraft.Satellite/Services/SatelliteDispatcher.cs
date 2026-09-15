using Microsoft.UI.Dispatching;

namespace DotCraft.Satellite.Services;

internal sealed class SatelliteDispatcher(
    DispatcherQueue queue,
    SatelliteCallbackGuard guard,
    SatelliteLog log)
{
    public DispatcherQueue Queue => queue;

    public void Post(string operation, Action action, Action? fallback = null)
    {
        if (!queue.TryEnqueue(() => guard.Run(operation, action, fallback)))
            log.Warning("ui.dispatch.rejected", $"The {operation} callback could not be dispatched.");
    }

    public void PostAsync(string operation, Func<Task> action, Action? fallback = null)
    {
        if (!queue.TryEnqueue(async () => await guard.RunAsync(operation, action, fallback)))
            log.Warning("ui.dispatch.rejected", $"The {operation} callback could not be dispatched.");
    }

    public Task ObserveAsync(string operation, Func<Task> action, Action? fallback = null) =>
        guard.RunAsync(operation, action, fallback);
}
