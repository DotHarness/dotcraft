namespace DotCraft.Imaging;

internal static class WebpVp8Transform
{
    public static void Walsh(Span<int> coefficients)
    {
        Span<int> intermediate = stackalloc int[16];
        for (var column = 0; column < 4; column++)
            Butterfly(coefficients[column], coefficients[column + 4], coefficients[column + 8], coefficients[column + 12], intermediate, column, 4);
        Span<int> row = stackalloc int[4];
        for (var y = 0; y < 4; y++)
        {
            var offset = y * 4;
            Butterfly(intermediate[offset], intermediate[offset + 1], intermediate[offset + 2], intermediate[offset + 3], row, 0, 1);
            for (var x = 0; x < 4; x++) coefficients[offset + x] = (row[x] + 3) >> 3;
        }
    }

    private static void Butterfly(int first, int second, int third, int fourth, Span<int> output, int offset, int stride)
    {
        output[offset] = first + fourth + second + third;
        output[offset + stride] = first - fourth + second - third;
        output[offset + stride * 2] = first + fourth - second - third;
        output[offset + stride * 3] = first - fourth - second + third;
    }

    public static void AddDct(ReadOnlySpan<int> coefficients, byte[] plane, int offset, int stride)
    {
        Span<int> intermediate = stackalloc int[16];
        for (var column = 0; column < 4; column++)
            Dct(coefficients[column], coefficients[column + 4], coefficients[column + 8], coefficients[column + 12], intermediate, column, 4);
        Span<int> row = stackalloc int[4];
        for (var y = 0; y < 4; y++)
        {
            var c = y * 4;
            Dct(intermediate[c], intermediate[c + 1], intermediate[c + 2], intermediate[c + 3], row, 0, 1);
            for (var x = 0; x < 4; x++)
                plane[offset + y * stride + x] = (byte)Math.Clamp(plane[offset + y * stride + x] + ((row[x] + 4) >> 3), 0, 255);
        }
    }

    private static void Dct(int first, int second, int third, int fourth, Span<int> output, int offset, int stride)
    {
        var evenSum = first + third;
        var evenDifference = first - third;
        var oddDifference = ((second * 35468) >> 16) - fourth - ((fourth * 20091) >> 16);
        var oddSum = second + ((second * 20091) >> 16) + ((fourth * 35468) >> 16);
        output[offset] = evenSum + oddSum;
        output[offset + stride] = evenDifference + oddDifference;
        output[offset + stride * 2] = evenDifference - oddDifference;
        output[offset + stride * 3] = evenSum - oddSum;
    }
}
