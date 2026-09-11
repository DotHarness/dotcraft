using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static DotCraft.Screen.ScreenCaptureNativeMethods;

namespace DotCraft.Screen;

/// <summary>Copies the whole virtual desktop through GDI; protected surfaces and hardware overlays may appear black.</summary>
[SupportedOSPlatform("windows")]
internal sealed class GdiScreenCaptureSource : IScreenCaptureSource
{
    private const int DetailLimit = 200;

    private readonly object _gate = new();
    private readonly MemoryStream _encoded = new();
    private Surface? _surface;
    private byte[] _pixels = [];
    private bool _disposed;

    public GdiScreenCaptureSource() => EnsureDpiAware();

    public ScreenCaptureCapability Probe()
    {
        var bounds = VirtualScreen();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return new ScreenCaptureCapability(false, ScreenCaptureReasons.NoDisplayServer);
        return HasInputDesktop()
            ? new ScreenCaptureCapability(true, null)
            : new ScreenCaptureCapability(false, ScreenCaptureReasons.NoInteractiveSession);
    }

    public ScreenCaptureResult Capture(ScreenCaptureRequest request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var probe = Probe();
            if (!probe.Available)
                return ScreenCaptureResult.Unavailable(probe.UnavailableReason!);
            try
            {
                return ScreenCaptureResult.Captured(Encode(request, VirtualScreen()));
            }
            catch (Exception ex)
            {
                return ScreenCaptureResult.Unavailable(ScreenCaptureReasons.CaptureFailed, Detail(ex));
            }
        }
    }

    private static string Detail(Exception exception)
    {
        var text = (exception is CaptureFailedException
            ? exception.Message
            : $"{exception.GetType().Name}: {exception.Message}").ReplaceLineEndings(" ");
        return text.Length <= DetailLimit ? text : text[..DetailLimit];
    }

    private ScreenFrame Encode(ScreenCaptureRequest request, ScreenBounds bounds)
    {
        Copy(bounds);

        using var image = Image.LoadPixelData<Bgra32>(_pixels, bounds.Width, bounds.Height);
        var (width, height) = ScreenCaptureSource.Fit(bounds.Width, bounds.Height, request.MaxWidth);
        if (width != bounds.Width || height != bounds.Height)
            image.Mutate(context => context.Resize(width, height, KnownResamplers.Triangle));
        _encoded.SetLength(0);
        image.SaveAsJpeg(_encoded, new JpegEncoder
        {
            Quality = Math.Clamp(request.Quality, 1, 100),
            ColorType = JpegEncodingColor.YCbCrRatio420
        });
        return new ScreenFrame(width, height, _encoded.ToArray());
    }

    /// <summary>The screen context lives for one frame: a cached one stops working after a session switch.</summary>
    private void Copy(ScreenBounds bounds)
    {
        var screen = GetDC(0);
        if (screen == 0)
            throw new CaptureFailedException("The screen has no device context.");
        try
        {
            var surface = _surface;
            if (surface is null || surface.Width != bounds.Width || surface.Height != bounds.Height)
                surface = Reset(screen, bounds);
            if (!BitBlt(surface.Memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SourceCopy))
            {
                var first = Marshal.GetLastPInvokeError();
                surface = Reset(screen, bounds);
                if (!BitBlt(surface.Memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SourceCopy))
                    throw new CaptureFailedException(
                        $"BitBlt failed (Win32 {first}, retry {Marshal.GetLastPInvokeError()})");
            }
            GdiFlush();
            Marshal.Copy(surface.Bits, _pixels, 0, _pixels.Length);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    private Surface Reset(nint screen, ScreenBounds bounds)
    {
        _surface?.Dispose();
        _surface = null;
        var surface = Surface.Create(screen, bounds.Width, bounds.Height);
        _pixels = new byte[bounds.Width * bounds.Height * 4];
        _surface = surface;
        return surface;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _surface?.Dispose();
            _surface = null;
            _encoded.Dispose();
        }
    }

    private sealed class Surface(nint memory, nint bitmap, nint bits, int width, int height) : IDisposable
    {
        public nint Memory => memory;
        public nint Bits => bits;
        public int Width => width;
        public int Height => height;

        public static Surface Create(nint screen, int width, int height)
        {
            var memory = CreateCompatibleDC(screen);
            var info = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
            if (memory == 0 || bitmap == 0 || bits == 0)
            {
                if (bitmap != 0)
                    DeleteObject(bitmap);
                if (memory != 0)
                    DeleteDC(memory);
                throw new CaptureFailedException("The capture bitmap could not be created.");
            }
            SelectObject(memory, bitmap);
            return new Surface(memory, bitmap, bits, width, height);
        }

        public void Dispose()
        {
            DeleteDC(memory);
            DeleteObject(bitmap);
        }
    }

    private sealed class CaptureFailedException(string message) : Exception(message);
}
