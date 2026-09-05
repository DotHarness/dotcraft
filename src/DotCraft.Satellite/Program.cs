using DotCraft.Satellite.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace DotCraft.Satellite;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must see the install and update hooks before anything else runs.
        VelopackApp.Build()
            .OnFirstRun(_ => InstallShellIntegration())
            .OnBeforeUninstallFastCallback(_ => SatelliteUninstall.Run())
            .Run();

        var options = StartupOptions.Parse(args);
        if (options.Uninstall)
        {
            SatelliteUninstall.Run();
            return 0;
        }

        var gate = SingleInstanceGate.TryAcquire();
        if (gate is null)
        {
            SingleInstanceGate.TrySend(options.Url is { Length: > 0 } url
                ? InstanceMessage.Join(url)
                : InstanceMessage.Show());
            return 0;
        }

        App? app = null;
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                app = new App(options, gate);
            });
        }
        finally
        {
            app?.Dispose();
            gate.Dispose();
        }

        return 0;
    }

    private static void InstallShellIntegration()
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable)
            return;
        new ShellIntegration(new WindowsRegistryStore(), executable).Install();
    }
}
