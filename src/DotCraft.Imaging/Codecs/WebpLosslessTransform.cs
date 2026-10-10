namespace DotCraft.Imaging;

internal sealed record WebpLosslessTransform(int Type, int Width, int Bits, uint[]? Data)
{
    public uint[] Apply(uint[] pixels, int height, ImageBudget budget)
    {
        if (Type == 3)
        {
            var result = budget.Allocate<uint>(ImageBudget.BufferLength(Width, height, 1));
            var packedWidth = (Width + (1 << Bits) - 1) >> Bits;
            var indexBits = 8 >> Bits;
            var mask = (1 << indexBits) - 1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var packed = (int)((pixels[y * packedWidth + (x >> Bits)] >> 8) & 255);
                    var index = (packed >> ((x & ((1 << Bits) - 1)) * indexBits)) & mask;
                    result[y * Width + x] = index < Data!.Length ? Data[index] : 0;
                }
            budget.Release((long)pixels.Length * sizeof(uint));
            return result;
        }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < Width; x++)
            {
                var i = y * Width + x;
                var pixel = pixels[i];
                if (Type == 0)
                {
                    uint predicted;
                    if (y == 0) predicted = x == 0 ? 0xff000000 : pixels[i - 1];
                    else if (x == 0) predicted = pixels[i - Width];
                    else
                    {
                        var mode = (int)((Data![(y >> Bits) * ((Width + (1 << Bits) - 1) >> Bits) + (x >> Bits)] >> 8) & 255);
                        predicted = Predict(mode, pixels[i - 1], pixels[i - Width], pixels[i - Width - 1], pixels[i - Width + 1]);
                    }
                    pixels[i] = Add(pixel, predicted);
                }
                else
                {
                    var red = (int)((pixel >> 16) & 255);
                    var green = (int)((pixel >> 8) & 255);
                    var blue = (int)(pixel & 255);
                    if (Type == 2) { red += green; blue += green; }
                    else
                    {
                        var coefficients = Data![(y >> Bits) * ((Width + (1 << Bits) - 1) >> Bits) + (x >> Bits)];
                        red += ((sbyte)coefficients * (sbyte)green) >> 5;
                        blue += ((sbyte)(coefficients >> 8) * (sbyte)green) >> 5;
                        blue += ((sbyte)(coefficients >> 16) * (sbyte)(byte)red) >> 5;
                    }
                    pixels[i] = (pixel & 0xff00ff00) | ((uint)(red & 255) << 16) | (uint)(blue & 255);
                }
            }
        return pixels;
    }

    public static uint Add(uint a, uint b)
    {
        var low = ((a & 0x00ff00ff) + (b & 0x00ff00ff)) & 0x00ff00ff;
        var high = (((a >> 8) & 0x00ff00ff) + ((b >> 8) & 0x00ff00ff)) & 0x00ff00ff;
        return low | (high << 8);
    }

    private static uint Average(uint a, uint b) => (((a ^ b) & 0xfefefefe) >> 1) + (a & b);

    private static uint Predict(int mode, uint left, uint top, uint topLeft, uint topRight) => mode switch
    {
        0 => 0xff000000,
        1 => left,
        2 => top,
        3 => topRight,
        4 => topLeft,
        5 => Average(Average(left, topRight), top),
        6 => Average(left, topLeft),
        7 => Average(left, top),
        8 => Average(topLeft, top),
        9 => Average(top, topRight),
        10 => Average(Average(left, topLeft), Average(top, topRight)),
        11 => Select(left, top, topLeft),
        12 => Clamp(left, top, topLeft, false),
        13 => Clamp(Average(left, top), 0, topLeft, true),
        _ => throw new ImageCodecException(ImageError.InvalidImage)
    };

    private static uint Select(uint left, uint top, uint topLeft)
    {
        var leftDistance = 0;
        var topDistance = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var l = (int)((left >> shift) & 255);
            var t = (int)((top >> shift) & 255);
            var tl = (int)((topLeft >> shift) & 255);
            leftDistance += Math.Abs(t - tl);
            topDistance += Math.Abs(l - tl);
        }
        return leftDistance < topDistance ? left : top;
    }

    private static uint Clamp(uint a, uint b, uint c, bool half)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var av = (int)((a >> shift) & 255);
            var bv = (int)((b >> shift) & 255);
            var cv = (int)((c >> shift) & 255);
            result |= (uint)Math.Clamp(half ? av + (av - cv) / 2 : av + bv - cv, 0, 255) << shift;
        }
        return result;
    }
}
