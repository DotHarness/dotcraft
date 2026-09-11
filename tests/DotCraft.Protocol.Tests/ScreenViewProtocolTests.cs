using DotCraft.Protocol.ScreenView;
using Xunit;

namespace DotCraft.Protocol.Tests;

public sealed class ScreenViewProtocolTests
{
    [Fact]
    public void FrameHeader_RoundTrips()
    {
        var header = new ScreenViewFrameHeader(uint.MaxValue, 3840, 2160, 1_757_500_000_123);
        var buffer = new byte[ScreenViewProtocol.FrameHeaderBytes];

        ScreenViewProtocol.WriteFrameHeader(buffer, header);

        Assert.True(ScreenViewProtocol.TryReadFrameHeader(buffer, out var read));
        Assert.Equal(header, read);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(ushort.MaxValue, ushort.MaxValue)]
    public void TryReadFrameHeader_RejectsSizesThatDescribeNoDesktop(int width, int height)
    {
        var buffer = new byte[ScreenViewProtocol.FrameHeaderBytes];
        ScreenViewProtocol.WriteFrameHeader(buffer, new ScreenViewFrameHeader(1, (ushort)width, (ushort)height, 0));

        Assert.False(ScreenViewProtocol.TryReadFrameHeader(buffer, out _));
    }

    [Fact]
    public void TryReadFrameHeader_RejectsAShortBuffer()
    {
        Assert.False(ScreenViewProtocol.TryReadFrameHeader(new byte[ScreenViewProtocol.FrameHeaderBytes - 1], out _));
    }

    [Fact]
    public void Clamp_BoundsEveryField_AndFloorsWatchersAtZero()
    {
        Assert.Equal(
            new ScreenViewControl(0, 1, 3840, 90),
            ScreenViewProtocol.Clamp(new ScreenViewControl(-3, 0, 10_000, 100)));
        Assert.Equal(
            new ScreenViewControl(2, 15, 320, 20),
            ScreenViewProtocol.Clamp(new ScreenViewControl(2, 99, 1, 0)));
    }
}
