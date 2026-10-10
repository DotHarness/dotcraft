using System.Buffers.Binary;
using System.Numerics;

namespace DotCraft.Imaging;

internal static class BmpCodec
{
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        var header = BmpHeader.Read(data);
        return new(ImageFormat.Bmp, new(header.Width, header.Height));
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var header = BmpHeader.Read(data);
        var fileSize = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
        if (fileSize != 0)
        {
            if (fileSize > data.Length || fileSize < header.PixelOffset)
                throw new ImageCodecException(ImageError.InvalidImage);
            data = data[..(int)fileSize];
        }
        var image = DecodedImage.Create(header.Width, header.Height, budget);
        var paletteOffset = 14 + header.HeaderSize;
        uint red = header.Depth == 16 ? 0x7c00u : 0xff0000u;
        uint green = header.Depth == 16 ? 0x3e0u : 0xff00u;
        uint blue = header.Depth == 16 ? 0x1fu : 0xffu;
        uint alpha = 0;
        if (header.Compression is 3 or 6)
        {
            var maskOffset = header.HeaderSize >= 52 ? 54 : paletteOffset;
            var count = header.HeaderSize >= 56 || header.Compression == 6 ? 4 : 3;
            if (maskOffset > data.Length - count * 4 || maskOffset + count * 4 > header.PixelOffset)
                throw new ImageCodecException(ImageError.InvalidImage);
            red = BinaryPrimitives.ReadUInt32LittleEndian(data[maskOffset..]);
            green = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 4)..]);
            blue = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 8)..]);
            if (count == 4) alpha = BinaryPrimitives.ReadUInt32LittleEndian(data[(maskOffset + 12)..]);
            if (header.HeaderSize == 40) paletteOffset += count * 4;
            ValidateMasks(red, green, blue, alpha, header.Depth);
        }
        byte[]? palette = null;
        try
        {
            if (header.Depth <= 8)
            {
                var count = header.ColorCount == 0 ? 1 << header.Depth : header.ColorCount;
                var entrySize = header.HeaderSize == 12 ? 3 : 4;
                if (count > 1 << header.Depth || paletteOffset > data.Length - count * entrySize ||
                    paletteOffset + count * entrySize > header.PixelOffset)
                    throw new ImageCodecException(ImageError.InvalidImage);
                palette = budget.Allocate<byte>(count * 3);
                for (var i = 0; i < count; i++)
                {
                    palette[i * 3] = data[paletteOffset + i * entrySize + 2];
                    palette[i * 3 + 1] = data[paletteOffset + i * entrySize + 1];
                    palette[i * 3 + 2] = data[paletteOffset + i * entrySize];
                }
            }
            if (header.Compression is 1 or 2)
                BmpRle.Decode(data[header.PixelOffset..], image, palette!, header.Depth);
            else
                ReadRows(data, image, header, palette, red, green, blue, alpha);
            return image with { IccProfile = ReadProfile(data, header, budget) };
        }
        finally { budget.Release(palette?.Length ?? 0); }
    }

    private static void ReadRows(ReadOnlySpan<byte> data, DecodedImage image, BmpHeader header,
        byte[]? palette, uint red, uint green, uint blue, uint alpha)
    {
        var stride = ((long)header.Width * header.Depth + 31) / 32 * 4;
        if (stride * header.Height > data.Length - header.PixelOffset)
            throw new ImageCodecException(ImageError.InvalidImage);
        for (var y = 0; y < header.Height; y++)
        {
            var row = data.Slice((int)(header.PixelOffset + y * stride), (int)stride);
            var destinationY = header.TopDown ? y : header.Height - 1 - y;
            for (var x = 0; x < header.Width; x++)
            {
                var pixel = image.Pixels.AsSpan((destinationY * image.Width + x) * 4, 4);
                if (header.Depth <= 8)
                {
                    var index = header.Depth switch
                    {
                        8 => row[x], 4 => (row[x / 2] >> (x % 2 == 0 ? 4 : 0)) & 15,
                        _ => (row[x / 8] >> (7 - x % 8)) & 1
                    };
                    WritePalette(pixel, index, palette!);
                }
                else if (header.Depth == 24)
                {
                    pixel[0] = row[x * 3 + 2]; pixel[1] = row[x * 3 + 1];
                    pixel[2] = row[x * 3]; pixel[3] = 255;
                }
                else
                {
                    var value = header.Depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]) :
                        BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                    pixel[0] = Expand(value, red); pixel[1] = Expand(value, green);
                    pixel[2] = Expand(value, blue); pixel[3] = alpha == 0 ? (byte)255 : Expand(value, alpha);
                }
            }
        }
    }

    internal static void WritePalette(Span<byte> pixel, int index, byte[] palette)
    {
        if (index >= palette.Length / 3) throw new ImageCodecException(ImageError.InvalidImage);
        palette.AsSpan(index * 3, 3).CopyTo(pixel);
        pixel[3] = 255;
    }

    private static byte Expand(uint value, uint mask)
    {
        var shift = BitOperations.TrailingZeroCount(mask);
        var maximum = mask >> shift;
        return (byte)(((ulong)((value & mask) >> shift) * 255 + maximum / 2) / maximum);
    }

    private static void ValidateMasks(uint red, uint green, uint blue, uint alpha, int depth)
    {
        if (red == 0 || green == 0 || blue == 0 || (red & green) != 0 || (red & blue) != 0 ||
            (green & blue) != 0 || (alpha & (red | green | blue)) != 0 ||
            (depth == 16 && (red | green | blue | alpha) > 65535))
            throw new ImageCodecException(ImageError.InvalidImage);
        ReadOnlySpan<uint> masks = [red, green, blue, alpha];
        foreach (var mask in masks)
        {
            if (mask == 0) continue;
            var bits = mask >> BitOperations.TrailingZeroCount(mask);
            if ((bits & unchecked(bits + 1)) != 0) throw new ImageCodecException(ImageError.InvalidImage);
        }
    }

    private static byte[]? ReadProfile(ReadOnlySpan<byte> data, BmpHeader header, ImageBudget budget)
    {
        if (header.HeaderSize != 124 || !data.Slice(70, 4).SequenceEqual("DEBM"u8)) return null;
        var offset = (long)BinaryPrimitives.ReadUInt32LittleEndian(data[126..]) + 14;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data[130..]);
        if (offset < 138 || length > int.MaxValue || offset > data.Length - (long)length) return null;
        var profile = data.Slice((int)offset, (int)length);
        return PngStructure.IsRgbProfile(profile) ? budget.Copy(profile) : null;
    }
}

