using DotCraft.RemoteTools;
using DotCraft.Satellite.Consent;
using DotCraft.Satellite.Island;
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
    private readonly SingleInstanceGate? _gate;
    private readonly SatelliteLog _log;
    private SatelliteRuntimeConnection? _connection;
    private TrayIconHost? _tray;
    private ToastPresenter? _toasts;
    private IslandViewModel? _island;
    private IslandWindow? _islandWindow;
    private Window? _consentPreview;

    internal App(StartupOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        _log = SatelliteLog.CreateDefault();
        DispatcherShutdownMode = ShutdownModeFor(options);
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    internal static DispatcherShutdownMode ShutdownModeFor(StartupOptions options) =>
        options.Preview is null
            ? DispatcherShutdownMode.OnExplicitShutdown
            : DispatcherShutdownMode.OnLastWindowClose;

    public void Dispose()
    {
        _log.Information("application.stopping", "The satellite application is stopping.");
        _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _islandWindow?.Dispose();
        _island?.Dispose();
        _toasts?.Dispose();
        _tray?.Dispose();
        GC.KeepAlive(_consentPreview);
        _consentPreview = null;
        UnhandledException -= OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _log.Information("application.started", "The satellite application started.");
        var strings = SatelliteStrings.Current;
        if (_options.PreviewConsent is { Length: > 0 } consentScenario)
        {
            _consentPreview = ConsentPreview.Show(consentScenario, strings);
            return;
        }

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var guard = new SatelliteCallbackGuard(_log);
        var dispatcher = new SatelliteDispatcher(dispatcherQueue, guard, _log);
        var approvals = new IslandApprovalQueue();
        _island = new IslandViewModel(strings, dispatcher, approvals);
        _islandWindow = new IslandWindow(_island, guard);

        if (_options.PreviewIsland is { Length: > 0 } scenario)
        {
            IslandPreview.Show(scenario, _island);
            return;
        }

        _connection = new SatelliteRuntimeConnection(
            RemoteToolHostRuntime.Create(new RemoteToolHostRuntimeOptions
            {
                ApprovalPresenter = new OwnerApprovalPresenter(dispatcherQueue, approvals)
            }),
            _log);
        var commands = new SatelliteCommands(_connection);
        _tray = new TrayIconHost(Path.Combine(AppContext.BaseDirectory, "Assets"));
        _toasts = new ToastPresenter(_tray);
        _toasts.Register();

        var viewModel = new TrayViewModel(
            _connection,
            commands,
            _tray,
            _toasts,
            strings,
            dispatcher);
        viewModel.Start();
        _island.Attach(_connection, commands);

        if (_gate is { } gate)
        {
            gate.MessageReceived += (_, message) => viewModel.HandleInstanceMessage(message);
            gate.StartListening();
        }

        if (_options.Url is { Length: > 0 } url)
            _ = dispatcher.ObserveAsync("tray.consent", () => viewModel.ShowConsentAsync(url));
        else if (!_options.Background)
            viewModel.HandleInstanceMessage(InstanceMessage.Show());
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) =>
        _log.Error("application.unhandled", "WinUI reported an unhandled exception.", args.Exception);
}
