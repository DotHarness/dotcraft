namespace DotCraft.Imaging;

internal sealed class WebpVp8Bits
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _position;
    private int _range = 255;
    private int _value;
    private int _shifted;
    private readonly bool _initialized;

    public WebpVp8Bits(byte[] data, int start, int length)
    {
        if (length < 0 || start < 0 || length > data.Length - start)
            throw new ImageCodecException(ImageError.InvalidImage);
        _data = data;
        _end = start + length;
        _initialized = length >= 2;
        _position = _initialized ? start + 2 : start;
        _value = _initialized ? (data[start] << 8) | data[start + 1] : 0;
    }

    public int Read(int probability = 128)
    {
        if (!_initialized) throw new ImageCodecException(ImageError.InvalidImage);
        var split = 1 + ((_range - 1) * probability >> 8);
        int bit;
        if (_value >= split << 8)
        {
            bit = 1;
            _value -= split << 8;
            _range -= split;
        }
        else { bit = 0; _range = split; }
        while (_range < 128)
        {
            _range <<= 1;
            _value <<= 1;
            if (++_shifted == 8)
            {
                _shifted = 0;
                if (_position >= _end) throw new ImageCodecException(ImageError.InvalidImage);
                _value |= _data[_position++];
            }
        }
        return bit;
    }

    public int Literal(int count)
    {
        var result = 0;
        for (var i = 0; i < count; i++) result = (result << 1) | Read();
        return result;
    }

    public int Signed(int count)
    {
        var magnitude = Literal(count);
        return Read() == 0 ? magnitude : -magnitude;
    }

    public int OptionalSigned(int count) => Read() == 0 ? 0 : Signed(count);

    public int Tree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int start = 0)
    {
        var node = start;
        for (var depth = 0; depth < tree.Length; depth++)
        {
            node = tree[node + Read(probabilities[node / 2])];
            if (node <= 0) return -node;
        }
        throw new ImageCodecException(ImageError.InvalidImage);
    }
}
