using System.Buffers.Binary;
using System.IO.Compression;

namespace DotCraft.Imaging;

internal readonly record struct PngHeader(int Width, int Height, int Depth, int Color, int Channels, bool Interlaced)
{
    public static PngHeader Read(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 33 || !data[..8].SequenceEqual(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new ImageCodecException(ImageError.InvalidImage);
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new ImageCodecException(ImageError.InvalidImage);
        int depth = data[24], color = data[25];
        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var validDepth = color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false
        };
        if (!validDepth || data[26] != 0 || data[27] != 0 || data[28] > 1)
            throw new ImageCodecException(ImageError.InvalidImage);
        return new((int)width, (int)height, depth, color, channels, data[28] == 1);
    }
}

internal static class PngStructure
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
            crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    public static bool IsRgbProfile(ReadOnlySpan<byte> profile) =>
        profile.Length >= 20 && profile.Slice(16, 4).SequenceEqual("RGB "u8);

    public static bool IsExif(ReadOnlySpan<byte> data) => data.Length >= 8 &&
        (data[..4].SequenceEqual("II\x2a\0"u8) || data[..4].SequenceEqual("MM\0\x2a"u8));

    public static void ValidateZlib(ReadOnlySpan<byte> encoded, ReadOnlySpan<byte> decoded)
    {
        if (encoded.Length < 6 || (encoded[0] & 15) != 8 || encoded[0] >> 4 > 7 ||
            (encoded[0] * 256 + encoded[1]) % 31 != 0 || (encoded[1] & 32) != 0)
            throw new ImageCodecException(ImageError.InvalidImage);
        uint a = 1, b = 0;
        foreach (var value in decoded)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        if ((b << 16 | a) != BinaryPrimitives.ReadUInt32BigEndian(encoded[^4..]))
            throw new ImageCodecException(ImageError.InvalidImage);
    }

    public static byte[]? ReadProfile(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var separator = data.IndexOf((byte)0);
        if (separator is < 1 or > 79 || separator + 2 >= data.Length || data[separator + 1] != 0)
            return null;
        var compressed = budget.Copy(data[(separator + 2)..]);
        try
        {
            using var input = new MemoryStream(compressed, false);
            using var workspace = new PngCompressionReservation(budget);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new BudgetMemoryStream(budget);
            var scratch = budget.Allocate<byte>(4096);
            try
            {
                int read;
                while ((read = zlib.Read(scratch)) != 0)
                    output.Write(scratch.AsSpan(0, read));
                var written = output.GetBuffer().AsSpan(0, (int)output.Length);
                try { ValidateZlib(compressed, written); }
                catch (ImageCodecException) { return null; }
                if (!IsRgbProfile(written))
                    return null;
                return output.ToArray();
            }
            finally { budget.Release(scratch.Length); }
        }
        catch (InvalidDataException) { return null; }
        finally { budget.Release(compressed.Length); }
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
