using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class GifCodecTests
{
    [Theory]
    [InlineData("gif-small.gif")]
    [InlineData("gif-offset.gif")]
    [InlineData("gif-interlace.gif")]
    [InlineData("gif-dictionary.gif")]
    [InlineData("gif-animation.gif")]
    public void DecodeMatchesIndependentFirstFrame(string name)
    {
        var input = PngFixtures.Read(name);
        var decoded = GifCodec.Decode(input, new ImageBudget());
        CodecExpectations.Pixels(name, decoded);
        Assert.Equal(new ImageSize(decoded.Width, decoded.Height), GifCodec.Identify(input).Size);
    }

    [Fact]
    public void RejectsMissingPaletteAndTruncatedFrame()
    {
        var input = PngFixtures.Read("gif-small.gif");
        AssertError(input[..^4], ImageError.InvalidImage);
        input[10] &= 127;
        AssertError(input, ImageError.InvalidImage);
    }

    [Fact]
    public void RejectsFrameOutsideLogicalScreen()
    {
        var input = PngFixtures.Read("gif-offset.gif");
        input[6] = 2;
        AssertError(input, ImageError.InvalidImage);
    }

    [Fact]
    public void RejectsExcessLzwPixelsAndMissingEndCode()
    {
        var data = PngFixtures.Read("gif-dictionary.gif");
        var descriptor = 13 + 768;
        Assert.Equal(0x2c, data[descriptor]);
        data[descriptor + 5] = 1;
        data[descriptor + 6] = 0;
        AssertError(data, ImageError.InvalidImage);
        Assert.Equal(ImageError.InvalidImage, Assert.Throws<ImageCodecException>(() =>
            GifLzw.Decode([0x04], 2, 1, new ImageBudget())).Error);
    }

    [Fact]
    public void RejectsAllocationLimit()
    {
        Assert.Equal(ImageError.LimitExceeded, Assert.Throws<ImageCodecException>(() =>
            GifCodec.Decode(PngFixtures.Read("gif-small.gif"), new ImageBudget(10))).Error);
    }

    private static void AssertError(byte[] data, ImageError error) =>
        Assert.Equal(error, Assert.Throws<ImageCodecException>(() => GifCodec.Decode(data, new ImageBudget())).Error);
}
