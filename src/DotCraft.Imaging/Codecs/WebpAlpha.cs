namespace DotCraft.Imaging;

internal static class WebpAlpha
{
    public static void Decode(ReadOnlySpan<byte> data, DecodedImage image, ImageBudget budget)
    {
        if (data.Length < 1) throw new ImageCodecException(ImageError.InvalidImage);
        var compression = data[0] & 3;
        var filter = (data[0] >> 2) & 3;
        var count = ImageBudget.BufferLength(image.Width, image.Height, 1);
        DecodedImage? decoded = null;
        if (compression == 1)
        {
            var bits = new WebpBits(data[1..]);
            decoded = WebpLosslessDecoder.DecodeStream(ref bits, image.Width, image.Height, budget);
        }
        else if (compression != 0 || data.Length != count + 1)
            throw new ImageCodecException(ImageError.InvalidImage);
        try
        {
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                {
                    var i = y * image.Width + x;
                    var left = x == 0 ? 0 : image.Pixels[(i - 1) * 4 + 3];
                    var top = y == 0 ? 0 : image.Pixels[(i - image.Width) * 4 + 3];
                    var topLeft = x == 0 || y == 0 ? 0 : image.Pixels[(i - image.Width - 1) * 4 + 3];
                    var predictor = filter == 0 ? 0 : y == 0 ? left : x == 0 ? top : filter switch
                    {
                        1 => left,
                        2 => top,
                        _ => Math.Clamp(left + top - topLeft, 0, 255)
                    };
                    var value = decoded is null ? data[i + 1] : decoded.Pixels[i * 4 + 1];
                    image.Pixels[i * 4 + 3] = (byte)(value + predictor);
                }
        }
        finally { if (decoded is not null) budget.Release(decoded.Pixels.Length); }
    }
}
