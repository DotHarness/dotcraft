namespace DotCraft.Screen;

/// <summary>Samples the desktop on demand and reports an unavailable display as state rather than a fault.</summary>
public interface IScreenCaptureSource : IDisposable
{
    ScreenCaptureCapability Probe();

    ScreenCaptureResult Capture(ScreenCaptureRequest request);
}

/// <param name="MaxWidth">The widest frame wanted; a wider desktop scales down to fit.</param>
/// <param name="Quality">JPEG quality, 1-100.</param>
public sealed record ScreenCaptureRequest(int MaxWidth, int Quality);

public sealed record ScreenFrame(int Width, int Height, byte[] Jpeg);

/// <param name="Detail">A short, diagnostic-only account of an unavailable capture; never a path.</param>
public sealed record ScreenCaptureResult(ScreenFrame? Frame, string? UnavailableReason, string? Detail = null)
{
    public static ScreenCaptureResult Unavailable(string reason, string? detail = null) => new(null, reason, detail);

    public static ScreenCaptureResult Captured(ScreenFrame frame) => new(frame, null);
}

public sealed record ScreenCaptureCapability(bool Available, string? UnavailableReason);

public static class ScreenCaptureReasons
{
    public const string NoCaptureBackend = "noCaptureBackend";
    public const string NoInteractiveSession = "noInteractiveSession";
    public const string NoDisplayServer = "noDisplayServer";
    public const string CaptureFailed = "captureFailed";
}

public static class ScreenCaptureSource
{
    /// <summary>Returns the platform backend, or a source that answers every call with <see cref="ScreenCaptureReasons.NoCaptureBackend"/>.</summary>
    public static IScreenCaptureSource Create() =>
        OperatingSystem.IsWindows()
            ? new GdiScreenCaptureSource()
            : new UnavailableScreenCaptureSource(ScreenCaptureReasons.NoCaptureBackend);

    internal static (int Width, int Height) Fit(int width, int height, int maxWidth)
    {
        if (maxWidth <= 0 || width <= maxWidth)
            return (width, height);
        return (maxWidth, Math.Max(1, (int)Math.Round(height * (double)maxWidth / width)));
    }
}

internal sealed class UnavailableScreenCaptureSource(string reason) : IScreenCaptureSource
{
    public ScreenCaptureCapability Probe() => new(false, reason);

    public ScreenCaptureResult Capture(ScreenCaptureRequest request) => ScreenCaptureResult.Unavailable(reason);

    public void Dispose()
    {
    }
}
