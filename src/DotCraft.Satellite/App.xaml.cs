using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.Tray;
using DotCraft.Satellite.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DotCraft.Satellite;

public sealed partial class App : Application, IDisposable
{
    private readonly StartupOptions _options;
    private readonly SingleInstanceGate _gate;
    private SatelliteRuntimeConnection? _connection;
    private TrayIconHost? _tray;
    private ToastPresenter? _toasts;

    internal App(StartupOptions options, SingleInstanceGate gate)
    {
        _options = options;
        _gate = gate;
        InitializeComponent();
    }

    public void Dispose()
    {
        _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _toasts?.Dispose();
        _tray?.Dispose();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var strings = SatelliteStrings.Current;
        _connection = new SatelliteRuntimeConnection(RemoteToolHostRuntime.Create());
        _tray = new TrayIconHost(Path.Combine(AppContext.BaseDirectory, "Assets"));
        _toasts = new ToastPresenter(_tray);
        _toasts.Register();

        var viewModel = new TrayViewModel(
            _connection,
            _tray,
            _toasts,
            strings,
            DispatcherQueue.GetForCurrentThread());
        viewModel.Start();

        _gate.MessageReceived += (_, message) => viewModel.HandleInstanceMessage(message);
        _gate.StartListening();

        if (_options.Url is { Length: > 0 } url)
            _ = viewModel.ShowConsentAsync(url);
        else if (!_options.Background)
            viewModel.HandleInstanceMessage(InstanceMessage.Show());
    }
}
