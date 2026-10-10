using System.Buffers.Binary;
using DotCraft.Imaging;
using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class JpegCodecTests
{
    [Theory]
    [InlineData("baseline-0")]
    [InlineData("baseline-1")]
    [InlineData("baseline-2")]
    [InlineData("progressive-2")]
    [InlineData("baseline-restart-0")]
    [InlineData("progressive-restart-2")]
    [InlineData("gray")]
    [InlineData("gray-progressive")]
    [InlineData("cmyk")]
    [InlineData("cmyk-progressive")]
    [InlineData("metadata")]
    [InlineData("rgb")]
    [InlineData("ycck")]
    public void DecodeMatchesIndependentPixels(string name)
    {
        var encoded = Fixture(name, "jpg");
        var decoded = JpegCodec.Decode(encoded, new());
        var identified = JpegCodec.Identify(encoded);
        Assert.Equal(new(decoded.Width, decoded.Height), identified.Size);
        CodecExpectations.Pixels($"jpeg-{name}.jpg", decoded, 4);
    }

    [Fact]
    public void NonAdobeCmykUsesUninvertedComponents()
    {
        var decoded = JpegCodec.Decode(Fixture("cmyk-plain", "jpg"), new());
        Assert.Equal(new byte[] { 180, 164, 134, 255 }, decoded.Pixels);
    }

    [Fact]
    public void DecodeCopiesExifAndRgbIcc()
    {
        var image = JpegCodec.Decode(Fixture("metadata", "jpg"), new());
        CodecExpectations.Metadata("jpeg-metadata.jpg", image);
        var encoded = JpegCodec.Encode(image, 85, JpegSubsampling.Yuv444, new());
        var processed = JpegCodec.Decode(encoded, new());
        Assert.Equal(image.Exif, processed.Exif);
        Assert.Equal(image.IccProfile, processed.IccProfile);
    }

    [Fact]
    public void NonRgbIccIsIgnored()
    {
        var input = Fixture("metadata", "jpg");
        var signature = input.AsSpan().IndexOf("RGB "u8);
        Assert.True(signature >= 0);
        "CMYK"u8.CopyTo(input.AsSpan(signature));
        Assert.Null(JpegCodec.Decode(input, new()).IccProfile);
    }

    [Theory]
    [InlineData(1, JpegSubsampling.Yuv444)]
    [InlineData(85, JpegSubsampling.Yuv444)]
    [InlineData(100, JpegSubsampling.Yuv444)]
    [InlineData(1, JpegSubsampling.Yuv420)]
    [InlineData(85, JpegSubsampling.Yuv420)]
    [InlineData(100, JpegSubsampling.Yuv420)]
    public void EncodePreservesOddDimensionsAndSampling(int quality, JpegSubsampling sampling)
    {
        var image = JpegCodec.Decode(Fixture("metadata", "jpg"), new());
        var encoded = JpegCodec.Encode(image, quality, sampling, new());
        Assert.Equal(new(19, 13), JpegCodec.Identify(encoded).Size);
        var frame = Segment(encoded, 0xC0);
        Assert.Equal(sampling == JpegSubsampling.Yuv420 ? 0x22 : 0x11, frame[7]);
        Assert.Equal(0x11, frame[10]);
        var decoded = JpegCodec.Decode(encoded, new());
        Assert.Equal(image.Pixels.Length, decoded.Pixels.Length);
    }

    [Theory]
    [InlineData("baseline-0")]
    [InlineData("progressive-2")]
    [InlineData("baseline-restart-0")]
    public void TruncatedInputIsRejected(string name)
    {
        var data = Fixture(name, "jpg");
        foreach (var length in new[] { 0, 1, 9, data.Length / 2, data.Length - 1 })
        {
            var exception = Assert.Throws<ImageCodecException>(() => JpegCodec.Decode(data.AsSpan(0, length), new()));
            Assert.Equal(ImageError.InvalidImage, exception.Error);
        }
    }

    [Fact]
    public void UnsupportedFrameCodingIsExplicit()
    {
        var data = Fixture("baseline-0", "jpg");
        var position = data.AsSpan().IndexOf(new byte[] { 255, 192 });
        data[position + 1] = 195;
        Assert.Equal(ImageError.UnsupportedFormat, Assert.Throws<ImageCodecException>(() => JpegCodec.Decode(data, new())).Error);
    }

    [Fact]
    public void InvalidRestartSequenceIsRejected()
    {
        var data = Fixture("baseline-restart-0", "jpg");
        var position = data.AsSpan().IndexOf(new byte[] { 255, 208 });
        Assert.True(position >= 0);
        data[position + 1] = 209;
        Assert.Equal(ImageError.InvalidImage, Assert.Throws<ImageCodecException>(() => JpegCodec.Decode(data, new())).Error);
    }

    [Fact]
    public void DeclaredDimensionsRespectBudgetBeforeAllocation()
    {
        var data = Fixture("baseline-0", "jpg");
        var position = data.AsSpan().IndexOf(new byte[] { 255, 192 });
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position + 5), 65535);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position + 7), 65535);
        Assert.Equal(ImageError.LimitExceeded, Assert.Throws<ImageCodecException>(() => JpegCodec.Decode(data, new())).Error);
    }

    [Theory]
    [InlineData(1, 1, JpegSubsampling.Yuv444)]
    [InlineData(1, 1, JpegSubsampling.Yuv420)]
    [InlineData(1, 17, JpegSubsampling.Yuv420)]
    [InlineData(17, 1, JpegSubsampling.Yuv420)]
    [InlineData(9, 17, JpegSubsampling.Yuv420)]
    public void EncoderExtendsPartialEdgeBlocks(int width, int height, JpegSubsampling sampling)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 230;
            pixels[i + 1] = 40;
            pixels[i + 2] = 100;
            pixels[i + 3] = 255;
        }
        var decoded = JpegCodec.Decode(JpegCodec.Encode(new(width, height, pixels), 100, sampling, new()), new());
        Assert.Equal(pixels.Length, decoded.Pixels.Length);
        Assert.True(pixels.Zip(decoded.Pixels, (a, b) => Math.Abs(a - b)).Max() <= 2);
    }

    internal static byte[] Fixture(string name, string extension) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"jpeg-{name}.{extension}"));

    private static byte[] Segment(byte[] data, int wanted)
    {
        var position = 2;
        while (position < data.Length)
        {
            var marker = JpegMarkers.Read(data, ref position);
            var segment = JpegMarkers.Segment(data, ref position);
            if (marker == wanted)
                return segment.ToArray();
        }
        throw new InvalidOperationException();
    }
}
