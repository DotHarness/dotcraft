using System.Text.Json;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.Web;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace DotCraft.Satellite.Island;

/// <summary>
/// One display's edge glow: a transparent window over the whole display that takes no input, never
/// takes the foreground, and is invisible to screen capture.
/// </summary>
internal sealed class GlowWindow : Window, IDisposable
{
    private readonly SatelliteCallbackGuard _guard;
    private readonly Action _failed;
    private readonly nint _handle;
    private readonly AppWindow _appWindow;
    private readonly RectInt32 _display;
    private readonly bool _reduceMotion;
    private SatellitePage? _page;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private PointInt32? _origin;
    private bool _shown;

    public GlowWindow(RectInt32 display, bool reduceMotion, SatelliteCallbackGuard guard, Action failed)
    {
        _display = display;
        _reduceMotion = reduceMotion;
        _guard = guard;
        _failed = failed;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_handle));

        IslandNativeMethods.ApplyIslandStyles(_handle, activatable: false);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = false;
        IslandNativeMethods.RemoveFrame(_handle);
        IslandNativeMethods.MakeTransparent(_handle);
        IslandNativeMethods.MakeLayered(_handle);
        IslandNativeMethods.SetClickThrough(_handle, clickThrough: true);
        IslandNativeMethods.ExcludeFromCapture(_handle);
        SystemBackdrop = new TransparentBackdrop();
        Content = new Grid();

        _appWindow.Move(new PointInt32(-4000, -4000));
        Activate();
        _appWindow.Hide();
        _ = _guard.RunAsync("glow.load", LoadPageAsync, _failed);
    }

    public void Show(PointInt32 origin)
    {
        _origin = origin;
        if (!_shown)
        {
            _shown = true;
            _appWindow.MoveAndResize(_display);
            _appWindow.Show(activateWindow: false);
            if (_controller is { } controller)
                controller.IsVisible = true;
            IslandNativeMethods.RaiseToTop(_handle);
        }
        Push();
    }

    public void Hide()
    {
        if (!_shown)
            return;
        _shown = false;
        _appWindow.Hide();
        if (_controller is { } controller)
            controller.IsVisible = false;
    }

    public void Dispose()
    {
        _page?.Dispose();
        _page = null;
        _controller = null;
        _core = null;
        Close();
    }

    private async Task LoadPageAsync()
    {
        var page = await SatellitePageHost.AttachAsync(
            _handle, "DotCraft.Satellite.glow.html", transparent: true, OnWebMessage, _failed);
        _page = page;
        _controller = page.Controller;
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, _display.Width, _display.Height);
        _controller.IsVisible = _shown;
    }

    private void OnWebMessage(JsonElement message)
    {
        if (message.GetProperty("type").GetString() != "ready")
            return;
        DispatcherQueue.TryEnqueue(() => _guard.Run("glow.ready", () =>
        {
            _core = _controller?.CoreWebView2;
            Push();
        }, _failed));
    }

    private void Push()
    {
        if (_core is not { } core || _origin is not { } origin)
            return;
        var dpi = Tray.TrayNativeMethods.GetDpiForWindow(_handle);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var inside = origin.X >= _display.X && origin.X < _display.X + _display.Width
            && origin.Y >= _display.Y && origin.Y < _display.Y + _display.Height;
        SatellitePageHost.Post(core, new
        {
            reduceMotion = _reduceMotion,
            origin = inside ? new { x = (origin.X - _display.X) / scale, y = (origin.Y - _display.Y) / scale } : null
        });
    }
}
