using System.Buffers.Binary;
using Xunit;

namespace DotCraft.Imaging.Tests;

public sealed class PngCodecTests
{
    public static IEnumerable<object[]> Inputs() => CodecExpectations.Names("png-")
        .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Inputs))]
    public void DecodeMatchesIndependentPixels(string name)
    {
        var decoded = PngCodec.Decode(PngFixtures.Read(name), new ImageBudget());
        CodecExpectations.Pixels(name, decoded);
        CodecExpectations.Precision(name, decoded.Pixels16);
    }

    [Theory]
    [InlineData("png-metadata.png")]
    [InlineData("png-c6-d16-adam7-tiny.png")]
    public void EncoderProducesRgba8Pixels(string name)
    {
        var budget = new ImageBudget();
        var image = PngCodec.Decode(PngFixtures.Read(name), budget);
        var encoded = PngCodec.Encode(image, budget);
        Assert.Equal(8, encoded[24]);
        Assert.Equal(6, encoded[25]);
        var decoded = PngCodec.Decode(encoded, new ImageBudget());
        CodecExpectations.Pixels(name, decoded);
        CodecExpectations.Metadata(name, image);
        CodecExpectations.Metadata(name, decoded);
    }

    [Fact]
    public void RetainsSixteenBitPrecisionUntilResamplingFinishes()
    {
        var budget = new ImageBudget();
        var source = PngCodec.Decode(PngFixtures.Read("png-precision.png"), budget);
        var resized = ImageResampler.Resize(source, 1, 1, budget);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, resized.Pixels);
    }

    [Fact]
    public void BadOptionalMetadataDoesNotInvalidatePixels()
    {
        var bytes = PngFixtures.Read("png-metadata.png");
        var profile = PngFixtures.ChunkOffset(bytes, "iCCP"u8);
        bytes[profile + 8] ^= 1;
        var image = PngCodec.Decode(bytes, new ImageBudget());
        Assert.Null(image.IccProfile);
        CodecExpectations.Pixels("png-metadata.png", image);
    }

    [Fact]
    public void RejectsTruncationCrcAndCorruptChecksum()
    {
        var bytes = PngFixtures.Read("png-c6-d8-plain.png");
        var corrupt = (byte[])bytes.Clone();
        corrupt[29] ^= 1;
        AssertError(corrupt, ImageError.InvalidImage);
        AssertError(bytes[..^1], ImageError.InvalidImage);
        corrupt = (byte[])bytes.Clone();
        var idat = PngFixtures.ChunkOffset(corrupt, "IDAT"u8);
        var length = BinaryPrimitives.ReadInt32BigEndian(corrupt.AsSpan(idat));
        corrupt[idat + 8 + length - 1] ^= 1;
        PngFixtures.UpdateCrc(corrupt, idat);
        AssertError(corrupt, ImageError.InvalidImage);
    }

    [Fact]
    public void RejectsTruncatedZlibWithCompletePixels()
    {
        var bytes = PngFixtures.Read("png-c6-d8-plain.png");
        var offset = PngFixtures.ChunkOffset(bytes, "IDAT"u8);
        var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
        var truncated = new byte[bytes.Length - 4];
        bytes.AsSpan(0, offset + 8 + length - 4).CopyTo(truncated);
        bytes.AsSpan(offset + 8 + length, bytes.Length - offset - 8 - length).CopyTo(truncated.AsSpan(offset + 8 + length - 4));
        BinaryPrimitives.WriteInt32BigEndian(truncated.AsSpan(offset), length - 4);
        PngFixtures.UpdateCrc(truncated, offset);
        AssertError(truncated, ImageError.InvalidImage);
    }

    [Fact]
    public void AllocationAndDimensionBombsFailBeforeAllocation()
    {
        var data = PngFixtures.Read("png-c6-d8-plain.png");
        Assert.Equal(ImageError.LimitExceeded, Assert.Throws<ImageCodecException>(() =>
            PngCodec.Decode(data, new ImageBudget(100))).Error);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), int.MaxValue);
        PngFixtures.UpdateCrc(data, 8);
        AssertError(data, ImageError.LimitExceeded);
    }

    [Fact]
    public void UnknownCriticalChunksFailExplicitly()
    {
        var data = PngFixtures.Read("png-c6-d8-plain.png");
        var offset = PngFixtures.ChunkOffset(data, "IEND"u8);
        var output = new byte[data.Length + 12];
        data.AsSpan(0, offset).CopyTo(output);
        "ABCD"u8.CopyTo(output.AsSpan(offset + 4));
        data.AsSpan(offset).CopyTo(output.AsSpan(offset + 12));
        PngFixtures.UpdateCrc(output, offset);
        AssertError(output, ImageError.UnsupportedFormat);
    }

    private static void AssertError(byte[] data, ImageError error) =>
        Assert.Equal(error, Assert.Throws<ImageCodecException>(() => PngCodec.Decode(data, new ImageBudget())).Error);
}

internal static class PngFixtures
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Directory, name));

    public static int ChunkOffset(byte[] data, ReadOnlySpan<byte> type)
    {
        var offset = 8;
        while (offset <= data.Length - 12)
        {
            if (data.AsSpan(offset + 4, 4).SequenceEqual(type)) return offset;
            offset += 12 + BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
        }
        throw new InvalidOperationException("Fixture chunk was not found.");
    }

    public static void UpdateCrc(byte[] data, int offset)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
        uint crc = 0xffffffff;
        foreach (var value in data.AsSpan(offset + 4, length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 8 + length), ~crc);
    }
}
