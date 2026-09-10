using System.Text.Json;
using DotCraft.Satellite.ViewModels;
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
/// Hosts the capsule page in a borderless, always-on-top, per-pixel transparent window that never
/// takes the foreground until the owner clicks it. Around the capsule the window is transparent to
/// the mouse, so clicks land on what is under it.
/// </summary>
internal sealed class IslandWindow : Window, IDisposable
{
    private const int MarginDips = 40;

    /// <summary>Room above the capsule for the entrance drop and the exit.</summary>
    private const int TopRoomDips = 160;

    private const int MaxWidthDips = 380;
    private const int CompactHeightDips = 34;
    private const int ApprovalHeightDips = 91;
    private const int PanelBaseDips = 101;
    private const int PanelRowDips = 48;
    private const int PanelRowsShown = 4;
    private const int WindowWidthDips = MaxWidthDips + (2 * MarginDips);
    private const int WindowHeightDips = TopRoomDips + PanelBaseDips + (PanelRowDips * PanelRowsShown) + MarginDips;
    private const int DragSlopPixels = 4;
    private const double CompactRadiusDips = 17;
    private const double PanelRadiusDips = 10;

    /// <summary>How far above its resting place the capsule drops from, as a share of its height.</summary>
    private const double EntranceRise = 1.4;

    private static readonly TimeSpan MorphDuration = TimeSpan.FromMilliseconds(480);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(200);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IslandViewModel _viewModel;
    private readonly WebView2 _web = new();
    private readonly DispatcherQueueTimer _pointer;
    private readonly DispatcherQueueTimer _exit;
    private readonly IslandNativeMethods.SubclassProc _erase;
    private readonly bool _animate;
    private readonly nint _handle;
    private readonly AppWindow _appWindow;
    private CoreWebView2? _core;
    private IslandStateModel _content = new();
    private RectInt32 _bounds;
    private RectInt32? _from;
    private double _radius = CompactRadiusDips;
    private double _fromRadius = CompactRadiusDips;
    private string _motion = "none";
    private DateTime _morphUntil;
    private PointInt32 _dragCursor;
    private PointInt32 _dragOrigin;
    private string _displayKey = string.Empty;
    private bool _placed;
    private bool _shown;
    private bool _dragging;
    private bool _moved;
    private bool _hovered;
    private bool _activatable;
    private bool _clickThrough;

    public IslandWindow(IslandViewModel viewModel)
    {
        _viewModel = viewModel;
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
        // The subclass must be in place before the frame changes, so the first erase is ours.
        _erase = Erase;
        IslandNativeMethods.Subclass(_handle, _erase);
        IslandNativeMethods.RemoveFrame(_handle);
        IslandNativeMethods.MakeTransparent(_handle);
        IslandNativeMethods.MakeLayered(_handle);
        SystemBackdrop = new TransparentBackdrop();

        _web.DefaultBackgroundColor = Colors.Transparent;
        _web.ActualThemeChanged += (_, _) => Push();
        Content = _web;
        Activated += OnActivated;

        _pointer = DispatcherQueue.CreateTimer();
        _pointer.Interval = TimeSpan.FromMilliseconds(16);
        _pointer.Tick += (_, _) => Tick();
        _exit = DispatcherQueue.CreateTimer();
        _exit.Interval = ExitDuration;
        _exit.IsRepeating = false;
        _exit.Tick += (_, _) => Hide();

        // Activating once builds the XAML content, which a window that is never focused otherwise
        // never does. The no-activate style keeps this off the foreground.
        _appWindow.Move(new PointInt32(-4000, -4000));
        Activate();
        _appWindow.Hide();
        _ = LoadPageAsync();

        _viewModel.PropertyChanged += (_, _) => Apply();
        Apply();
    }

    public void Dispose()
    {
        _pointer.Stop();
        _exit.Stop();
        _web.Close();
    }

    private double Scale => Tray.TrayNativeMethods.GetDpiForWindow(_handle) is var dpi && dpi > 0
        ? dpi / 96.0
        : 1.0;

    private RectInt32 WorkArea =>
        DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;

    private string Theme => _web.ActualTheme == ElementTheme.Light ? "light" : "dark";

    private static int HeightDips(IslandStateModel state) => state.Mode switch
    {
        IslandMode.Approval => ApprovalHeightDips,
        IslandMode.Expanded => PanelBaseDips + (PanelRowDips * Math.Min(state.Peers.Count, PanelRowsShown)),
        _ => CompactHeightDips
    };

    private static string Mode(IslandStateModel state) => state.Mode switch
    {
        IslandMode.Approval => "approval",
        IslandMode.Expanded => "expanded",
        IslandMode.Running => "running",
        _ => "compact"
    };

