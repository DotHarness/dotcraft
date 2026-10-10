using System.Buffers.Binary;
using System.IO.Compression;

namespace DotCraft.Imaging;

internal static partial class PngCodec
{
    public static byte[] Encode(DecodedImage image, ImageBudget budget)
    {
        using var output = new BudgetMemoryStream(budget);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        output.Write(signature);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header, budget);
        if (image.Exif is { Length: > 0 }) WriteChunk(output, "eXIf"u8, image.Exif, budget);
        if (image.IccProfile is not null && PngStructure.IsRgbProfile(image.IccProfile))
        {
            using var profile = new BudgetMemoryStream(budget);
            profile.Write("DotCraft\0\0"u8);
            using var workspace = new PngCompressionReservation(budget);
            using (var zlib = new ZLibStream(profile, CompressionLevel.Optimal, true)) zlib.Write(image.IccProfile);
            WriteChunk(output, "iCCP"u8, profile.GetBuffer().AsSpan(0, (int)profile.Length), budget);
        }
        using (var compressed = new BudgetMemoryStream(budget))
        {
            using var workspace = new PngCompressionReservation(budget);
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            {
                var rowBytes = ImageBudget.BufferLength(image.Width, 1, 4);
                for (var y = 0; y < image.Height; y++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(image.Pixels.AsSpan(y * rowBytes, rowBytes));
                }
            }
            WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length), budget);
        }
        WriteChunk(output, "IEND"u8, [], budget);
        return output.ToArray();
    }

    private static void WriteChunk(BudgetMemoryStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, ImageBudget budget)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var crcInput = budget.Allocate<byte>(checked(data.Length + 4));
        try
        {
            type.CopyTo(crcInput);
            data.CopyTo(crcInput.AsSpan(4));
            output.Write(crcInput);
            BinaryPrimitives.WriteUInt32BigEndian(length, PngStructure.Crc(crcInput));
            output.Write(length);
        }
        finally { budget.Release(crcInput.Length); }
    }
}
