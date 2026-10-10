namespace DotCraft.Imaging;

internal static class BmpRle
{
    public static void Decode(ReadOnlySpan<byte> data, DecodedImage image, byte[] palette, int depth)
    {
        for (var i = 0; i < image.Pixels.Length; i += 4)
            BmpCodec.WritePalette(image.Pixels.AsSpan(i, 4), 0, palette);
        var offset = 0;
        var x = 0;
        var y = 0;
        while (offset <= data.Length - 2)
        {
            var count = data[offset++];
            var value = data[offset++];
            if (count != 0)
            {
                ValidatePosition(image, x, y, count);
                for (var i = 0; i < count; i++)
                    WritePixel(image, palette, x++, y, depth == 8 ? value : (value >> (i % 2 == 0 ? 4 : 0)) & 15);
                continue;
            }
            switch (value)
            {
                case 0:
                    x = 0;
                    y++;
                    if (y > image.Height) throw new ImageCodecException(ImageError.InvalidImage);
                    break;
                case 1: return;
                case 2:
                    if (offset > data.Length - 2) throw new ImageCodecException(ImageError.InvalidImage);
                    x += data[offset++]; y += data[offset++];
                    ValidatePosition(image, x, y, 0);
                    break;
                default:
                    ValidatePosition(image, x, y, value);
                    var bytes = depth == 8 ? value : (value + 1) / 2;
                    var paddedBytes = (bytes + 1) & ~1;
                    if (offset > data.Length - paddedBytes) throw new ImageCodecException(ImageError.InvalidImage);
                    for (var i = 0; i < value; i++)
                    {
                        var index = depth == 8 ? data[offset + i] : (data[offset + i / 2] >> (i % 2 == 0 ? 4 : 0)) & 15;
                        WritePixel(image, palette, x++, y, index);
                    }
                    offset += paddedBytes;
                    break;
            }
        }
        throw new ImageCodecException(ImageError.InvalidImage);
    }

    private static void ValidatePosition(DecodedImage image, int x, int y, int count)
    {
        if (x > image.Width - count || y >= image.Height)
            throw new ImageCodecException(ImageError.InvalidImage);
    }

    private static void WritePixel(DecodedImage image, byte[] palette, int x, int y, int value) =>
        BmpCodec.WritePalette(image.Pixels.AsSpan(((image.Height - 1 - y) * image.Width + x) * 4, 4), value, palette);
}
