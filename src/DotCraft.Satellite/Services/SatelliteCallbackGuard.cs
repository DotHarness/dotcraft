namespace DotCraft.Satellite.Services;

internal sealed class SatelliteCallbackGuard(SatelliteLog log)
{
    public void Run(string operation, Action action, Action? fallback = null)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            log.Error("ui.callback.failed", $"The {operation} callback failed.", exception);
            RunFallback(operation, fallback);
        }
    }

    public async Task RunAsync(string operation, Func<Task> action, Action? fallback = null)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            log.Error("ui.callback.failed", $"The {operation} callback failed.", exception);
            RunFallback(operation, fallback);
        }
    }

    private void RunFallback(string operation, Action? fallback)
    {
        if (fallback is null)
            return;
        try
        {
            fallback();
        }
        catch (Exception exception)
        {
            log.Error("ui.fallback.failed", $"The {operation} fallback failed.", exception);
        }
    }
}
