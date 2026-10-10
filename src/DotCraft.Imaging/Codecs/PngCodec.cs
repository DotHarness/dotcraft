using System.Buffers.Binary;
using System.IO.Compression;

namespace DotCraft.Imaging;

internal static partial class PngCodec
{
    private static ReadOnlySpan<int> PassX => [0, 4, 0, 2, 0, 1, 0];
    private static ReadOnlySpan<int> PassY => [0, 0, 4, 0, 2, 0, 1];
    private static ReadOnlySpan<int> PassDx => [8, 8, 4, 4, 2, 2, 1];
    private static ReadOnlySpan<int> PassDy => [8, 8, 8, 4, 4, 2, 2];

    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        var header = PngHeader.Read(data);
        return new(ImageFormat.Png, new(header.Width, header.Height));
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var header = PngHeader.Read(data);
        var image = DecodedImage.Create(header.Width, header.Height, budget);
        var chunks = ReadChunks(data, header, budget);
        try
        {
            var length = ScanlineLength(header);
            var scanlines = budget.Allocate<byte>(length);
            try
            {
                using var input = new MemoryStream(chunks.Compressed, false);
                using var workspace = new PngCompressionReservation(budget);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                zlib.ReadExactly(scanlines);
                if (zlib.ReadByte() != -1)
                    throw new ImageCodecException(ImageError.InvalidImage);
                PngStructure.ValidateZlib(chunks.Compressed, scanlines);
                ushort[]? pixels16 = header.Depth == 16 ? budget.Allocate<ushort>(image.Pixels.Length) : null;
                ReadPixels(scanlines, image, pixels16, header, chunks.Palette, chunks.Transparency);
                return image with { Pixels16 = pixels16, Exif = chunks.Exif, IccProfile = chunks.Profile };
            }
            catch (InvalidDataException) { throw new ImageCodecException(ImageError.InvalidImage); }
            catch (EndOfStreamException) { throw new ImageCodecException(ImageError.InvalidImage); }
            finally { budget.Release(scanlines.Length); }
        }
        finally
        {
            budget.Release(chunks.Compressed.Length);
            budget.Release(chunks.Palette?.Length ?? 0);
            budget.Release(chunks.Transparency?.Length ?? 0);
        }
    }

    private sealed record Chunks(byte[] Compressed, byte[]? Palette, byte[]? Transparency, byte[]? Exif, byte[]? Profile);

    private static Chunks ReadChunks(ReadOnlySpan<byte> data, PngHeader header, ImageBudget budget)
    {
        byte[]? palette = null, transparency = null, exif = null, profile = null;
        using var compressed = new BudgetMemoryStream(budget);
        var offset = 8;
        var seenData = false;
        var endedData = false;
        var seenEnd = false;
        var seenHeader = false;
        var seenTransparency = false;
        while (offset <= data.Length - 12)
        {
            var lengthValue = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (lengthValue > int.MaxValue || lengthValue > data.Length - offset - 12)
                throw new ImageCodecException(ImageError.InvalidImage);
            var length = (int)lengthValue;
            var type = data.Slice(offset + 4, 4);
            var payload = data.Slice(offset + 8, length);
            foreach (var c in type)
                if (c is not (>= 65 and <= 90) and not (>= 97 and <= 122))
                    throw new ImageCodecException(ImageError.InvalidImage);
            if ((type[2] & 32) != 0)
                throw new ImageCodecException(ImageError.InvalidImage);
            var validCrc = PngStructure.Crc(data.Slice(offset + 4, length + 4)) ==
                BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8 + length)..]);
            if (!validCrc && ((type[0] & 32) == 0 || type.SequenceEqual("tRNS"u8)))
                throw new ImageCodecException(ImageError.InvalidImage);
            if (!seenHeader && !type.SequenceEqual("IHDR"u8))
                throw new ImageCodecException(ImageError.InvalidImage);
            if (seenData && !type.SequenceEqual("IDAT"u8)) endedData = true;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (seenHeader || length != 13) throw new ImageCodecException(ImageError.InvalidImage);
                seenHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (palette is not null || seenData || seenTransparency || header.Color is 0 or 4 ||
                    length == 0 || length > 768 || length % 3 != 0 ||
                    (header.Color == 3 && length / 3 > 1 << header.Depth))
                    throw new ImageCodecException(ImageError.InvalidImage);
                palette = budget.Copy(payload);
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (seenTransparency || seenData || header.Color is 4 or 6 ||
                    (header.Color == 0 && length != 2) || (header.Color == 2 && length != 6) ||
                    (header.Color == 3 && (palette is null || length == 0 || length > palette.Length / 3)))
                    throw new ImageCodecException(ImageError.InvalidImage);
                transparency = budget.Copy(payload);
                seenTransparency = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (endedData || (header.Color == 3 && palette is null))
                    throw new ImageCodecException(ImageError.InvalidImage);
                compressed.Write(payload);
                seenData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!seenData || length != 0) throw new ImageCodecException(ImageError.InvalidImage);
                seenEnd = true;
                break;
            }
            else if ((type[0] & 32) == 0)
                throw new ImageCodecException(ImageError.UnsupportedFormat);
            else if (validCrc && type.SequenceEqual("eXIf"u8) && exif is null && PngStructure.IsExif(payload))
                exif = budget.Copy(payload);
            else if (validCrc && type.SequenceEqual("iCCP"u8) && profile is null)
                profile = PngStructure.ReadProfile(payload, budget);
            offset += length + 12;
        }
        if (!seenEnd || compressed.Length == 0) throw new ImageCodecException(ImageError.InvalidImage);
        return new(compressed.ToArray(), palette, transparency, exif, profile);
    }

    private static int ScanlineLength(PngHeader header)
    {
        long length = 0;
        for (var pass = 0; pass < (header.Interlaced ? 7 : 1); pass++)
        {
            var (width, height, _, _, _, _) = Pass(header, pass);
            if (width == 0 || height == 0) continue;
            var row = ((long)width * header.Channels * header.Depth + 7) / 8;
            length += (row + 1) * height;
        }
        if (length > int.MaxValue) throw new ImageCodecException(ImageError.LimitExceeded);
        return (int)length;
    }

    private static (int Width, int Height, int X, int Y, int Dx, int Dy) Pass(PngHeader header, int pass)
    {
        if (!header.Interlaced) return (header.Width, header.Height, 0, 0, 1, 1);
        var x = PassX[pass]; var y = PassY[pass]; var dx = PassDx[pass]; var dy = PassDy[pass];
        return ((int)Math.Max(0, ((long)header.Width - x + dx - 1) / dx),
            (int)Math.Max(0, ((long)header.Height - y + dy - 1) / dy), x, y, dx, dy);
    }

    private static void ReadPixels(byte[] data, DecodedImage image, ushort[]? pixels16,
        PngHeader header, byte[]? palette, byte[]? transparency)
    {
        var offset = 0;
        var bytesPerPixel = Math.Max(1, (header.Channels * header.Depth + 7) / 8);
        Span<ushort> channels = stackalloc ushort[4];
        for (var pass = 0; pass < (header.Interlaced ? 7 : 1); pass++)
        {
            var (width, height, x0, y0, dx, dy) = Pass(header, pass);
            if (width == 0 || height == 0) continue;
            var rowBytes = (int)(((long)width * header.Channels * header.Depth + 7) / 8);
            for (var y = 0; y < height; y++)
            {
                var filter = data[offset];
                var row = data.AsSpan(offset + 1, rowBytes);
                var previous = y == 0 ? ReadOnlySpan<byte>.Empty : data.AsSpan(offset - rowBytes, rowBytes);
                Unfilter(row, previous, filter, bytesPerPixel);
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < header.Channels; c++)
                        channels[c] = Sample(row, x * header.Channels + c, header.Depth);
                    var pixel = ((y0 + y * dy) * image.Width + x0 + x * dx) * 4;
                    WritePixel(image.Pixels.AsSpan(pixel, 4), pixels16 is null ? Span<ushort>.Empty :
                        pixels16.AsSpan(pixel, 4), channels, header, palette, transparency);
                }
                offset += rowBytes + 1;
            }
        }
    }

    private static ushort Sample(ReadOnlySpan<byte> row, int index, int depth) => depth switch
    {
        16 => BinaryPrimitives.ReadUInt16BigEndian(row[(index * 2)..]),
        8 => row[index],
        _ => (ushort)((row[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1))
    };

    private static void WritePixel(Span<byte> rgba, Span<ushort> rgba16, ReadOnlySpan<ushort> values,
        PngHeader h, byte[]? palette, byte[]? transparency)
    {
        var maximum = (1 << h.Depth) - 1;
        var r = (int)values[0]; var g = r; var b = r; var a = maximum;
        switch (h.Color)
        {
            case 0:
                if (transparency is not null && r == BinaryPrimitives.ReadUInt16BigEndian(transparency)) a = 0;
                break;
            case 2:
                g = values[1]; b = values[2];
                if (transparency is not null && r == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                    g == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                    b == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) a = 0;
                break;
            case 3:
                if (palette is null || r >= palette.Length / 3) throw new ImageCodecException(ImageError.InvalidImage);
                var index = r;
                r = palette[index * 3]; g = palette[index * 3 + 1]; b = palette[index * 3 + 2];
                a = transparency is not null && index < transparency.Length ? transparency[index] : 255;
                maximum = 255;
                break;
            case 4: a = values[1]; break;
            case 6: g = values[1]; b = values[2]; a = values[3]; break;
        }
        rgba[0] = (byte)((r * 255L + maximum / 2) / maximum);
        rgba[1] = (byte)((g * 255L + maximum / 2) / maximum);
        rgba[2] = (byte)((b * 255L + maximum / 2) / maximum);
        rgba[3] = (byte)((a * 255L + maximum / 2) / maximum);
        if (!rgba16.IsEmpty)
        {
            rgba16[0] = (ushort)r; rgba16[1] = (ushort)g;
            rgba16[2] = (ushort)b; rgba16[3] = (ushort)a;
        }
    }

    private static void Unfilter(Span<byte> row, ReadOnlySpan<byte> previous, int filter, int bpp)
    {
        if (filter > 4) throw new ImageCodecException(ImageError.InvalidImage);
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bpp ? row[i - bpp] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            var corner = i >= bpp && !previous.IsEmpty ? previous[i - bpp] : 0;
            row[i] = unchecked((byte)(row[i] + (filter switch
            {
                1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth(left, up, corner), _ => 0
            })));
        }
    }

    private static int Paeth(int left, int up, int corner)
    {
        var p = left + up - corner;
        var a = Math.Abs(p - left); var b = Math.Abs(p - up); var c = Math.Abs(p - corner);
        return a <= b && a <= c ? left : b <= c ? up : corner;
    }
}
