using DotCraft.TraceViewer.ViewModels;
using DotCraft.TraceViewer.Analysis;
using Microsoft.UI.Xaml;

namespace DotCraft.TraceViewer;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; private set; } = null!;

    internal TraceViewerSettingsStore SettingsStore { get; private set; } = null!;

    internal nint WindowHandle => _window is null
        ? 0
        : WinRT.Interop.WindowNative.GetWindowHandle(_window);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var traceViewerSessionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            "trace-viewer",
            "sessions");
        SettingsStore = new TraceViewerSettingsStore();
        ViewModel = new MainViewModel(
            SettingsStore,
            new TraceAnalystService(),
            new TraceReviewStore(traceViewerSessionsPath));
        _window = new MainWindow();
        _window.Activate();
    }
}