internal readonly record struct BmpHeader(int Width, int Height, bool TopDown, int Depth,
    int HeaderSize, int PixelOffset, int ColorCount, int Compression)
{
    public static BmpHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || !data[..2].SequenceEqual("BM"u8))
            throw new ImageCodecException(ImageError.InvalidImage);
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (headerSize is not (12 or 40 or 52 or 56 or 108 or 124))
            throw new ImageCodecException(ImageError.UnsupportedFormat);
        if (headerSize > data.Length - 14) throw new ImageCodecException(ImageError.InvalidImage);
        int width, height, depth, planes, compression = 0, colorCount = 0;
        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[20..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(data[22..]);
            depth = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
        }
        else
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
            planes = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);
            depth = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
            var compressionValue = BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);
            var colors = BinaryPrimitives.ReadUInt32LittleEndian(data[46..]);
            if (compressionValue > int.MaxValue || colors > 256)
                throw new ImageCodecException(ImageError.UnsupportedFormat);
            compression = (int)compressionValue;
            colorCount = (int)colors;
        }
        if (width <= 0 || height is 0 or int.MinValue || planes != 1)
            throw new ImageCodecException(ImageError.InvalidImage);
        if (depth is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 1 or 2 or 3 or 6))
            throw new ImageCodecException(ImageError.UnsupportedFormat);
        if ((compression == 1 && depth != 8) || (compression == 2 && depth != 4) ||
            (compression is 3 or 6 && depth is not (16 or 32)) ||
            (height < 0 && compression is 1 or 2))
            throw new ImageCodecException(ImageError.InvalidImage);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        if (offset < 14 + headerSize || offset > data.Length)
            throw new ImageCodecException(ImageError.InvalidImage);
        return new(width, Math.Abs(height), height < 0, depth, (int)headerSize,
            (int)offset, colorCount, compression);
    }
}
