using DotCraft.Screen;
using Xunit;

namespace DotCraft.Tests.Screen;

public sealed class ScreenCaptureSourceTests
{
    [Fact]
    public void Fit_KeepsAspect_AndNeverYieldsZero()
    {
        Assert.Equal((1920, 540), ScreenCaptureSource.Fit(3840, 1080, 1920));
        Assert.Equal((1024, 768), ScreenCaptureSource.Fit(1024, 768, 1920));
        Assert.Equal((640, 1), ScreenCaptureSource.Fit(7680, 1, 640));
    }

    [Fact]
    public void UnavailableSource_AnswersEveryCallWithItsReason()
    {
        using var source = new UnavailableScreenCaptureSource(ScreenCaptureReasons.NoCaptureBackend);

        Assert.Equal("noCaptureBackend", source.Probe().UnavailableReason);
        Assert.Equal("noCaptureBackend", source.Capture(new ScreenCaptureRequest(640, 80)).UnavailableReason);
    }
}
