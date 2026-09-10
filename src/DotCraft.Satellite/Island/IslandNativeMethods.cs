using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DotCraft.Satellite.Island;

/// <summary>The window traits WinUI does not reach: no taskbar presence, no foreground steal, a see-through body and a mouse-transparent margin.</summary>
[SupportedOSPlatform("windows")]
internal static class IslandNativeMethods
{
    public const uint WM_ERASEBKGND = 0x0014;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const int BLACK_BRUSH = 4;

    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    public delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

    private const uint DWM_BB_ENABLE = 0x1;
    private const uint DWM_BB_BLURREGION = 0x2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint Region;
        public int TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint window, ref BlurBehind blurBehind);

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_FRAME = 0x0400;
    private const uint RDW_ALLCHILDREN = 0x0080;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint rect, nint region, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint deviceContext, ref Rect rect, nint brush);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    /// <summary>Keeps the island out of the taskbar and Alt-Tab, and off the activation path.</summary>
    public static void ApplyIslandStyles(nint window, bool activatable)
    {
        var style = GetWindowLongPtr(window, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW;
        style = activatable ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        SetWindowLongPtr(window, GWL_EXSTYLE, style);
    }

    /// <summary>Drops the caption frame the presenter leaves behind, which would otherwise paint a rectangle around the transparent client area.</summary>
    public static void RemoveFrame(nint window)
    {
        SetWindowLongPtr(window, GWL_STYLE, GetWindowLongPtr(window, GWL_STYLE) & ~WS_CAPTION);
        SetWindowPos(
            window,
            nint.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Blur-behind with an empty region blurs nothing, but makes the desktop window manager
    /// composite the window with its alpha, so everything the content leaves clear is see-through
    /// instead of an opaque black sheet. The system's rounded corners and one-pixel border go with
    /// it, since a transparent window has no edge to draw them on.
    /// </summary>
    public static void MakeTransparent(nint window)
    {
        var square = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(uint));
        var none = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(window, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
        var region = CreateRectRgn(-2, -2, -1, -1);
        var blur = new BlurBehind { Flags = DWM_BB_ENABLE | DWM_BB_BLURREGION, Enable = 1, Region = region };
        _ = DwmEnableBlurBehindWindow(window, ref blur);
        DeleteObject(region);
        RedrawWindow(window, nint.Zero, nint.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
    }

    /// <summary>
    /// Erases the client area to premultiplied black, which is alpha zero; the window class's own
    /// white brush would leave a haze wherever nothing is drawn on top.
    /// </summary>
    public static void EraseToTransparent(nint window, nint deviceContext)
    {
        if (GetClientRect(window, out var rect))
            _ = FillRect(deviceContext, ref rect, GetStockObject(BLACK_BRUSH));
    }

    /// <summary>The caller keeps <paramref name="proc"/> alive for as long as the window exists.</summary>
    public static void Subclass(nint window, SubclassProc proc) => SetWindowSubclass(window, proc, 1, 0);

    /// <summary>
    /// Only a layered window can be transparent to the mouse; a fully opaque layer changes nothing
    /// else about how it is drawn.
    /// </summary>
    public static void MakeLayered(nint window)
    {
        SetWindowLongPtr(window, GWL_EXSTYLE, GetWindowLongPtr(window, GWL_EXSTYLE) | WS_EX_LAYERED);
        SetLayeredWindowAttributes(window, 0, byte.MaxValue, LWA_ALPHA);
    }

    /// <summary>While set, every click lands on whatever is under the window instead.</summary>
    public static void SetClickThrough(nint window, bool clickThrough)
    {
        var style = GetWindowLongPtr(window, GWL_EXSTYLE);
        var next = clickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (next != style)
            SetWindowLongPtr(window, GWL_EXSTYLE, next);
    }

    public static void RaiseToTop(nint window) => SetWindowPos(
        window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
