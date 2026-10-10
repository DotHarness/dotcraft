using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal static class WebpCodec
{
    private const uint Vp8 = 0x20385056;
    private const uint Vp8L = 0x4c385056;
    private const uint Vp8X = 0x58385056;
    private const uint Alph = 0x48504c41;
    private const uint Anim = 0x4d494e41;
    private const uint Anmf = 0x464d4e41;
    private const uint Exif = 0x46495845;
    private const uint Iccp = 0x50434349;

    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        var end = ValidateHeader(data);
        var position = 12;
        if (!NextChunk(data, end, ref position, out var tag, out var offset, out var length))
            throw new ImageCodecException(ImageError.InvalidImage);
        var bytes = data.Slice(offset, length);
        ImageSize size;
        if (tag == Vp8X)
        {
            if (bytes.Length != 10) throw new ImageCodecException(ImageError.InvalidImage);
            size = new(Read24(bytes[4..]) + 1, Read24(bytes[7..]) + 1);
            if ((long)size.Width * size.Height > uint.MaxValue)
                throw new ImageCodecException(ImageError.InvalidImage);
        }
        else size = IdentifyFrame(tag, bytes);
        return new(ImageFormat.Webp, size);
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var info = Identify(data);
        var end = ValidateHeader(data);
        var position = 12;
        var flags = 0;
        var animationHeader = false;
        DecodedImage? image = null;
        byte[]? exif = null;
        byte[]? profile = null;
        ReadOnlySpan<byte> alpha = default;
        var hasAlphaChunk = false;
        while (NextChunk(data, end, ref position, out var tag, out var offset, out var length))
        {
            var chunk = data.Slice(offset, length);
            switch (tag)
            {
                case Vp8X:
                    if (offset != 20 || length != 10) throw new ImageCodecException(ImageError.InvalidImage);
                    flags = chunk[0];
                    break;
                case Iccp:
                    if (profile is null && chunk.Length >= 20 && chunk.Slice(16, 4).SequenceEqual("RGB "u8))
                        profile = budget.Copy(chunk);
                    break;
                case Exif:
                    if (exif is null)
                    {
                        var tiff = chunk.StartsWith("Exif\0\0"u8) ? chunk[6..] : chunk;
                        if (tiff.Length >= 8 && (tiff.StartsWith("II*\0"u8) || tiff.StartsWith("MM\0*"u8)))
                            exif = budget.Copy(tiff);
                    }
                    break;
                case Anim:
                    if ((flags & 2) != 0)
                    {
                        if (animationHeader || length != 6 || image is not null)
                            throw new ImageCodecException(ImageError.InvalidImage);
                        animationHeader = true;
                    }
                    break;
                case Anmf:
                    if ((flags & 2) == 0 || !animationHeader) throw new ImageCodecException(ImageError.InvalidImage);
                    if (image is null) image = DecodeAnimation(chunk, info.Size, (flags & 16) != 0, budget);
                    break;
                case Alph:
                    if (length == 0 || image is not null || hasAlphaChunk || (flags & 2) != 0)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    hasAlphaChunk = true;
                    alpha = chunk;
                    break;
                case Vp8:
                case Vp8L:
                    if (image is not null || (flags & 2) != 0)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    image = DecodeFrame(tag, chunk, alpha, budget);
                    if (image.Width != info.Size.Width || image.Height != info.Size.Height)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    break;
            }
        }
        if (image is null) throw new ImageCodecException(ImageError.InvalidImage);
        return image with { Exif = exif, IccProfile = profile };
    }

    public static byte[] Encode(DecodedImage image, ImageBudget budget)
    {
        var encoded = WebpLosslessEncoder.Encode(image, budget);
        try
        {
            using var output = new BudgetMemoryStream(budget);
            output.Write("RIFF\0\0\0\0WEBP"u8);
            var profile = image.IccProfile;
            if (profile is not null && (profile.Length < 20 || !profile.AsSpan(16, 4).SequenceEqual("RGB "u8))) profile = null;
            if (profile is not null || image.Exif is not null)
            {
                Span<byte> header = stackalloc byte[10];
                header.Clear();
                header[0] = (byte)((profile is null ? 0 : 32) | (image.Exif is null ? 0 : 8) | (WebpLosslessEncoder.HasAlpha(image) ? 16 : 0));
                Write24(header[4..], image.Width - 1);
                Write24(header[7..], image.Height - 1);
                WriteChunk(output, Vp8X, header);
                if (profile is not null) WriteChunk(output, Iccp, profile);
            }
            WriteChunk(output, Vp8L, encoded);
            if (image.Exif is not null) WriteChunk(output, Exif, image.Exif);
            var result = output.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
            return result;
        }
        finally { budget.Release(encoded.Length); }
    }

    private static DecodedImage DecodeAnimation(ReadOnlySpan<byte> data, ImageSize size, bool hasAlpha, ImageBudget budget)
    {
        if (data.Length < 16) throw new ImageCodecException(ImageError.InvalidImage);
        var x = Read24(data) * 2;
        var y = Read24(data[3..]) * 2;
        var width = Read24(data[6..]) + 1;
        var height = Read24(data[9..]) + 1;
        if ((long)x + width > size.Width || (long)y + height > size.Height)
            throw new ImageCodecException(ImageError.InvalidImage);
        var position = 16;
        ReadOnlySpan<byte> alpha = default;
        var hasAlphaChunk = false;
        DecodedImage? frame = null;
        while (NextChunk(data, data.Length, ref position, out var tag, out var offset, out var length))
        {
            if (tag == Alph)
            {
                if (length == 0 || hasAlphaChunk || frame is not null) throw new ImageCodecException(ImageError.InvalidImage);
                hasAlphaChunk = true;
                alpha = data.Slice(offset, length);
            }
            else if (tag is Vp8 or Vp8L)
            {
                if (frame is not null) throw new ImageCodecException(ImageError.InvalidImage);
                frame = DecodeFrame(tag, data.Slice(offset, length), alpha, budget);
            }
        }
        if (frame is null || frame.Width != width || frame.Height != height)
            throw new ImageCodecException(ImageError.InvalidImage);
        var canvas = DecodedImage.Create(size.Width, size.Height, budget);
        if (!hasAlpha)
            for (var i = 3; i < canvas.Pixels.Length; i += 4) canvas.Pixels[i] = 255;
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var source = (row * width + column) * 4;
                var destination = ((row + y) * size.Width + column + x) * 4;
                if ((data[15] & 2) != 0)
                    frame.Pixels.AsSpan(source, 4).CopyTo(canvas.Pixels.AsSpan(destination, 4));
                else Blend(frame.Pixels.AsSpan(source, 4), canvas.Pixels.AsSpan(destination, 4));
            }
        budget.Release(frame.Pixels.Length);
        return canvas;
    }

    private static void Blend(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var sourceAlpha = source[3];
        var destinationAlpha = destination[3];
        var alphaNumerator = sourceAlpha * 255 + destinationAlpha * (255 - sourceAlpha);
        for (var c = 0; c < 3; c++)
            destination[c] = alphaNumerator == 0 ? (byte)0 : (byte)((source[c] * sourceAlpha * 255 + destination[c] * destinationAlpha * (255 - sourceAlpha)) / alphaNumerator);
        destination[3] = (byte)(alphaNumerator / 255);
    }

    private static DecodedImage DecodeFrame(uint tag, ReadOnlySpan<byte> data, ReadOnlySpan<byte> alpha, ImageBudget budget)
    {
        if (tag == Vp8L && !alpha.IsEmpty) throw new ImageCodecException(ImageError.InvalidImage);
        var image = tag == Vp8L ? WebpLosslessDecoder.Decode(data, budget) : WebpVp8Decoder.Decode(data, budget);
        if (!alpha.IsEmpty) WebpAlpha.Decode(alpha, image, budget);
        return image;
    }

    private static ImageSize IdentifyFrame(uint tag, ReadOnlySpan<byte> data)
    {
        if (tag == Vp8L)
        {
            if (data.Length < 5 || data[0] != 47) throw new ImageCodecException(ImageError.InvalidImage);
            var header = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
            if ((header >> 29) != 0) throw new ImageCodecException(ImageError.InvalidImage);
            return new((int)(header & 0x3fff) + 1, (int)((header >> 14) & 0x3fff) + 1);
        }
        if (tag != Vp8) throw new ImageCodecException(ImageError.InvalidImage);
        if (data.Length < 10 || (data[0] & 1) != 0 || (data[0] & 0x10) == 0 || data[3] != 157 || data[4] != 1 || data[5] != 42)
            throw new ImageCodecException(ImageError.InvalidImage);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3fff;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3fff;
        if (width == 0 || height == 0) throw new ImageCodecException(ImageError.InvalidImage);
        return new(width, height);
    }

    private static int ValidateHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            throw new ImageCodecException(ImageError.InvalidImage);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (length < 4 || (length & 1) != 0 || (long)length + 8 > data.Length)
            throw new ImageCodecException(ImageError.InvalidImage);
        return (int)length + 8;
    }

    private static bool NextChunk(ReadOnlySpan<byte> data, int end, ref int position, out uint tag, out int offset, out int length)
    {
        tag = 0; offset = 0; length = 0;
        if (position == end) return false;
        if (end - position < 8) throw new ImageCodecException(ImageError.InvalidImage);
        tag = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
        offset = position + 8;
        var next = (long)offset + size + (size & 1);
        if (next > end) throw new ImageCodecException(ImageError.InvalidImage);
        length = (int)size;
        if ((size & 1) != 0 && data[offset + length] != 0) throw new ImageCodecException(ImageError.InvalidImage);
        position = (int)next;
        return true;
    }

    private static int Read24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
    private static void Write24(Span<byte> bytes, int value) { bytes[0] = (byte)value; bytes[1] = (byte)(value >> 8); bytes[2] = (byte)(value >> 16); }

    private static void WriteChunk(Stream stream, uint tag, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, tag);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)data.Length);
        stream.Write(header);
        stream.Write(data);
        if ((data.Length & 1) != 0) stream.WriteByte(0);
    }
}