    private static string Page()
    {
        using var stream = typeof(IslandWindow).Assembly.GetManifestResourceStream("DotCraft.Satellite.island.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task LoadPageAsync()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotCraft", "Satellite", "WebView2");
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions());
        await _web.EnsureCoreWebView2Async(environment);
        _web.DefaultBackgroundColor = Colors.Transparent;
        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        core.WebMessageReceived += OnWebMessage;
        core.NavigateToString(Page());
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var document = JsonDocument.Parse(args.WebMessageAsJson);
        var message = document.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "ready":
                _core = sender;
                Push();
                break;
            case "press":
                BeginPress();
                break;
            case "release":
                EndPress();
                break;
            case "key":
                switch (message.GetProperty("key").GetString())
                {
                    case "Escape":
                        _viewModel.Collapse();
                        break;
                    case "Enter" or " ":
                        _viewModel.Toggle();
                        break;
                    default:
                        break;
                }
                break;
            case "action":
                var peer = message.TryGetProperty("peer", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
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
                    case "disconnect" when peer is not null:
                        _viewModel.Disconnect(peer);
                        break;
                    case "openFolder" when peer is not null:
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

    /// <summary>The window keeps a fixed size; the capsule's centre line and top edge decide where it sits.</summary>
    private RectInt32 WindowRect(RectInt32 capsule)
    {
        var scale = Scale;
        var width = (int)Math.Round(WindowWidthDips * scale);
        var height = (int)Math.Round(WindowHeightDips * scale);
        return new RectInt32(
            capsule.X + (capsule.Width / 2) - (width / 2),
            capsule.Y - (int)Math.Round(TopRoomDips * scale),
            width,
            height);
    }

    private void Apply()
    {
        var state = _viewModel.State;
        if (!state.Visible)
        {
            Leave();
            return;
        }

        _content = state;
        var scale = Scale;
        var target = Place((int)Math.Round(state.Width * scale), (int)Math.Round(HeightDips(state) * scale));
        var radius = state.IsPanelShape ? PanelRadiusDips : CompactRadiusDips;
        if (!_shown)
        {
            Enter(target, radius);
            return;
        }

        if (target != _bounds || radius != _radius)
        {
            _from = _bounds;
            _fromRadius = _radius;
            _bounds = target;
            _radius = radius;
            _motion = "spring";
            _morphUntil = DateTime.UtcNow + MorphDuration;
            _appWindow.MoveAndResize(WindowRect(_bounds));
        }
        Push();
    }

    private RectInt32 Place(int width, int height)
    {
        var work = WorkArea;
        var key = IslandPlacement.Key(work);
        if (!_placed || !string.Equals(key, _displayKey, StringComparison.Ordinal))
        {
            _displayKey = key;
            var origin = IslandPlacement.Read(key) ?? IslandPlacement.Default(work, width, Scale);
            _bounds = new RectInt32(origin.X, origin.Y, width, height);
            _placed = true;
        }

        var centre = _bounds.X + (_bounds.Width / 2);
        var x = Math.Clamp(centre - (width / 2), work.X, work.X + Math.Max(0, work.Width - width));
        var y = Math.Clamp(_bounds.Y, work.Y, work.Y + Math.Max(0, work.Height - height));
        return new RectInt32(x, y, width, height);
    }

    private void Enter(RectInt32 target, double radius)
    {
        _exit.Stop();
        _shown = true;
        _bounds = target;
        _radius = radius;
        _from = target with { Y = target.Y - Rise(target) };
        _fromRadius = radius;
        _motion = "enter";
        _appWindow.MoveAndResize(WindowRect(_bounds));
        Push();
        _appWindow.Show(activateWindow: false);
        IslandNativeMethods.RaiseToTop(_handle);
        _pointer.Start();
    }

    private void Leave()
    {
        if (!_shown)
            return;
        _shown = false;
        _dragging = false;
        SetActivatable(false);
        _from = null;
        _motion = "exit";
        Push();
        _exit.Start();
    }

    private void Hide()
    {
        _pointer.Stop();
        _appWindow.Hide();
        if (_hovered)
        {
            _hovered = false;
            _viewModel.PointerExited();
        }
    }

    private static int Rise(RectInt32 capsule) => (int)Math.Round(capsule.Height * EntranceRise);

    /// <summary>Hands the page everything it renders, with the geometry in the page's own pixels.</summary>
    private void Push()
    {
        if (_core is not { } core)
            return;
        var state = _content;
        var window = WindowRect(_bounds);
        var scale = Scale;
        var to = _motion == "exit" ? _bounds with { Y = _bounds.Y - Rise(_bounds) } : _bounds;
        core.PostWebMessageAsJson(JsonSerializer.Serialize(
            new
            {
                theme = Theme,
                reduceMotion = !_animate,
                motion = _motion,
                from = _from is { } from ? Box(from, _fromRadius) : null,
                to = Box(to, _radius),
                mode = Mode(state),
                summary = state.Summary,
                label = state.CompactLabel,
                since = state.SinceLabel,
                running = state.ShowRunning
                    ? new
                    {
                        operation = state.OperationLabel,
                        command = state.CommandPreview,
                        commandFull = state.CommandFull,
                        elapsed = state.ElapsedLabel,
                        concurrency = state.Concurrency
                    }
                    : null,
                panel = new
                {
                    title = state.PanelTitle,
                    count = state.PanelCount,
                    pause = state.PauseLabel,
                    peers = state.Peers.Select(peer => new
                    {
                        id = peer.PeerId,
                        name = peer.Name,
                        meta = peer.Meta,
                        canOpenFolder = peer.CanOpenFolder,
                        disconnect = peer.DisconnectLabel,
                        openFolder = peer.OpenFolderLabel
                    })
                },
                approval = state.IsApproval
                    ? new
                    {
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
            },
            JsonOptions));
        _motion = "none";
        _from = null;

        object Box(RectInt32 rect, double radius) => new
        {
            x = (rect.X - window.X) / scale,
            y = (rect.Y - window.Y) / scale,
            width = rect.Width / scale,
            height = rect.Height / scale,
            radius
        };
    }

    private nint Erase(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message != IslandNativeMethods.WM_ERASEBKGND)
            return IslandNativeMethods.DefSubclassProc(window, message, wParam, lParam);
        IslandNativeMethods.EraseToTransparent(window, wParam);
        return 1;
    }

    /// <summary>The pointer drives hover, the mouse-transparent margin, and the drag, since the page cannot see past its own edge.</summary>
    private void Tick()
    {
        var cursor = Cursor();
        if (_dragging)
            Drag(cursor);
        var inside = _dragging || InsideCapsule(cursor);
        SetClickThrough(!inside);
        if (inside == _hovered)
            return;
        _hovered = inside;
        if (inside)
            _viewModel.PointerEntered();
        else
            _viewModel.PointerExited();
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_clickThrough == clickThrough)
            return;
        _clickThrough = clickThrough;
        IslandNativeMethods.SetClickThrough(_handle, clickThrough);
    }

