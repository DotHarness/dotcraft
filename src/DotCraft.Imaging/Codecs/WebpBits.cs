namespace DotCraft.Imaging;

internal ref struct WebpBits(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private long _position;

    public int Read(int count)
    {
        if (count is < 0 or > 24 || _position + count > (long)_data.Length * 8)
            throw new ImageCodecException(ImageError.InvalidImage);
        var value = 0;
        for (var i = 0; i < count; i++, _position++)
            value |= ((_data[(int)(_position >> 3)] >> (int)(_position & 7)) & 1) << i;
        return value;
    }
}

internal sealed class WebpHuffman : IDisposable
{
    private readonly ImageBudget _budget;
    private readonly int[] _children;
    private readonly int _single;

    private WebpHuffman(int[] lengths, ImageBudget budget)
    {
        _budget = budget;
        var populated = 0;
        var lastSymbol = 0;
        Span<int> counts = stackalloc int[16];
        counts.Clear();
        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] is < 0 or > 15)
                throw new ImageCodecException(ImageError.InvalidImage);
            if (lengths[i] == 0) continue;
            counts[lengths[i]]++;
            populated++;
            lastSymbol = i;
        }
        if (populated == 0) throw new ImageCodecException(ImageError.InvalidImage);
        if (populated == 1 && lengths[lastSymbol] != 1)
            throw new ImageCodecException(ImageError.InvalidImage);
        _single = populated == 1 ? lastSymbol : -1;
        _children = budget.Allocate<int>(Math.Max(2, populated * 4));
        Array.Fill(_children, int.MinValue);
        if (_single >= 0) return;
        Span<int> next = stackalloc int[16];
        var code = 0;
        for (var bits = 1; bits <= 15; bits++)
        {
            code = (code + counts[bits - 1]) << 1;
            next[bits] = code;
            if (code + counts[bits] > 1 << bits)
                throw new ImageCodecException(ImageError.InvalidImage);
        }
        if (code + counts[15] != 1 << 15)
            throw new ImageCodecException(ImageError.InvalidImage);
        var nodes = 1;
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length == 0) continue;
            code = next[length]++;
            var node = 0;
            for (var bit = length - 1; bit >= 0; bit--)
            {
                var slot = node * 2 + ((code >> bit) & 1);
                if (bit == 0)
                {
                    if (_children[slot] != int.MinValue)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    _children[slot] = ~symbol;
                }
                else
                {
                    if (_children[slot] == int.MinValue) _children[slot] = nodes++;
                    if (_children[slot] < 0 || nodes * 2 > _children.Length)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    node = _children[slot];
                }
            }
        }
    }

    public int Read(ref WebpBits bits)
    {
        if (_single >= 0) return _single;
        var node = 0;
        for (var depth = 0; depth < 15; depth++)
        {
            node = _children[node * 2 + bits.Read(1)];
            if (node == int.MinValue) throw new ImageCodecException(ImageError.InvalidImage);
            if (node < 0) return ~node;
        }
        throw new ImageCodecException(ImageError.InvalidImage);
    }

    public static WebpHuffman Parse(ref WebpBits bits, int alphabetSize, ImageBudget budget)
    {
        var lengths = budget.Allocate<int>(alphabetSize);
        try
        {
            if (bits.Read(1) != 0)
            {
                var count = bits.Read(1) + 1;
                var first = bits.Read(bits.Read(1) == 0 ? 1 : 8);
                if (first >= alphabetSize) throw new ImageCodecException(ImageError.InvalidImage);
                lengths[first] = 1;
                if (count == 2)
                {
                    var second = bits.Read(8);
                    if (second >= alphabetSize) throw new ImageCodecException(ImageError.InvalidImage);
                    lengths[second] = 1;
                }
            }
            else
            {
                ReadOnlySpan<int> order = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
                var codeLengths = budget.Allocate<int>(19);
                try
                {
                    var count = bits.Read(4) + 4;
                    for (var i = 0; i < count; i++) codeLengths[order[i]] = bits.Read(3);
                    using var code = new WebpHuffman(codeLengths, budget);
                    var tokens = alphabetSize;
                    if (bits.Read(1) != 0) tokens = bits.Read(2 + 2 * bits.Read(3)) + 2;
                    if (tokens > alphabetSize) throw new ImageCodecException(ImageError.InvalidImage);
                    var previous = 8;
                    var position = 0;
                    while (position < alphabetSize && tokens-- > 0)
                    {
                        var value = code.Read(ref bits);
                        if (value < 16)
                        {
                            lengths[position++] = value;
                            if (value != 0) previous = value;
                            continue;
                        }
                        var repeat = value switch
                        {
                            16 => bits.Read(2) + 3,
                            17 => bits.Read(3) + 3,
                            18 => bits.Read(7) + 11,
                            _ => throw new ImageCodecException(ImageError.InvalidImage)
                        };
                        if (repeat > alphabetSize - position)
                            throw new ImageCodecException(ImageError.InvalidImage);
                        Array.Fill(lengths, value == 16 ? previous : 0, position, repeat);
                        position += repeat;
                    }
                }
                finally { budget.Release(19 * sizeof(int)); }
            }
            return new WebpHuffman(lengths, budget);
        }
        finally { budget.Release((long)lengths.Length * sizeof(int)); }
    }

    public void Dispose() => _budget.Release((long)_children.Length * sizeof(int));
}
