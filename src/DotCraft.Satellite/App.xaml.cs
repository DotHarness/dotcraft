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
    private Window? _lifetimeWindow;
    private SatelliteRuntimeConnection? _connection;
    private TrayIconHost? _tray;
    private ToastPresenter? _toasts;
    private OwnerApprovalPresenter? _approvals;

    internal App(StartupOptions options, SingleInstanceGate gate)
    {
        _options = options;
        _gate = gate;
        InitializeComponent();
    }

    public void Dispose()
    {
        _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _approvals?.Dispose();
        _toasts?.Dispose();
        _tray?.Dispose();
        GC.KeepAlive(_lifetimeWindow);
        _lifetimeWindow = null;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI exits when its last Window closes. Keep an unactivated window alive so closing
        // consent returns this tray application to the background instead of terminating it.
        _lifetimeWindow ??= new Window();

        var strings = SatelliteStrings.Current;
        _approvals = new OwnerApprovalPresenter(DispatcherQueue.GetForCurrentThread(), strings);
        _connection = new SatelliteRuntimeConnection(RemoteToolHostRuntime.Create(new RemoteToolHostRuntimeOptions
        {
            ApprovalPresenter = _approvals
        }));
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
