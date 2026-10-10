using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal static class GifCodec
{
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || (!data[..6].SequenceEqual("GIF87a"u8) && !data[..6].SequenceEqual("GIF89a"u8)))
            throw new ImageCodecException(ImageError.InvalidImage);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (width == 0 || height == 0) throw new ImageCodecException(ImageError.InvalidImage);
        return new(ImageFormat.Gif, new(width, height));
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var info = Identify(data);
        var image = DecodedImage.Create(info.Size.Width, info.Size.Height, budget);
        var reader = new GifReader(data[13..]);
        byte[]? global = null;
        var transparent = -1;
        try
        {
            if ((data[10] & 128) != 0) global = budget.Copy(reader.Take(3 * (2 << (data[10] & 7))));
            while (!reader.IsEmpty)
            {
                switch (reader.Byte())
                {
                    case 0x21:
                        var label = reader.Byte();
                        if (label == 0xf9)
                        {
                            if (reader.Byte() != 4) throw new ImageCodecException(ImageError.InvalidImage);
                            var control = reader.Take(4);
                            transparent = (control[0] & 1) != 0 ? control[3] : -1;
                            if (reader.Byte() != 0) throw new ImageCodecException(ImageError.InvalidImage);
                        }
                        else
                        {
                            reader.SkipBlocks();
                            if (label == 1) transparent = -1;
                        }
                        break;
                    case 0x2c:
                        ReadFrame(ref reader, image, global, transparent, budget);
                        return image;
                    default: throw new ImageCodecException(ImageError.InvalidImage);
                }
            }
            throw new ImageCodecException(ImageError.InvalidImage);
        }
        finally { budget.Release(global?.Length ?? 0); }
    }

    private static void ReadFrame(ref GifReader reader, DecodedImage image, byte[]? global, int transparent, ImageBudget budget)
    {
        var descriptor = reader.Take(9);
        int x0 = BinaryPrimitives.ReadUInt16LittleEndian(descriptor),
            y0 = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]),
            width = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..]),
            height = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[6..]);
        if (width == 0 || height == 0 || x0 + width > image.Width || y0 + height > image.Height)
            throw new ImageCodecException(ImageError.InvalidImage);
        var flags = descriptor[8];
        byte[]? local = null;
        byte[]? compressed = null;
        byte[]? indices = null;
        try
        {
            if ((flags & 128) != 0) local = budget.Copy(reader.Take(3 * (2 << (flags & 7))));
            var palette = local ?? global ?? throw new ImageCodecException(ImageError.InvalidImage);
            var codeSize = reader.Byte();
            compressed = reader.ReadBlocks(budget);
            indices = GifLzw.Decode(compressed, codeSize, ImageBudget.BufferLength(width, height, 1), budget);
            var index = 0;
            for (var pass = 0; pass < ((flags & 64) != 0 ? 4 : 1); pass++)
            {
                var start = (flags & 64) == 0 ? 0 : pass switch { 0 => 0, 1 => 4, 2 => 2, _ => 1 };
                var step = (flags & 64) == 0 ? 1 : pass switch { 0 or 1 => 8, 2 => 4, _ => 2 };
                for (var y = start; y < height; y += step)
                    for (var x = 0; x < width; x++)
                    {
                        var value = indices[index++];
                        if (value >= palette.Length / 3) throw new ImageCodecException(ImageError.InvalidImage);
                        if (value == transparent) continue;
                        var pixel = ((y0 + y) * image.Width + x0 + x) * 4;
                        palette.AsSpan(value * 3, 3).CopyTo(image.Pixels.AsSpan(pixel, 3));
                        image.Pixels[pixel + 3] = 255;
                    }
            }
        }
        finally
        {
            budget.Release(local?.Length ?? 0);
            budget.Release(compressed?.Length ?? 0);
            budget.Release(indices?.Length ?? 0);
        }
    }
}

internal ref struct GifReader(ReadOnlySpan<byte> data)
{
    private ReadOnlySpan<byte> _remaining = data;
    public bool IsEmpty => _remaining.IsEmpty;
    public byte Byte() => Take(1)[0];
    public ReadOnlySpan<byte> Take(int count)
    {
        if (count > _remaining.Length) throw new ImageCodecException(ImageError.InvalidImage);
        var result = _remaining[..count];
        _remaining = _remaining[count..];
        return result;
    }

    public void SkipBlocks()
    {
        int count;
        while ((count = Byte()) != 0) Take(count);
    }

    public byte[] ReadBlocks(ImageBudget budget)
    {
        var probe = this;
        long total = 0;
        int count;
        while ((count = probe.Byte()) != 0) { total += count; probe.Take(count); }
        if (total > int.MaxValue) throw new ImageCodecException(ImageError.LimitExceeded);
        var bytes = budget.Allocate<byte>((int)total);
        var offset = 0;
        while ((count = Byte()) != 0)
        {
            Take(count).CopyTo(bytes.AsSpan(offset));
            offset += count;
        }
        return bytes;
    }
}
