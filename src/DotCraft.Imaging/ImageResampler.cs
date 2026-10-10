namespace DotCraft.Imaging;

internal static class ImageResampler
{
    public static DecodedImage Resize(DecodedImage source, int width, int height, ImageBudget budget)
    {
        if (width == source.Width && height == source.Height)
            return ToRgba8(source, budget);
        var output = DecodedImage.Create(width, height, budget) with { Exif = source.Exif, IccProfile = source.IccProfile };
        var verticalFirst = (long)source.Width * height <= (long)width * source.Height;
        var intermediate = budget.Allocate<float>(verticalFirst
            ? ImageBudget.BufferLength(source.Width, height, 4)
            : ImageBudget.BufferLength(width, source.Height, 4));
        try
        {
            if (verticalFirst)
            {
                Vertical(source, intermediate, height);
                Horizontal(intermediate, source.Width, height, output.Pixels, width);
            }
            else
            {
                HorizontalFirst(source, intermediate, width);
                VerticalLast(intermediate, width, source.Height, output.Pixels, height);
            }
        }
        finally
        {
            budget.Release((long)intermediate.Length * sizeof(float));
        }
        return output;
    }

    public static DecodedImage ToRgba8(DecodedImage source, ImageBudget budget)
    {
        if (source.Pixels16 is not { } highPrecision)
            return source;
        var bytes = budget.Allocate<byte>(highPrecision.Length);
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)((highPrecision[index] + 128) / 257);
        return source with { Pixels = bytes, Pixels16 = null };
    }

    private static void Vertical(DecodedImage source, float[] output, int height)
    {
        var ratio = (float)source.Height / height;
        var support = Math.Max(1, ratio);
        for (var y = 0; y < height; y++)
        {
            var center = (y + 0.5f) * ratio;
            var start = Math.Clamp((int)MathF.Floor(center - support), 0, source.Height - 1);
            var end = Math.Clamp((int)MathF.Ceiling(center + support), start + 1, source.Height);
            var total = 0f;
            for (var sy = start; sy < end; sy++)
                total += Weight(sy + 0.5f - center, support);
            for (var x = 0; x < source.Width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0f;
                    for (var sy = start; sy < end; sy++)
                    {
                        var index = (sy * source.Width + x) * 4 + channel;
                        var value = source.Pixels16 is { } pixels16 ? pixels16[index] / 257f : source.Pixels[index];
                        sum += value * Weight(sy + 0.5f - center, support);
                    }
                    output[(y * source.Width + x) * 4 + channel] = sum / total;
                }
            }
        }
    }

    private static void Horizontal(float[] source, int sourceWidth, int height, byte[] output, int width)
    {
        var ratio = (float)sourceWidth / width;
        var support = Math.Max(1, ratio);
        for (var x = 0; x < width; x++)
        {
            var center = (x + 0.5f) * ratio;
            var start = Math.Clamp((int)MathF.Floor(center - support), 0, sourceWidth - 1);
            var end = Math.Clamp((int)MathF.Ceiling(center + support), start + 1, sourceWidth);
            var total = 0f;
            for (var sx = start; sx < end; sx++)
                total += Weight(sx + 0.5f - center, support);
            for (var y = 0; y < height; y++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0f;
                    for (var sx = start; sx < end; sx++)
                        sum += source[(y * sourceWidth + sx) * 4 + channel] * Weight(sx + 0.5f - center, support);
                    output[(y * width + x) * 4 + channel] = (byte)Math.Clamp((int)MathF.Round(sum / total, MidpointRounding.AwayFromZero), 0, 255);
                }
            }
        }
    }

    private static float Weight(float distance, float support) => Math.Max(0, 1 - Math.Abs(distance / support));

    private static void HorizontalFirst(DecodedImage source, float[] output, int width)
    {
        var ratio = (float)source.Width / width;
        var support = Math.Max(1, ratio);
        for (var x = 0; x < width; x++)
        {
            var center = (x + 0.5f) * ratio;
            var start = Math.Clamp((int)MathF.Floor(center - support), 0, source.Width - 1);
            var end = Math.Clamp((int)MathF.Ceiling(center + support), start + 1, source.Width);
            var total = 0f;
            for (var sx = start; sx < end; sx++) total += Weight(sx + 0.5f - center, support);
            for (var y = 0; y < source.Height; y++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0f;
                    for (var sx = start; sx < end; sx++)
                    {
                        var index = (y * source.Width + sx) * 4 + channel;
                        var value = source.Pixels16 is { } highPrecision ? highPrecision[index] / 257f : source.Pixels[index];
                        sum += value * Weight(sx + 0.5f - center, support);
                    }
                    output[(y * width + x) * 4 + channel] = sum / total;
                }
            }
        }
    }

    private static void VerticalLast(float[] source, int width, int sourceHeight, byte[] output, int height)
    {
        var ratio = (float)sourceHeight / height;
        var support = Math.Max(1, ratio);
        for (var y = 0; y < height; y++)
        {
            var center = (y + 0.5f) * ratio;
            var start = Math.Clamp((int)MathF.Floor(center - support), 0, sourceHeight - 1);
            var end = Math.Clamp((int)MathF.Ceiling(center + support), start + 1, sourceHeight);
            var total = 0f;
            for (var sy = start; sy < end; sy++) total += Weight(sy + 0.5f - center, support);
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0f;
                    for (var sy = start; sy < end; sy++)
                        sum += source[(sy * width + x) * 4 + channel] * Weight(sy + 0.5f - center, support);
                    output[(y * width + x) * 4 + channel] = (byte)Math.Clamp((int)MathF.Round(sum / total, MidpointRounding.AwayFromZero), 0, 255);
                }
            }
        }
    }
}