    private bool InsideCapsule(PointInt32 point) =>
        Inside(point, _bounds, _radius)
        || (_from is { } from && DateTime.UtcNow < _morphUntil && Inside(point, from, _fromRadius));

    private bool Inside(PointInt32 point, RectInt32 capsule, double radiusDips)
    {
        var hw = capsule.Width / 2.0;
        var hh = capsule.Height / 2.0;
        var radius = Math.Min(radiusDips * Scale, Math.Min(hw, hh));
        var dx = Math.Abs(point.X - (capsule.X + hw)) - hw + radius;
        var dy = Math.Abs(point.Y - (capsule.Y + hh)) - hh + radius;
        var ox = Math.Max(dx, 0);
        var oy = Math.Max(dy, 0);
        return Math.Sqrt((ox * ox) + (oy * oy)) + Math.Min(Math.Max(dx, dy), 0) - radius <= 0;
    }

    private void BeginPress()
    {
        _dragCursor = Cursor();
        _dragOrigin = new PointInt32(_bounds.X, _bounds.Y);
        _dragging = true;
        _moved = false;

        // Clicking is the explicit gesture that lets the keyboard reach the island.
        SetActivatable(true);
        Tray.TrayNativeMethods.SetForegroundWindow(_handle);
        if (_viewModel.State.IsApproval)
            FocusDecision();
    }

    private void Drag(PointInt32 cursor)
    {
        var dx = cursor.X - _dragCursor.X;
        var dy = cursor.Y - _dragCursor.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > DragSlopPixels)
            _moved = true;
        if (!_moved)
            return;
        _bounds = _bounds with { X = _dragOrigin.X + dx, Y = _dragOrigin.Y + dy };
        var window = WindowRect(_bounds);
        _appWindow.Move(new PointInt32(window.X, window.Y));
    }

    private void EndPress()
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (!_moved)
        {
            _viewModel.Toggle();
            return;
        }
        IslandPlacement.Write(_displayKey, new PointInt32(_bounds.X, _bounds.Y));
    }

    /// <summary>Moves focus to a decision the moment one is asked for, so Tab and Enter answer it.</summary>
    private void FocusDecision()
    {
        _web.Focus(FocusState.Programmatic);
        _ = _core?.ExecuteScriptAsync("island.focusDecision()");
    }

    /// <summary>A click elsewhere ends the island's turn with the keyboard and unpins the peer list.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            return;
        SetActivatable(false);
        _viewModel.Collapse();
    }

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
