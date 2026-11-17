using System.Text.Json;
using DotCraft.Satellite.ViewModels;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.Web;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace DotCraft.Satellite.Island;

/// <summary>
/// Hosts the pill page in a transparent, always-on-top window fixed at the top centre of the primary
/// display. Clicks pass through everywhere but the pill.
/// </summary>
internal sealed class IslandWindow : Window, IDisposable
{
    private const int WidthDips = 600;
    private const int HeightDips = 300;
    private const int NearDips = 120;

    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(260);

    /// <summary>The pill stays for the life of the process, so the poll idles while the pointer is away.</summary>
    private static readonly TimeSpan PointerNear = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan PointerAway = TimeSpan.FromMilliseconds(96);

    private readonly IslandViewModel _viewModel;
    private readonly SatelliteCallbackGuard _guard;
    private readonly GlowOverlay _glow;
    private readonly Grid _root = new();
    private readonly DispatcherQueueTimer _pointer;
    private readonly DispatcherQueueTimer _exit;
    private readonly bool _animate;
    private readonly nint _handle;
    private readonly AppWindow _appWindow;
    private SatellitePage? _page;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private IslandStateModel _content = new();
    private RectInt32 _window;
    private PageBox? _pill;
    private bool _shown;
    private bool _unavailable;
    private bool _activatable;
    private bool _clickThrough;

    /// <summary>A box the page reports, in its own pixels from the window's top-left corner.</summary>
    private readonly record struct PageBox(double X, double Y, double Width, double Height);

