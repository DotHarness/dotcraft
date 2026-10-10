using System.Buffers.Binary;
using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class ImageProcessorTests
{
    [Fact]
    public void Process_ValidatedBytesAreOwnedByResult()
    {
        var original = PngFixture();
        var expected = original.ToArray();
        var result = ImageProcessor.Process(original, new(ImageFormat.Png));
        Assert.True(result.IsSuccess);
        Assert.Equal(new ImageSize(1, 1), result.SourceSize);
        Assert.Equal(expected, result.Image.Data.ToArray());
        original.AsSpan().Clear();
        Assert.Equal(expected, result.Image.Data.ToArray());
    }

    [Fact]
    public void Identify_HeaderDoesNotReplaceCompleteValidation()
    {
        var original = PngFixture();
        var header = original.AsSpan(0, 33).ToArray();
        Assert.True(ImageProcessor.TryIdentify(header, out var info));
        Assert.Equal(new ImageSize(1, 1), info.Size);
        var result = ImageProcessor.Process(header, new(ImageFormat.Png));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Image);
        Assert.Null(result.SourceSize);
        Assert.Equal(ImageError.InvalidImage, result.Error);
    }

    [Fact]
    public void Process_UnknownContentReturnsDomainError()
    {
        Assert.False(ImageProcessor.TryIdentify("unknown"u8, out var info));
        Assert.Equal(default, info);
        var result = ImageProcessor.Process("unknown"u8, new(ImageFormat.Png));
        Assert.Equal(ImageError.UnsupportedFormat, result.Error);
    }

    [Fact]
    public void Process_InvalidOptionsThrow()
    {
        var bytes = PngFixture();
        Assert.Throws<ArgumentNullException>(() => ImageProcessor.Process(bytes, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.Process(bytes, new(ImageFormat.Gif)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.Process(bytes, new(ImageFormat.Png) { TargetSize = new(0, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.Process(bytes, new(ImageFormat.Png) { TargetSize = new(2, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.Process(bytes, new(ImageFormat.Jpeg) { JpegQuality = 0 }));
    }

    [Fact]
    public void Process_HugeDimensionsFailBeforePixelAllocation()
    {
        var bytes = PngFixture();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), 100_000);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), 100_000);
        var crc = Crc(bytes.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29), crc);
        var result = ImageProcessor.Process(bytes, new(ImageFormat.Png));
        Assert.Equal(ImageError.LimitExceeded, result.Error);
    }

    [Fact]
    public void EncodeBgraJpeg_ValidatesLayoutBeforeReadingPixels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.EncodeBgraJpeg(new byte[16], new(2, 2), 4, new()));
        Assert.Throws<ArgumentException>(() => ImageProcessor.EncodeBgraJpeg(new byte[15], new(2, 2), 8, new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageProcessor.EncodeBgraJpeg(new byte[16], new(2, 2), 8, new() { TargetSize = new(3, 2) }));
    }

    [Fact]
    public void EncodeBgraJpeg_ResizesWithoutChangingOrientationOrChannels()
    {
        var pixels = new byte[20 * 17];
        for (var y = 0; y < 17; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var offset = y * 20 + x * 4;
                pixels[offset] = (byte)(y < 8 ? 20 : 220);
                pixels[offset + 1] = 70;
                pixels[offset + 2] = (byte)(y < 8 ? 220 : 20);
            }
        }
        var result = ImageProcessor.EncodeBgraJpeg(pixels, new(3, 17), 20,
            new() { TargetSize = new(2, 11), Quality = 90, Subsampling = JpegSubsampling.Yuv420 });
        Assert.True(result.IsSuccess);
        Assert.Equal(new ImageSize(3, 17), result.SourceSize);
        Assert.Equal(new ImageSize(2, 11), result.Image.Size);
        var decoded = JpegCodec.Decode(result.Image.Data.Span, new());
        Assert.InRange(decoded.Pixels[0], 215, 225);
        Assert.InRange(decoded.Pixels[1], 65, 75);
        Assert.InRange(decoded.Pixels[2], 15, 25);
        var last = (decoded.Height - 1) * decoded.Width * 4;
        Assert.InRange(decoded.Pixels[last], 15, 25);
        Assert.InRange(decoded.Pixels[last + 2], 215, 225);
    }

    private static byte[] PngFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "IntegrationFixtures", "color-1.png"));

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = crc >> 1 ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
