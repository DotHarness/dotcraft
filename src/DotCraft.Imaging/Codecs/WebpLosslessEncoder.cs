namespace DotCraft.Imaging;

internal static class WebpLosslessEncoder
{
    public static byte[] Encode(DecodedImage image, ImageBudget budget)
    {
        if (image.Width is < 1 or > 16384 || image.Height is < 1 or > 16384)
            throw new ImageCodecException(ImageError.EncodeFailed);
        using var output = new BudgetMemoryStream(budget);
        output.WriteByte(47);
        var bits = new Writer(output);
        bits.Write(image.Width - 1, 14);
        bits.Write(image.Height - 1, 14);
        bits.Write(HasAlpha(image) ? 1 : 0, 1);
        bits.Write(0, 3);
        bits.Write(0, 1);
        bits.Write(0, 1);
        bits.Write(0, 1);
        Span<int> singleton = stackalloc int[4];
        ReadOnlySpan<int> channels = [1, 0, 2, 3];
        for (var c = 0; c < 4; c++)
        {
            singleton[c] = image.Pixels[channels[c]];
            for (var p = channels[c]; p < image.Pixels.Length; p += 4)
                if (image.Pixels[p] != singleton[c]) { singleton[c] = -1; break; }
            if (singleton[c] >= 0) WriteSingle(bits, singleton[c]);
            else WriteByteTree(bits);
        }
        WriteSingle(bits, 0);
        for (var p = 0; p < image.Pixels.Length; p += 4)
            for (var c = 0; c < 4; c++)
                if (singleton[c] < 0) bits.Write(Reverse(image.Pixels[p + channels[c]]), 8);
        bits.Flush();
        return output.ToArray();
    }

    private static void WriteSingle(Writer bits, int value)
    {
        bits.Write(1, 1);
        bits.Write(0, 1);
        bits.Write(1, 1);
        bits.Write(value, 8);
    }

    public static bool HasAlpha(DecodedImage image)
    {
        for (var i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] != 255) return true;
        return false;
    }

    private static void WriteByteTree(Writer bits)
    {
        bits.Write(0, 1);
        bits.Write(8, 4);
        ReadOnlySpan<int> order = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8];
        foreach (var code in order) bits.Write(code is 0 or 8 ? 1 : 0, 3);
        bits.Write(1, 1);
        bits.Write(3, 3);
        bits.Write(254, 8);
        for (var i = 0; i < 256; i++) bits.Write(1, 1);
    }

    private static int Reverse(int value)
    {
        value = ((value & 0x55) << 1) | ((value >> 1) & 0x55);
        value = ((value & 0x33) << 2) | ((value >> 2) & 0x33);
        return ((value & 15) << 4) | (value >> 4);
    }

    private sealed class Writer(Stream output)
    {
        private uint _buffer;
        private int _count;

        public void Write(int value, int count)
        {
            _buffer |= (uint)value << _count;
            _count += count;
            while (_count >= 8)
            {
                output.WriteByte((byte)_buffer);
                _buffer >>= 8;
                _count -= 8;
            }
        }

        public void Flush()
        {
            if (_count > 0) output.WriteByte((byte)_buffer);
            _buffer = 0;
            _count = 0;
        }
    }
}