    public IslandWindow(IslandViewModel viewModel, SatelliteCallbackGuard guard)
    {
        _viewModel = viewModel;
        _guard = guard;
        _glow = new GlowOverlay(guard);
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_handle));
        _animate = new UISettings().AnimationsEnabled;

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
        SystemBackdrop = new TransparentBackdrop();

        _root.ActualThemeChanged += (_, _) => Run("island.theme", Push);
        Content = _root;
        Activated += (_, args) => Run("island.activated", () => OnActivated(args));

        _pointer = DispatcherQueue.CreateTimer();
        _pointer.Interval = PointerNear;
        _pointer.Tick += (_, _) => Run("island.pointer", Tick);
        _exit = DispatcherQueue.CreateTimer();
        _exit.Interval = ExitDuration;
        _exit.IsRepeating = false;
        _exit.Tick += (_, _) => Run("island.exit", Hide);

        // Activating once builds the XAML content, which a window that is never focused otherwise
        // never does. The no-activate style keeps this off the foreground.
        _appWindow.Move(new PointInt32(-4000, -4000));
        Activate();
        _appWindow.Hide();
        _ = _guard.RunAsync("island.load", LoadPageAsync, DisableSurface);

        _viewModel.PropertyChanged += (_, _) => Run("island.apply", Apply);
        _viewModel.SurfaceFailed += DisableSurface;
        Apply();
    }

    public void Dispose()
    {
        _pointer.Stop();
        _exit.Stop();
        _glow.Dispose();
        _viewModel.SurfaceFailed -= DisableSurface;
        _page?.Dispose();
        _page = null;
        _controller = null;
        _core = null;
    }

    private double Scale => Tray.TrayNativeMethods.GetDpiForWindow(_handle) is var dpi && dpi > 0
        ? dpi / 96.0
        : 1.0;

    private string Theme => _root.ActualTheme == ElementTheme.Light ? "light" : "dark";

    private static string Mode(IslandStateModel state) => state.Mode switch
    {
        IslandMode.Approval => "approval",
        IslandMode.Running => "running",
        IslandMode.Standby => "standby",
        IslandMode.Paused => "paused",
        IslandMode.Offline => "offline",
        _ => "compact"
    };

    private async Task LoadPageAsync()
    {
        try
        {
            var page = await SatellitePageHost.AttachAsync(
                _handle, "DotCraft.Satellite.island.html", transparent: true,
                QueueWebMessage, DisableSurface);
            _page = page;
            var controller = page.Controller;
            controller.Bounds = ClientBounds();
            controller.IsVisible = _shown;
            _controller = controller;
        }
        catch (Exception)
        {
            DisableSurface();
        }
    }

    private Windows.Foundation.Rect ClientBounds() =>
        new(0, 0, WidthDips * Scale, HeightDips * Scale);

    private void QueueWebMessage(JsonElement message)
    {
        var ownedMessage = message.Clone();
        DispatcherQueue.TryEnqueue(() => Run("island.message", () => OnWebMessage(ownedMessage)));
    }

    private void OnWebMessage(JsonElement message)
    {
        switch (message.GetProperty("type").GetString())
        {
            case "ready":
                _core = _controller?.CoreWebView2;
                Push();
                break;
            case "bounds":
                _pill = ReadBox(message, "pill");
                Glow();
                break;
            case "press":
                Press();
                break;
            case "action":
                switch (message.GetProperty("name").GetString())
                {
                    case "allow":
                        _viewModel.Answer(allowed: true);
                        break;
                    case "deny":
                        _viewModel.Answer(allowed: false);
                        break;
                    case "pause":
                        _viewModel.Pause();
                        break;
                    case "resume":
                        _viewModel.Resume();
                        break;
                    case "disconnect" when Peer(message) is { } peer:
                        _viewModel.Disconnect(peer);
                        break;
                    case "openFolder" when Peer(message) is { } peer:
                        _viewModel.OpenFolder(peer);
                        break;
                    default:
                        break;
                }
                break;
            default:
                break;
        }
    }

    private static string? Peer(JsonElement message) =>
        message.TryGetProperty("peer", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static PageBox? ReadBox(JsonElement message, string name)
    {
        if (!message.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return new PageBox(
            value.GetProperty("x").GetDouble(),
            value.GetProperty("y").GetDouble(),
            value.GetProperty("width").GetDouble(),
            value.GetProperty("height").GetDouble());
    }

    private void Apply()
    {
        var state = _viewModel.State;
        if (_unavailable || !state.Visible)
        {
            Leave();
            return;
        }

        _content = state;
        if (!_shown)
            Enter();
        Push();
        Glow();
    }

    private void Enter()
    {
        _exit.Stop();
        _shown = true;
        var work = DisplayArea.Primary.WorkArea;
        var width = (int)Math.Round(WidthDips * Scale);
        var height = (int)Math.Round(HeightDips * Scale);
        _window = new RectInt32(work.X + ((work.Width - width) / 2), work.Y, width, height);
        _appWindow.MoveAndResize(_window);
        if (_controller is { } controller)
        {
            controller.Bounds = ClientBounds();
            controller.IsVisible = true;
        }
        _appWindow.Show(activateWindow: false);
        IslandNativeMethods.RaiseToTop(_handle);
        SetClickThrough(true);
        _pointer.Start();
    }

    private void Leave()
    {
        if (!_shown)
            return;
        _shown = false;
        _pill = null;
        _glow.Hide();
        SetActivatable(false);
        Push();
        _exit.Start();
    }

    private void Hide()
    {
        _pointer.Stop();
        _appWindow.Hide();
        if (_controller is { } controller)
            controller.IsVisible = false;
    }

    private void Glow()
    {
        if (!_shown || !_content.ShowRunning)
        {
            _glow.Hide();
            return;
        }
        _glow.Show(ToScreen(_pill ?? new PageBox((WidthDips / 2.0) - 1, 12, 2, 44)));
        IslandNativeMethods.RaiseToTop(_handle);
    }

    private void Push()
    {
        if (_core is not { } core)
            return;
        var state = _content;
        SatellitePageHost.Post(
            core,
            new
            {
                visible = _shown,
                theme = Theme,
                reduceMotion = !_animate,
                mode = Mode(state),
                summary = state.Summary,
                label = state.CompactLabel,
                since = state.SinceLabel,
                pause = state.PauseLabel,
                resume = state.ResumeLabel,
                expand = state.ExpandLabel,
                collapse = state.CollapseLabel,
                disconnect = state.DisconnectLabel,
                openFolder = state.OpenFolderLabel,
                watching = state.Watching,
                watchingLabel = state.WatchingLabel,
                peers = state.Peers.Select(peer => new
                {
                    id = peer.PeerId,
                    name = peer.Name,
                    meta = peer.Meta,
                    canOpenFolder = peer.CanOpenFolder
                }),
                running = state.ShowRunning
                    ? new
                    {
                        peerId = state.RunningPeerId,
                        operation = state.OperationLabel,
                        command = state.CommandPreview,
                        commandFull = state.CommandFull,
                        concurrency = state.Concurrency
                    }
                    : null,
                approval = state.IsApproval
                    ? new
                    {
                        peerId = state.ApprovalPeerId,
                        title = state.ApprovalTitle,
                        operation = state.ApprovalOperation,
                        target = state.ApprovalTarget,
                        targetFull = state.ApprovalTargetFull,
                        countdown = state.CountdownLabel,
                        fraction = state.CountdownFraction,
                        queued = state.Queued,
                        deny = state.DenyLabel,
                        allow = state.AllowLabel
                    }
                    : null
            });
    }

    /// <summary>The pointer drives mouse transparency, since a window it passes through hands the page no pointer events.</summary>
    private void Tick()
    {
        var cursor = Cursor();
        var onPill = Inside(cursor, _pill);
        SetClickThrough(!onPill);
        var pad = (int)Math.Round(NearDips * Scale);
        var near = onPill || (cursor.X >= _window.X - pad && cursor.X <= _window.X + _window.Width + pad
            && cursor.Y >= _window.Y - pad && cursor.Y <= _window.Y + _window.Height + pad);
        var cadence = near ? PointerNear : PointerAway;
        if (_pointer.Interval != cadence)
            _pointer.Interval = cadence;
    }

    private bool Inside(PointInt32 point, PageBox? box)
    {
        if (box is not { } value)
            return false;
        var rect = ToScreen(value);
        return point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
    }

    private RectInt32 ToScreen(PageBox box)
    {
        var scale = Scale;
        return new RectInt32(
            _window.X + (int)Math.Round(box.X * scale),
            _window.Y + (int)Math.Round(box.Y * scale),
            (int)Math.Round(box.Width * scale),
            (int)Math.Round(box.Height * scale));
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_clickThrough == clickThrough)
            return;
        _clickThrough = clickThrough;
        IslandNativeMethods.SetClickThrough(_handle, clickThrough);
    }

    /// <summary>Pressing the pill is the explicit gesture that lets the keyboard reach it.</summary>
    private void Press()
    {
        SetActivatable(true);
        Tray.TrayNativeMethods.SetForegroundWindow(_handle);
        if (_viewModel.State.IsApproval)
        {
            _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            _ = _core?.ExecuteScriptAsync("island.focusDecision()");
        }
    }

    private void OnActivated(WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            SetActivatable(false);
    }

    private void DisableSurface()
    {
        if (_unavailable)
            return;
        _unavailable = true;
        _controller = null;
        _core = null;
        _viewModel.Approvals.Disable();
        Leave();
    }

    private void Run(string operation, Action action) =>
        _guard.Run(operation, action, DisableSurface);

    private void SetActivatable(bool activatable)
    {
        if (_activatable == activatable)
            return;
        _activatable = activatable;
        IslandNativeMethods.ApplyIslandStyles(_handle, activatable);
    }

    private static PointInt32 Cursor()
    {
        Tray.TrayNativeMethods.GetCursorPos(out var point);
        return new PointInt32(point.X, point.Y);
    }
}
