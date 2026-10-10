using System.Buffers.Binary;
using System.IO.Compression;

namespace DotCraft.Tests;

internal static class ImageFixture
{
    public static byte[] Read(string name)
    {
        using var stream = typeof(ImageFixture).Assembly.GetManifestResourceStream("DotCraft.Tests.ImageFixtures." + name)
            ?? throw new InvalidOperationException($"Image fixture {name} is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    public static byte[] Red(string mediaType, int width = 1, int height = 1) =>
        width == 1 && height == 1 ? Read(
            "red-1." + (mediaType switch
            {
                "image/jpeg" => "jpg",
                "image/webp" => "webp",
                "image/bmp" => "bmp",
                "image/gif" => "gif",
                _ => "png"
            })) : SolidPng(width, height);

    private static byte[] SolidPng(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[width * 3 + 1];
            for (var x = 0; x < width; x++) row[x * 3 + 1] = 255;
            for (var y = 0; y < height; y++) zlib.Write(row);
        }
        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = crc >> 1 ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    public static (int Width, int Height) PngSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidOperationException("Expected a PNG image.");
        return (BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)));
    }
}
