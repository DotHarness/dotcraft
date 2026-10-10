namespace DotCraft.Imaging;

internal static class GifLzw
{
    public static byte[] Decode(ReadOnlySpan<byte> data, int minimumSize, int pixelCount, ImageBudget budget)
    {
        if (minimumSize is < 2 or > 8) throw new ImageCodecException(ImageError.InvalidImage);
        var output = budget.Allocate<byte>(pixelCount);
        var prefix = budget.Allocate<ushort>(4096);
        var suffix = budget.Allocate<byte>(4096);
        var stack = budget.Allocate<byte>(4096);
        try
        {
            var clear = 1 << minimumSize;
            var end = clear + 1;
            var next = end + 1;
            var size = minimumSize + 1;
            var previous = -1;
            var first = 0;
            var position = 0;
            long bit = 0;
            var seenClear = false;
            while (true)
            {
                var code = ReadCode(data, ref bit, size);
                if (code == clear)
                {
                    next = end + 1;
                    size = minimumSize + 1;
                    previous = -1;
                    seenClear = true;
                    continue;
                }
                if (!seenClear) throw new ImageCodecException(ImageError.InvalidImage);
                if (code == end)
                {
                    if (position != pixelCount) throw new ImageCodecException(ImageError.InvalidImage);
                    return output;
                }
                var incoming = code;
                var count = 0;
                if (code == next)
                {
                    if (previous < 0) throw new ImageCodecException(ImageError.InvalidImage);
                    stack[count++] = (byte)first;
                    code = previous;
                }
                else if (code > next || code >= 4096)
                    throw new ImageCodecException(ImageError.InvalidImage);
                while (code >= clear)
                {
                    if (code < end + 1 || code >= next || count >= stack.Length)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    stack[count++] = suffix[code];
                    code = prefix[code];
                }
                first = code;
                if (count >= stack.Length || count + 1 > pixelCount - position)
                    throw new ImageCodecException(ImageError.InvalidImage);
                stack[count++] = (byte)first;
                while (count > 0) output[position++] = stack[--count];
                if (previous >= 0 && next < 4096)
                {
                    prefix[next] = (ushort)previous;
                    suffix[next++] = (byte)first;
                    if (next == 1 << size && size < 12) size++;
                }
                previous = incoming;
            }
        }
        finally
        {
            budget.Release(prefix.Length * sizeof(ushort));
            budget.Release(suffix.Length);
            budget.Release(stack.Length);
        }
    }

    private static int ReadCode(ReadOnlySpan<byte> data, ref long bit, int size)
    {
        if (bit + size > (long)data.Length * 8) throw new ImageCodecException(ImageError.InvalidImage);
        var value = 0;
        for (var shift = 0; shift < size; shift++, bit++)
            value |= ((data[(int)(bit / 8)] >> (int)(bit % 8)) & 1) << shift;
        return value;
    }
}
