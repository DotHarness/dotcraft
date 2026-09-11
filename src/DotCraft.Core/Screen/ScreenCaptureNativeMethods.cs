using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DotCraft.Screen;

[SupportedOSPlatform("windows")]
internal static class ScreenCaptureNativeMethods
{
    public const uint SourceCopy = 0x00CC0020;

    private const int VirtualScreenX = 76;
    private const int VirtualScreenY = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private const uint DesktopReadObjects = 0x0001;
    private const nint PerMonitorAwareV2 = -4;

    public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ColorsUsed;
        public int ColorsImportant;
    }

    public static ScreenBounds VirtualScreen() => new(
        GetSystemMetrics(VirtualScreenX),
        GetSystemMetrics(VirtualScreenY),
        GetSystemMetrics(VirtualScreenWidth),
        GetSystemMetrics(VirtualScreenHeight));

    /// <summary>The input desktop is the one a signed-in person is looking at; without it there is nothing to read.</summary>
    public static bool HasInputDesktop()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == 0)
            return false;
        CloseDesktop(desktop);
        return true;
    }

    /// <summary>Physical-pixel coordinates must be chosen before any capture API locks in scaled ones.</summary>
    public static void EnsureDpiAware()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                return;
        }
        catch (EntryPointNotFoundException)
        {
        }
        SetProcessDPIAware();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint window, nint context);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint context);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(
        nint context,
        ref BitmapInfoHeader info,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint context, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint context);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(
        nint destination,
        int x,
        int y,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GdiFlush();
}
