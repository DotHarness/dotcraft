using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DotCraft.Satellite.ViewModels;

namespace DotCraft.Satellite.Tray;

internal interface ITrayIcon : IDisposable
{
    event EventHandler? MenuRequested;

    void SetState(string iconName, string tooltip);

    TrayMenuItem? ShowMenu(IReadOnlyList<TrayMenuItem> items);

    void ShowBalloon(string title, string body);
}

[SupportedOSPlatform("windows")]
internal sealed class TrayIconHost : ITrayIcon
{
    private const string WindowClassName = "DotCraftSatelliteTray";
    private const uint TrayIconId = 1;

    private readonly TrayNativeMethods.WindowProcedure _procedure;
    private readonly uint _taskbarCreated;
    private readonly string _assetDirectory;
    private readonly nint _window;
    private string _iconName = string.Empty;
    private string _tooltip = string.Empty;
    private nint _icon;
    private bool _added;
    private bool _disposed;

    public TrayIconHost(string assetDirectory)
    {
        _assetDirectory = assetDirectory;
        _procedure = WindowProc;
        _taskbarCreated = TrayNativeMethods.RegisterWindowMessageW("TaskbarCreated");
        var instance = TrayNativeMethods.GetModuleHandleW(null);
        var windowClass = new TrayNativeMethods.WindowClass
        {
            Size = (uint)Marshal.SizeOf<TrayNativeMethods.WindowClass>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = instance,
            ClassName = WindowClassName
        };
        TrayNativeMethods.RegisterClassExW(ref windowClass);
        _window = TrayNativeMethods.CreateWindowExW(
            0,
            WindowClassName,
            null,
            0,
            TrayNativeMethods.CW_USEDEFAULT,
            TrayNativeMethods.CW_USEDEFAULT,
            0,
            0,
            0,
            0,
            instance,
            0);
        if (_window == 0)
            throw new InvalidOperationException("The tray window could not be created.");
    }

    public event EventHandler? MenuRequested;

    public void SetState(string iconName, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _iconName = iconName;
        _tooltip = tooltip;
        Publish(_added ? TrayNativeMethods.NIM_MODIFY : TrayNativeMethods.NIM_ADD);
    }

    public TrayMenuItem? ShowMenu(IReadOnlyList<TrayMenuItem> items) => TrayPopupMenu.Show(_window, items);

    public void ShowBalloon(string title, string body)
    {
        if (_disposed || !_added)
            return;
        var data = NewData(TrayNativeMethods.NIF_INFO);
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(body, 255);
        data.InfoFlags = TrayNativeMethods.NIIF_INFO;
        TrayNativeMethods.Shell_NotifyIconW(TrayNativeMethods.NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_added)
        {
            var data = NewData(0);
            TrayNativeMethods.Shell_NotifyIconW(TrayNativeMethods.NIM_DELETE, ref data);
            _added = false;
        }
        ReleaseIcon();
        if (_window != 0)
            TrayNativeMethods.DestroyWindow(_window);
    }

    private void Publish(int message)
    {
        LoadIcon();
        var data = NewData(TrayNativeMethods.NIF_MESSAGE
                           | TrayNativeMethods.NIF_ICON
                           | TrayNativeMethods.NIF_TIP
                           | TrayNativeMethods.NIF_SHOWTIP);
        data.CallbackMessage = TrayNativeMethods.WM_TRAYICON;
        data.Icon = _icon;
        data.Tip = Truncate(_tooltip, 127);
        if (!TrayNativeMethods.Shell_NotifyIconW(message, ref data) && message == TrayNativeMethods.NIM_MODIFY)
        {
            _added = false;
            Publish(TrayNativeMethods.NIM_ADD);
            return;
        }

        _added = true;
        var version = NewData(0);
        version.VersionOrTimeout = TrayNativeMethods.NOTIFYICON_VERSION_4;
        TrayNativeMethods.Shell_NotifyIconW(TrayNativeMethods.NIM_SETVERSION, ref version);
    }

    private TrayNativeMethods.NotifyIconData NewData(int flags) => new()
    {
        Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(),
        Window = _window,
        Id = TrayIconId,
        Flags = (uint)flags,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private void LoadIcon()
    {
        ReleaseIcon();
        var dpi = TrayNativeMethods.GetDpiForWindow(_window);
        var side = (int)Math.Round(16 * (dpi == 0 ? 96 : dpi) / 96.0);
        _icon = TrayNativeMethods.LoadImageW(
            0,
            Path.Combine(_assetDirectory, _iconName + ".ico"),
            TrayNativeMethods.IMAGE_ICON,
            side,
            side,
            TrayNativeMethods.LR_LOADFROMFILE);
    }

    private void ReleaseIcon()
    {
        if (_icon == 0)
            return;
        TrayNativeMethods.DestroyIcon(_icon);
        _icon = 0;
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == _taskbarCreated && _iconName.Length > 0)
        {
            _added = false;
            Publish(TrayNativeMethods.NIM_ADD);
            return 0;
        }

        switch (message)
        {
            case TrayNativeMethods.WM_TRAYICON:
                var notification = (uint)(lParam & 0xFFFF);
                if (notification is TrayNativeMethods.WM_RBUTTONUP
                    or TrayNativeMethods.WM_LBUTTONUP
                    or TrayNativeMethods.WM_CONTEXTMENU)
                {
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                }
                return 0;
            case TrayNativeMethods.WM_DPICHANGED when _iconName.Length > 0:
                Publish(TrayNativeMethods.NIM_MODIFY);
                return 0;
            default:
                return TrayNativeMethods.DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
