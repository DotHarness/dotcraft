using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DotCraft.Satellite.Tray;

[SupportedOSPlatform("windows")]
internal static partial class TrayNativeMethods
{
    public const int WM_DESTROY = 0x0002;
    public const int WM_COMMAND = 0x0111;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_TRAYICON = 0x8000 + 1;

    public const int NIM_ADD = 0x0000;
    public const int NIM_MODIFY = 0x0001;
    public const int NIM_DELETE = 0x0002;
    public const int NIM_SETVERSION = 0x0004;

    public const int NIF_MESSAGE = 0x0001;
    public const int NIF_ICON = 0x0002;
    public const int NIF_TIP = 0x0004;
    public const int NIF_INFO = 0x0010;
    public const int NIF_SHOWTIP = 0x0080;

    public const int NOTIFYICON_VERSION_4 = 4;
    public const int NIIF_INFO = 0x0001;

    public const int IMAGE_ICON = 1;
    public const int LR_LOADFROMFILE = 0x0010;

    public const int MF_STRING = 0x0000;
    public const int MF_GRAYED = 0x0001;
    public const int MF_POPUP = 0x0010;
    public const int MF_SEPARATOR = 0x0800;

    public const int TPM_RIGHTBUTTON = 0x0002;
    public const int TPM_RETURNCMD = 0x0100;

    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint VersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    public delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadImageW(
        nint instance,
        string name,
        uint type,
        int cx,
        int cy,
        uint load);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenuW(nint menu, uint flags, nuint item, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandleW(string? moduleName);
}
