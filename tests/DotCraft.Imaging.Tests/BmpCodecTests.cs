using System.Buffers.Binary;
using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class BmpCodecTests
{
    public static IEnumerable<object[]> Inputs() => CodecExpectations.Names("bmp-")
        .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Inputs))]
    public void DecodeMatchesIndependentPixels(string name)
    {
        var bytes = PngFixtures.Read(name);
        var image = BmpCodec.Decode(bytes, new ImageBudget());
        CodecExpectations.Pixels(name, image);
        CodecExpectations.Metadata(name, image);
        Assert.Equal(new ImageSize(image.Width, image.Height), BmpCodec.Identify(bytes).Size);
    }

    [Fact]
    public void RejectsOverlappingMasksAndPaletteOverrun()
    {
        var bytes = PngFixtures.Read("bmp-fields16.bmp");
        bytes.AsSpan(54, 4).CopyTo(bytes.AsSpan(58));
        AssertError(bytes, ImageError.InvalidImage);
        bytes = PngFixtures.Read("bmp-palette4.bmp");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
        bytes[offset] = 0xf0;
        AssertError(bytes, ImageError.InvalidImage);
    }

    [Fact]
    public void RejectsTruncationRleOverrunAndInvalidHeight()
    {
        var bytes = PngFixtures.Read("bmp-rgb24-bottom.bmp");
        AssertError(bytes[..^1], ImageError.InvalidImage);
        bytes = PngFixtures.Read("bmp-rle8.bmp");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
        bytes[offset] = 255;
        AssertError(bytes, ImageError.InvalidImage);
        bytes = PngFixtures.Read("bmp-rgb24-bottom.bmp");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), int.MinValue);
        AssertError(bytes, ImageError.InvalidImage);
    }

    [Fact]
    public void RejectsUnsupportedModeAndAllocationLimit()
    {
        var bytes = PngFixtures.Read("bmp-rgb24-bottom.bmp");
        Assert.Equal(ImageError.LimitExceeded, Assert.Throws<ImageCodecException>(() =>
            BmpCodec.Decode(bytes, new ImageBudget(10))).Error);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(30), 4);
        AssertError(bytes, ImageError.UnsupportedFormat);
    }

    [Fact]
    public void DropsNonRgbProfileWithoutChangingPixels()
    {
        var bytes = PngFixtures.Read("bmp-header124.bmp");
        var profileOffset = 14 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(126));
        "CMYK"u8.CopyTo(bytes.AsSpan(profileOffset + 16));
        var image = BmpCodec.Decode(bytes, new ImageBudget());
        Assert.Null(image.IccProfile);
        CodecExpectations.Pixels("bmp-header124.bmp", image);
    }

    private static void AssertError(byte[] data, ImageError error) =>
        Assert.Equal(error, Assert.Throws<ImageCodecException>(() => BmpCodec.Decode(data, new ImageBudget())).Error);
}
