namespace DotCraft.Imaging;

internal static class WebpVp8Filter
{
    public static void Apply(byte[] y, byte[] u, byte[] v, int columns, int rows, byte[] filters, bool simple, int sharpness)
    {
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var level = filters[row * columns + column] & 63;
                if (level == 0) continue;
                var internalEdges = (filters[row * columns + column] & 64) != 0;
                var interior = level;
                if (sharpness > 0) interior = Math.Min(level >> (sharpness > 4 ? 2 : 1), 9 - sharpness);
                interior = Math.Max(interior, 1);
                var variance = level >= 40 ? 2 : level >= 15 ? 1 : 0;
                FilterPlane(y, columns * 16, column * 16, row * 16, 16, level, interior, variance, internalEdges, simple);
                if (!simple)
                {
                    FilterPlane(u, columns * 8, column * 8, row * 8, 8, level, interior, variance, internalEdges, false);
                    FilterPlane(v, columns * 8, column * 8, row * 8, 8, level, interior, variance, internalEdges, false);
                }
            }
    }

    private static void FilterPlane(byte[] plane, int stride, int x, int y, int size, int level, int interior, int variance, bool internalEdges, bool simple)
    {
        var macroLimit = (level + 2) * 2 + interior;
        var subLimit = level * 2 + interior;
        if (x > 0) Edge(plane, y * stride + x, 1, stride, size, macroLimit, interior, variance, true, simple);
        if (internalEdges)
            for (var i = 4; i < size; i += 4) Edge(plane, y * stride + x + i, 1, stride, size, subLimit, interior, variance, false, simple);
        if (y > 0) Edge(plane, y * stride + x, stride, 1, size, macroLimit, interior, variance, true, simple);
        if (internalEdges)
            for (var i = 4; i < size; i += 4) Edge(plane, (y + i) * stride + x, stride, 1, size, subLimit, interior, variance, false, simple);
    }

    private static void Edge(byte[] plane, int offset, int across, int along, int length, int edgeLimit, int interiorLimit, int variance, bool macroblock, bool simple)
    {
        for (var i = 0; i < length; i++, offset += along)
        {
            var p0 = plane[offset - across] - 128;
            var p1 = plane[offset - across * 2] - 128;
            var q0 = plane[offset] - 128;
            var q1 = plane[offset + across] - 128;
            if (Math.Abs(p0 - q0) * 2 + Math.Abs(p1 - q1) / 2 > edgeLimit) continue;
            var p2 = plane[offset - across * 3] - 128;
            var p3 = plane[offset - across * 4] - 128;
            var q2 = plane[offset + across * 2] - 128;
            var q3 = plane[offset + across * 3] - 128;
            if (!simple && (Math.Abs(p3 - p2) > interiorLimit || Math.Abs(p2 - p1) > interiorLimit || Math.Abs(p1 - p0) > interiorLimit
                || Math.Abs(q3 - q2) > interiorLimit || Math.Abs(q2 - q1) > interiorLimit || Math.Abs(q1 - q0) > interiorLimit)) continue;
            var highVariance = Math.Abs(p1 - p0) > variance || Math.Abs(q1 - q0) > variance;
            if (!simple && macroblock && !highVariance)
            {
                var difference = SignedClamp(SignedClamp(p1 - q1) + 3 * (q0 - p0));
                var adjustment = SignedClamp((27 * difference + 63) >> 7);
                plane[offset - across] = UnsignedClamp(p0 + adjustment);
                plane[offset] = UnsignedClamp(q0 - adjustment);
                adjustment = SignedClamp((18 * difference + 63) >> 7);
                plane[offset - across * 2] = UnsignedClamp(p1 + adjustment);
                plane[offset + across] = UnsignedClamp(q1 - adjustment);
                adjustment = SignedClamp((9 * difference + 63) >> 7);
                plane[offset - across * 3] = UnsignedClamp(p2 + adjustment);
                plane[offset + across * 2] = UnsignedClamp(q2 - adjustment);
            }
            else
            {
                var difference = SignedClamp((simple || highVariance ? SignedClamp(p1 - q1) : 0) + 3 * (q0 - p0));
                var after = SignedClamp(difference + 4) >> 3;
                var before = SignedClamp(difference + 3) >> 3;
                plane[offset - across] = UnsignedClamp(p0 + before);
                plane[offset] = UnsignedClamp(q0 - after);
                if (!simple && !highVariance)
                {
                    var outer = (after + 1) >> 1;
                    plane[offset - across * 2] = UnsignedClamp(p1 + outer);
                    plane[offset + across] = UnsignedClamp(q1 - outer);
                }
            }
        }
    }

    private static int SignedClamp(int value) => Math.Clamp(value, -128, 127);
    private static byte UnsignedClamp(int value) => (byte)(SignedClamp(value) + 128);
}
