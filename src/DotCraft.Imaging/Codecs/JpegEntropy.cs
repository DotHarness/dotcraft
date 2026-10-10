namespace DotCraft.Imaging;

internal sealed class JpegHuffman
{
    private readonly int[] _firstCode = new int[17];
    private readonly int[] _firstSymbol = new int[17];
    private readonly int[] _counts = new int[17];
    private readonly byte[] _symbols;

    public JpegHuffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
    {
        _symbols = symbols.ToArray();
        var code = 0;
        var offset = 0;
        for (var length = 1; length <= 16; length++)
        {
            _counts[length] = counts[length - 1];
            _firstCode[length] = code;
            _firstSymbol[length] = offset;
            code += counts[length - 1];
            if (code >= 1 << length && counts[length - 1] > 0)
                throw new ImageCodecException(ImageError.InvalidImage);
            offset += counts[length - 1];
            code <<= 1;
        }
        if (offset == 0 || offset != symbols.Length)
            throw new ImageCodecException(ImageError.InvalidImage);
    }

    public int Read(ref JpegEntropyReader reader)
    {
        var code = 0;
        for (var length = 1; length <= 16; length++)
        {
            code = (code << 1) | reader.Bit();
            var index = code - _firstCode[length];
            if (index >= 0 && index < _counts[length])
                return _symbols[_firstSymbol[length] + index];
        }
        throw new ImageCodecException(ImageError.InvalidImage);
    }
}

internal ref struct JpegEntropyReader(ReadOnlySpan<byte> data, int position)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position = position;
    private int _value;
    private int _bits;
    public readonly int Position => _position;

    public int Bit()
    {
        if (_bits == 0)
        {
            if (_position >= _data.Length)
                throw new ImageCodecException(ImageError.InvalidImage);
            _value = _data[_position++];
            if (_value == 255)
            {
                if (_position >= _data.Length || _data[_position++] != 0)
                    throw new ImageCodecException(ImageError.InvalidImage);
            }
            _bits = 8;
        }
        return (_value >> --_bits) & 1;
    }

    public int Bits(int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
            value = (value << 1) | Bit();
        return value;
    }

    public int Signed(int count)
    {
        if (count == 0)
            return 0;
        var value = Bits(count);
        return value < 1 << (count - 1) ? value + 1 - (1 << count) : value;
    }

    public void Restart(int number)
    {
        Finish();
        if (JpegMarkers.Read(_data, ref _position) != 0xD0 + number)
            throw new ImageCodecException(ImageError.InvalidImage);
    }

    public void Finish()
    {
        if (_bits > 0 && (_value & ((1 << _bits) - 1)) != (1 << _bits) - 1)
            throw new ImageCodecException(ImageError.InvalidImage);
        _bits = 0;
    }
}
