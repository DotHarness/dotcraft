namespace DotCraft.Imaging;

internal static class WebpVp8Prediction
{
    public static void Full(byte[] plane, int stride, int x, int y, int size, int mode)
    {
        var sum = 0;
        var count = 0;
        if (x > 0)
            for (var i = 0; i < size; i++) { sum += Get(plane, stride, x - 1, y + i); count++; }
        if (y > 0)
            for (var i = 0; i < size; i++) { sum += Get(plane, stride, x + i, y - 1); count++; }
        var average = count == 0 ? 128 : (sum + count / 2) / count;
        var corner = Get(plane, stride, x - 1, y - 1);
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var value = mode switch
                {
                    0 => average,
                    1 => Get(plane, stride, x + column, y - 1),
                    2 => Get(plane, stride, x - 1, y + row),
                    3 => Math.Clamp(Get(plane, stride, x + column, y - 1) + Get(plane, stride, x - 1, y + row) - corner, 0, 255),
                    _ => throw new ImageCodecException(ImageError.InvalidImage)
                };
                plane[(y + row) * stride + x + column] = (byte)value;
            }
    }

    public static void Subblock(byte[] plane, int stride, int x, int y, int macroblockY, int mode)
    {
        Span<int> top = stackalloc int[9];
        Span<int> left = stackalloc int[4];
        Span<int> edge = stackalloc int[9];
        top[0] = Get(plane, stride, x - 1, y - 1);
        for (var i = 0; i < 8; i++)
            top[i + 1] = Get(plane, stride, x + i, i >= 4 && (x & 15) == 12 ? macroblockY - 1 : y - 1);
        for (var i = 0; i < 4; i++) { left[i] = Get(plane, stride, x - 1, y + i); edge[3 - i] = left[i]; }
        for (var i = 0; i < 5; i++) edge[4 + i] = top[i];
        var dc = (top[1] + top[2] + top[3] + top[4] + left[0] + left[1] + left[2] + left[3] + 4) >> 3;
        ReadOnlySpan<sbyte> verticalRight = [4,5,6,7, 4,5,6,7, 3,4,5,6, 2,4,5,6];
        ReadOnlySpan<sbyte> horizontalDown = [3,4,5,6, 2,3,3,4, 1,2,2,3, 0,1,1,2];
        for (var row = 0; row < 4; row++)
            for (var column = 0; column < 4; column++)
            {
                var position = row * 4 + column;
                int value;
                switch (mode)
                {
                    case 0: value = dc; break;
                    case 1: value = Math.Clamp(left[row] + top[column + 1] - top[0], 0, 255); break;
                    case 2: value = Average3(top[column], top[column + 1], top[column + 2]); break;
                    case 3: value = Average3(row == 0 ? top[0] : left[row - 1], left[row], left[Math.Min(3, row + 1)]); break;
                    case 4:
                        var center = row + column + 2;
                        value = Average3(top[center - 1], top[center], top[Math.Min(8, center + 1)]);
                        break;
                    case 5:
                        value = EdgeAverage(edge, 4 + column - row);
                        break;
                    case 6:
                        var index = verticalRight[position];
                        value = (row == 0 || row == 2 && column > 0) ? Average2(edge[index], edge[index + 1]) : EdgeAverage(edge, index);
                        break;
                    case 7:
                        var offset = column + (row >> 1) + 1;
                        if (column == 3 && row >= 2) value = Average3(top[row == 2 ? 5 : 6], top[row == 2 ? 6 : 7], top[row == 2 ? 7 : 8]);
                        else value = (row & 1) == 0 ? Average2(top[offset], top[offset + 1]) : Average3(top[offset], top[offset + 1], top[offset + 2]);
                        break;
                    case 8:
                        var diagonal = horizontalDown[position];
                        var pair = column == 0 || column == 2 && row > 0;
                        value = pair ? Average2(edge[diagonal], edge[diagonal + 1]) : EdgeAverage(edge, diagonal);
                        break;
                    case 9:
                        var k = row * 2 + column;
                        value = k >= 6 ? left[3] : (k & 1) == 0 ? Average2(left[k / 2], left[k / 2 + 1]) : Average3(left[k / 2], left[k / 2 + 1], left[Math.Min(3, k / 2 + 2)]);
                        break;
                    default: throw new ImageCodecException(ImageError.InvalidImage);
                }
                plane[(y + row) * stride + x + column] = (byte)value;
            }
    }

    private static int Get(byte[] plane, int stride, int x, int y)
    {
        if (y < 0) return 127;
        if (x < 0) return 129;
        return plane[y * stride + Math.Min(x, stride - 1)];
    }

    private static int Average2(int a, int b) => (a + b + 1) >> 1;
    private static int Average3(int a, int b, int c) => (a + b * 2 + c + 2) >> 2;
    private static int EdgeAverage(ReadOnlySpan<int> edge, int center) => Average3(edge[center - 1], edge[center], edge[center + 1]);
}
