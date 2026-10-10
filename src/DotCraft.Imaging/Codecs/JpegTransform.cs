namespace DotCraft.Imaging;

internal static class JpegTransform
{
    public static ReadOnlySpan<int> ZigZag => [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];

    private static readonly double[] Basis = CreateBasis();

    private static double[] CreateBasis()
    {
        var values = new double[64];
        for (var k = 0; k < 8; k++)
            for (var x = 0; x < 8; x++)
                values[k * 8 + x] = Math.Cos((2 * x + 1) * k * Math.PI / 16) * (k == 0 ? 1 / Math.Sqrt(2) : 1) / 2;
        return values;
    }

    public static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Floor(value + .5), 0, 255);

    public static void Inverse(ReadOnlySpan<int> coefficients, ReadOnlySpan<int> quantization, Span<byte> samples, int stride)
    {
        var dcOnly = true;
        for (var i = 1; i < 64; i++)
            dcOnly &= coefficients[i] == 0;
        if (dcOnly)
        {
            var value = Clamp((double)coefficients[0] * quantization[0] / 8 + 128);
            for (var y = 0; y < 8; y++)
                samples.Slice(y * stride, 8).Fill(value);
            return;
        }
        Span<double> intermediate = stackalloc double[64];
        for (var v = 0; v < 8; v++)
            for (var x = 0; x < 8; x++)
            {
                var value = 0.0;
                for (var u = 0; u < 8; u++)
                    value += coefficients[v * 8 + u] * (double)quantization[v * 8 + u] * Basis[u * 8 + x];
                intermediate[v * 8 + x] = value;
            }
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var value = 128.0;
                for (var v = 0; v < 8; v++)
                    value += intermediate[v * 8 + x] * Basis[v * 8 + y];
                samples[y * stride + x] = Clamp(value);
            }
    }

    public static void Forward(ReadOnlySpan<double> samples, ReadOnlySpan<int> quantization, Span<int> coefficients)
    {
        var constant = true;
        for (var i = 1; i < 64; i++)
            constant &= samples[i] == samples[0];
        if (constant)
        {
            coefficients.Clear();
            coefficients[0] = (int)Math.Round((samples[0] - 128) * 8 / quantization[0], MidpointRounding.AwayFromZero);
            return;
        }
        Span<double> intermediate = stackalloc double[64];
        for (var y = 0; y < 8; y++)
            ForwardLine(samples[(y * 8)..], 1, 128, intermediate[(y * 8)..], 1);
        Span<double> transformed = stackalloc double[64];
        for (var u = 0; u < 8; u++)
            ForwardLine(intermediate[u..], 8, 0, transformed[u..], 8);
        for (var k = 0; k < 64; k++)
            coefficients[k] = (int)Math.Round(transformed[k] / quantization[k], MidpointRounding.AwayFromZero);
    }

    private static void ForwardLine(ReadOnlySpan<double> input, int sourceStride, double offset, Span<double> output, int targetStride)
    {
        var s0 = input[0] + input[7 * sourceStride] - 2 * offset;
        var s1 = input[sourceStride] + input[6 * sourceStride] - 2 * offset;
        var s2 = input[2 * sourceStride] + input[5 * sourceStride] - 2 * offset;
        var s3 = input[3 * sourceStride] + input[4 * sourceStride] - 2 * offset;
        var d0 = input[0] - input[7 * sourceStride];
        var d1 = input[sourceStride] - input[6 * sourceStride];
        var d2 = input[2 * sourceStride] - input[5 * sourceStride];
        var d3 = input[3 * sourceStride] - input[4 * sourceStride];
        var a = s0 + s3;
        var b = s1 + s2;
        var u = s0 - s3;
        var v = s1 - s2;
        output[0] = (a + b) * .3535533905932738;
        output[4 * targetStride] = (a - b) * .3535533905932738;
        output[2 * targetStride] = u * .4619397662556434 + v * .1913417161825449;
        output[6 * targetStride] = u * .1913417161825449 - v * .4619397662556434;
        output[targetStride] = d0 * .4903926402016152 + d1 * .4157348061512726 + d2 * .2777851165098011 + d3 * .09754516100806417;
        output[3 * targetStride] = d0 * .4157348061512726 - d1 * .09754516100806417 - d2 * .4903926402016152 - d3 * .2777851165098011;
        output[5 * targetStride] = d0 * .2777851165098011 - d1 * .4903926402016152 + d2 * .09754516100806417 + d3 * .4157348061512726;
        output[7 * targetStride] = d0 * .09754516100806417 - d1 * .2777851165098011 + d2 * .4157348061512726 - d3 * .4903926402016152;
    }
}
