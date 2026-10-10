using System.Buffers.Binary;
using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class WebpCodecTests
{
    public static IEnumerable<object[]> LosslessFixtures()
    {
        foreach (var name in new[] { "palette", "binary", "gradient", "repeated", "solid", "tiles" })
            yield return [$"webp-{name}-6"];
        yield return ["webp-gradient-0"];
        yield return ["webp-offset-animation"];
        yield return ["webp-animation-blend-0"];
        yield return ["webp-animation-blend-2"];
        yield return ["webp-metadata"];
        yield return ["webp-metadata-prefix"];
    }

    public static IEnumerable<object[]> LossyFixtures()
    {
        foreach (var name in new[] { "small", "multi", "alpha" }) yield return [$"webp-lossy-{name}"];
        foreach (var i in new[] { 0, 11 }) yield return [$"webp-lossy-random-{i}"];
        for (var filtering = 0; filtering < 4; filtering++)
            yield return [$"webp-lossy-alpha-filter-{filtering}-{filtering % 2}"];
    }

    [Theory]
    [MemberData(nameof(LosslessFixtures))]
    public void IndependentLosslessFixturesDecodeExactPixels(string name)
    {
        var input = Read(name, "webp");
        var image = WebpCodec.Decode(input, new ImageBudget());
        CodecExpectations.Pixels(name + ".webp", image);
        var info = WebpCodec.Identify(input);
        Assert.Equal(new ImageSize(image.Width, image.Height), info.Size);
    }

    [Theory]
    [MemberData(nameof(LossyFixtures))]
    public void IndependentLossyFixturesDecodePixels(string name)
    {
        var input = Read(name, "webp");
        var image = WebpCodec.Decode(input, new ImageBudget());
        CodecExpectations.Pixels(name + ".webp", image, 1);
    }

    [Theory]
    [InlineData("webp-gradient-6")]
    [InlineData("webp-solid-6")]
    public void EncoderRetainsAllStoredChannels(string name)
    {
        var image = WebpCodec.Decode(Read(name, "webp"), new ImageBudget());
        var encoded = WebpCodec.Encode(image, new ImageBudget());
        var decoded = WebpCodec.Decode(encoded, new ImageBudget());
        CodecExpectations.Pixels(name + ".webp", decoded);
    }

    [Fact]
    public void TruncatedDataAndOversizedCanvasFailExplicitly()
    {
        var input = Read("webp-gradient-6", "webp");
        foreach (var length in new[] { 0, 11, input.Length / 2, input.Length - 1 })
        {
            var failure = Assert.Throws<ImageCodecException>(() => WebpCodec.Decode(input.AsSpan(0, length), new ImageBudget()));
            Assert.Equal(ImageError.InvalidImage, failure.Error);
        }
        var failureBudget = Assert.Throws<ImageCodecException>(() => WebpCodec.Decode(input, new ImageBudget(100)));
        Assert.Equal(ImageError.LimitExceeded, failureBudget.Error);
    }

    [Theory]
    [InlineData("webp-metadata")]
    [InlineData("webp-metadata-prefix")]
    public void MetadataSurvivesTranscodingWithoutChangingStoredOrientation(string name)
    {
        var source = Read(name, "webp");
        var image = WebpCodec.Decode(source, new ImageBudget());
        CodecExpectations.Metadata(name + ".webp", image);
        CodecExpectations.Pixels(name + ".webp", image);
        var encoded = WebpCodec.Encode(image, new ImageBudget());
        var decoded = WebpCodec.Decode(encoded, new ImageBudget());
        Assert.Equal(image.Exif, decoded.Exif);
        Assert.Equal(image.IccProfile, decoded.IccProfile);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData("webp-metadata", ImageFormat.Png)]
    [InlineData("webp-metadata", ImageFormat.Jpeg)]
    [InlineData("webp-metadata-prefix", ImageFormat.Png)]
    [InlineData("webp-metadata-prefix", ImageFormat.Jpeg)]
    public void CrossFormatTranscodingCarriesTiffExifAndRgbProfile(string name, ImageFormat format)
    {
        var result = ImageProcessor.Process(Read(name, "webp"), new ImageProcessingOptions(format));
        Assert.True(result.IsSuccess);
        var decoded = format == ImageFormat.Png
            ? PngCodec.Decode(result.Image.Data.Span, new ImageBudget())
            : JpegCodec.Decode(result.Image.Data.Span, new ImageBudget());
        CodecExpectations.Metadata(name + ".webp", decoded);
        Assert.Equal(new ImageSize(63, 29), result.Image.Size);
        if (format == ImageFormat.Png) CodecExpectations.Pixels(name + ".webp", decoded);
    }

    [Fact]
    public void PublicProcessingPreservesValidatedAnimationAndOwnsTheOutputBuffer()
    {
        var source = Read("webp-offset-animation", "webp");
        var expected = source.ToArray();
        var result = ImageProcessor.Process(source, new ImageProcessingOptions(ImageFormat.Webp));
        Assert.True(result.IsSuccess);
        source.AsSpan().Clear();
        Assert.Equal(expected, result.Image.Data.ToArray());
        Assert.Equal(new ImageSize(16, 12), result.Image.Size);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EmptyAndDuplicateAlphaChunksRejectBothStaticAndAnimationFrames(bool animated, bool duplicate)
    {
        var input = Read("webp-lossy-small", "webp");
        using var frame = new MemoryStream();
        if (duplicate) WriteChunk(frame, "ALPH"u8, new byte[64]);
        WriteChunk(frame, "ALPH"u8, []);
        frame.Write(input.AsSpan(12));
        using var body = new MemoryStream();
        body.Write("WEBP"u8);
        WriteChunk(body, "VP8X"u8, [(byte)(animated ? 18 : 16), 0, 0, 0, 6, 0, 0, 8, 0, 0]);
        if (animated)
        {
            WriteChunk(body, "ANIM"u8, new byte[6]);
            using var animationFrame = new MemoryStream();
            animationFrame.Write(new byte[] { 0, 0, 0, 0, 0, 0, 6, 0, 0, 8, 0, 0, 0, 0, 0, 2 });
            animationFrame.Write(frame.ToArray());
            WriteChunk(body, "ANMF"u8, animationFrame.ToArray());
        }
        else body.Write(frame.ToArray());
        using var file = new MemoryStream();
        file.Write("RIFF"u8);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)body.Length);
        file.Write(length);
        file.Write(body.ToArray());
        var result = ImageProcessor.Process(file.ToArray(), new ImageProcessingOptions(ImageFormat.Webp));
        Assert.Equal(ImageError.InvalidImage, result.Error);
    }

    [Fact]
    public void UndisplayedVp8KeyframeIsNotAnImage()
    {
        var input = Read("webp-lossy-small", "webp");
        input[20] &= 239;
        Assert.False(ImageProcessor.TryIdentify(input, out _));
        var result = ImageProcessor.Process(input, new ImageProcessingOptions(ImageFormat.Webp));
        Assert.Equal(ImageError.InvalidImage, result.Error);
    }

    [Fact]
    public void Vp8SegmentationWithoutFeatureUpdatesUsesAbsoluteDefaults()
    {
        AssertVp8Transcode(
            "UklGRiQAAABXRUJQVlA4IBcAAAAQAQCdASoQABAAIAAeAAADcAD+3qwAAAA=",
            [133]);
    }

    [Fact]
    public void Vp8FrameDisablesFilteringDespitePositiveReferenceAdjustment()
    {
        AssertVp8Transcode(
            "UklGRjYAAABXRUJQVlA4ICkAAABQAQCdASoQABAAAAd4ACgAAA3AAMT8iSJIkiSF02L6zpJ2P5C6bEgAAAA=",
            [140, 134, 127, 121]);
    }

    private static void AssertVp8Transcode(string encoded, ReadOnlySpan<byte> expectedRow)
    {
        var result = ImageProcessor.Process(Convert.FromBase64String(encoded), new(ImageFormat.Png));
        Assert.True(result.IsSuccess);
        Assert.Equal(new ImageSize(16, 16), result.Image.Size);
        var decoded = PngCodec.Decode(result.Image.Data.Span, new ImageBudget());
        for (var offset = 0; offset < decoded.Pixels.Length; offset += 4)
        {
            var expected = expectedRow[(offset / 4) % expectedRow.Length];
            for (var channel = 0; channel < 3; channel++)
                Assert.InRange(Math.Abs(decoded.Pixels[offset + channel] - expected), 0, 1);
            Assert.Equal(255, decoded.Pixels[offset + 3]);
        }
    }

    private static byte[] Read(string name, string extension) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{name}.{extension}"));

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> payload)
    {
        output.Write(tag);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
        output.Write(size);
        output.Write(payload);
        if ((payload.Length & 1) != 0) output.WriteByte(0);
    }
}
