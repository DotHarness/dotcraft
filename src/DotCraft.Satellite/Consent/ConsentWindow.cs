using System.Text.Json;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.ViewModels;
using DotCraft.Satellite.Web;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.UI.ViewManagement;

namespace DotCraft.Satellite.Consent;

internal sealed class ConsentWindow : Window
{
    private const int WidthDips = 660;
    private const int HeightDips = 500;

    private readonly ConsentViewModel _viewModel;
    private readonly Grid _root = new();
    private readonly AppWindow _appWindow;
    private readonly nint _handle;
    private readonly bool _animate;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;

    public ConsentWindow(ConsentViewModel viewModel)
    {
        _viewModel = viewModel;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_handle));
        _animate = new UISettings().AnimationsEnabled;
        Title = viewModel.WindowTitle;
        viewModel.Finished += (_, _) => Close();
        viewModel.PropertyChanged += (_, _) => Push();

        _root.ActualThemeChanged += (_, _) => Push();
        _root.Loaded += OnRootLoaded;
        Content = _root;
        Closed += (_, _) => Detach();
        _appWindow.Changed += OnWindowChanged;
        Resize();
    }

    private void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        _root.Loaded -= OnRootLoaded;
        _ = AttachAsync();
    }

    private async Task AttachAsync()
    {
        try
        {
            var controller = await SatellitePageHost.AttachAsync(
                _handle, "DotCraft.Satellite.consent.html", transparent: false, OnWebMessage);
            _controller = controller;
            Layout();
            controller.IsVisible = true;
        }
        catch (Exception)
        {
            ShowRuntimeMissing();
        }
    }

    private void ShowRuntimeMissing() => Content = new StackPanel
    {
        Padding = new Thickness(24),
        Spacing = 16,
        Children =
        {
            new TextBlock
            {
                Text = SatelliteStrings.Current["consent.webviewMissing"],
                TextWrapping = TextWrapping.Wrap
            },
            new Button
            {
                Content = _viewModel.DeclineText,
                Command = _viewModel.DeclineCommand,
                HorizontalAlignment = HorizontalAlignment.Right
            }
        }
    };

    private void OnWebMessage(JsonElement message)
    {
        var type = message.GetProperty("type").GetString();
        var mode = message.TryGetProperty("mode", out var value) ? value.GetString() : null;

        // The folder picker must not open inside a WebView2 callback, so every message is answered
        // on the next dispatcher turn.
        DispatcherQueue.TryEnqueue(() => Handle(type, mode));
    }

    private void Handle(string? type, string? mode)
    {
        switch (type)
        {
            case "ready":
                _core = _controller?.CoreWebView2;
                Push();
                FocusDecline();
                break;
            case "select" when mode == "full":
                _viewModel.SelectFullAccessCommand.Execute(null);
                break;
            case "select" when mode == "folder":
                _viewModel.SelectWorkspaceModeCommand.Execute(null);
                break;
            case "changeFolder":
                _viewModel.ChangeFolderCommand.Execute(null);
                break;
            case "allow":
                _viewModel.AllowCommand.Execute(null);
                break;
            case "decline":
                _viewModel.DeclineCommand.Execute(null);
                break;
        }
    }

    private void Push()
    {
        if (_core is not { } core)
            return;
        SatellitePageHost.Post(core, ConsentPageState.From(
            _viewModel,
            _root.ActualTheme == ElementTheme.Light ? "light" : "dark",
            SatelliteStrings.Current.Locale,
            reduceMotion: !_animate));
    }

    /// <summary>Decline holds the focus, so a keystroke that lands before the reader does nothing.</summary>
    private void FocusDecline()
    {
        _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        _ = _core?.ExecuteScriptAsync("consent.focusDecline()");
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
            Layout();
    }

    private void Layout()
    {
        if (_controller is not { } controller)
            return;
        var size = _appWindow.ClientSize;
        controller.Bounds = new Windows.Foundation.Rect(0, 0, size.Width, size.Height);
    }

    private void Detach()
    {
        _appWindow.Changed -= OnWindowChanged;
        _core = null;
        _controller?.Close();
        _controller = null;
    }

    private void Resize()
    {
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "satellite.ico"));
        var scale = RasterizationScale();
        var width = (int)Math.Round(WidthDips * scale);
        var height = (int)Math.Round(HeightDips * scale);
        var work = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }
    }

    private double RasterizationScale()
    {
        var dpi = Tray.TrayNativeMethods.GetDpiForWindow(_handle);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
