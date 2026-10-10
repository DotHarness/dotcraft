namespace DotCraft.Imaging;

internal static class WebpVp8Coefficients
{
    public static bool Read(WebpVp8Bits bits, byte[] probabilities, int type, int context, Span<int> coefficients, int dcScale, int acScale)
    {
        ReadOnlySpan<byte> bands = [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7];
        ReadOnlySpan<byte> zigzag = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        ReadOnlySpan<sbyte> tree = [-11, 2, 0, 4, -1, 6, 8, 12, -2, 10, -3, -4, 14, 16, -5, -6, 18, 20, -7, -8, -9, -10];
        var nonzero = false;
        var previousZero = false;
        for (var i = type == 0 ? 1 : 0; i < 16; i++)
        {
            var offset = ((type * 8 + bands[i]) * 3 + context) * 11;
            var token = bits.Tree(tree, probabilities.AsSpan(offset, 11), previousZero ? 2 : 0);
            if (token == 11) break;
            var value = token <= 4 ? token : Category(bits, token - 5);
            previousZero = value == 0;
            context = Math.Min(value, 2);
            if (value != 0)
            {
                nonzero = true;
                if (bits.Read() != 0) value = -value;
                coefficients[zigzag[i]] = value * (i == 0 ? dcScale : acScale);
            }
        }
        return nonzero;
    }

    private static int Category(WebpVp8Bits bits, int category)
    {
        ReadOnlySpan<byte> probabilities = category switch
        {
            0 => [159],
            1 => [165, 145],
            2 => [173, 148, 140],
            3 => [176, 155, 140, 135],
            4 => [180, 157, 141, 134, 130],
            5 => [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129],
            _ => throw new ImageCodecException(ImageError.InvalidImage)
        };
        var result = 0;
        foreach (var probability in probabilities) result = (result << 1) | bits.Read(probability);
        ReadOnlySpan<int> bases = [5, 7, 11, 19, 35, 67];
        return result + bases[category];
    }
}
