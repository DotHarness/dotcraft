using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal ref partial struct JpegDecoder
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly ImageBudget _budget;
    private readonly int[]?[] _quantization;
    private readonly JpegHuffman?[,] _huffman;
    private readonly byte[]?[] _iccChunks;
    private JpegComponent[] _components;
    private int _position;
    private int _width;
    private int _height;
    private int _horizontal;
    private int _vertical;
    private int _mcuColumns;
    private int _mcuRows;
    private int _restart;
    private int _adobe;
    private int _iccCount;
    private bool _iccInvalid;
    private bool _progressive;
    private bool _hasScan;
    private bool _jfif;
    private byte[]? _exif;

    public JpegDecoder(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        _data = data;
        _budget = budget;
        _budget.Reserve(16 * 1024);
        _quantization = new int[4][];
        _huffman = new JpegHuffman[2, 4];
        _iccChunks = new byte[256][];
        _components = [];
        _position = 2;
        _adobe = -1;
    }

    public DecodedImage Decode()
    {
        if (_data.Length < 4 || _data[0] != 255 || _data[1] != 216)
            throw Invalid();
        while (_position < _data.Length)
        {
            var marker = JpegMarkers.Read(_data, ref _position);
            if (marker == 0xD9)
            {
                if (!_hasScan || _components.Any(c => c.Approximation[0] < 0))
                    throw Invalid();
                if (!_progressive && _components.Any(c => c.Approximation.Any(a => a < 0)))
                    throw Invalid();
                return CreatePixels();
            }
            if (marker is 0xD8 or >= 0xD0 and <= 0xD7 or 0x01)
                throw Invalid();
            var segment = JpegMarkers.Segment(_data, ref _position);
            switch (marker)
            {
                case 0xC0: case 0xC1: case 0xC2:
                    Frame(segment, marker == 0xC2);
                    break;
                case 0xDB:
                    Quantization(segment);
                    break;
                case 0xC4:
                    Huffman(segment);
                    break;
                case 0xDA:
                    Scan(segment);
                    _hasScan = true;
                    break;
                case 0xDD:
                    if (segment.Length != 2)
                        throw Invalid();
                    _restart = BinaryPrimitives.ReadUInt16BigEndian(segment);
                    break;
                case 0xE0: case 0xE1: case 0xE2: case 0xEE:
                    Metadata(segment, marker);
                    break;
                default:
                    if (JpegMarkers.IsFrame(marker) || marker is 0xCC or 0xDC or 0xDE or 0xDF)
                        throw new ImageCodecException(ImageError.UnsupportedFormat);
                    if (marker is not (>= 0xE0 and <= 0xEF or 0xFE))
                        throw Invalid();
                    break;
            }
        }
        throw Invalid();
    }

    private void Frame(scoped ReadOnlySpan<byte> segment, bool progressive)
    {
        if (_components.Length != 0 || segment.Length < 6)
            throw Invalid();
        if (segment[0] != 8)
            throw new ImageCodecException(ImageError.UnsupportedFormat);
        _height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        _width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var count = segment[5];
        if (count is not (1 or 3 or 4))
            throw new ImageCodecException(ImageError.UnsupportedFormat);
        if (_width == 0 || _height == 0 || segment.Length != 6 + count * 3)
            throw Invalid();
        _components = new JpegComponent[count];
        var blocksPerMcu = 0;
        for (var i = 0; i < count; i++)
        {
            var horizontal = segment[7 + i * 3] >> 4;
            var vertical = segment[7 + i * 3] & 15;
            var quantization = segment[8 + i * 3];
            if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || quantization > 3)
                throw Invalid();
            var id = segment[6 + i * 3];
            if (_components.Take(i).Any(c => c.Id == id))
                throw Invalid();
            _components[i] = new() { Id = id, Horizontal = horizontal, Vertical = vertical, Quantization = quantization };
            _horizontal = Math.Max(_horizontal, horizontal);
            _vertical = Math.Max(_vertical, vertical);
            blocksPerMcu += horizontal * vertical;
        }
        if (count > 1 && blocksPerMcu > 10)
            throw Invalid();
        _mcuColumns = (_width + _horizontal * 8 - 1) / (_horizontal * 8);
        _mcuRows = (_height + _vertical * 8 - 1) / (_vertical * 8);
        foreach (var component in _components)
        {
            component.Columns = _mcuColumns * component.Horizontal;
            component.Rows = _mcuRows * component.Vertical;
            component.SampleWidth = (_width * component.Horizontal + _horizontal - 1) / _horizontal;
            component.SampleHeight = (_height * component.Vertical + _vertical - 1) / _vertical;
            component.Coefficients = _budget.Allocate<int>(ImageBudget.BufferLength(component.Columns, component.Rows, 64));
        }
        _progressive = progressive;
    }

    private void Quantization(scoped ReadOnlySpan<byte> segment)
    {
        while (!segment.IsEmpty)
        {
            var precision = segment[0] >> 4;
            var id = segment[0] & 15;
            if (id > 3 || precision > 1 || segment.Length < 1 + 64 * (precision + 1))
                throw Invalid();
            segment = segment[1..];
            var table = _quantization[id] ??= _budget.Allocate<int>(64);
            for (var i = 0; i < 64; i++)
            {
                var value = precision == 0 ? segment[i] : BinaryPrimitives.ReadUInt16BigEndian(segment[(i * 2)..]);
                if (value == 0)
                    throw Invalid();
                table[JpegTransform.ZigZag[i]] = value;
            }
            segment = segment[(64 * (precision + 1))..];
        }
    }

    private void Huffman(scoped ReadOnlySpan<byte> segment)
    {
        while (!segment.IsEmpty)
        {
            if (segment.Length < 17 || segment[0] >> 4 > 1 || (segment[0] & 15) > 3)
                throw Invalid();
            var count = 0;
            for (var i = 1; i <= 16; i++)
                count += segment[i];
            if (count > 256 || segment.Length < 17 + count)
                throw Invalid();
            _huffman[segment[0] >> 4, segment[0] & 15] = new(segment.Slice(1, 16), segment.Slice(17, count));
            segment = segment[(17 + count)..];
        }
    }

    private void Metadata(scoped ReadOnlySpan<byte> segment, int marker)
    {
        if (marker == 0xE0 && segment.Length >= 5 && segment[..5].SequenceEqual("JFIF\0"u8))
            _jfif = true;
        else if (marker == 0xEE && segment.Length >= 12 && segment[..5].SequenceEqual("Adobe"u8))
            _adobe = segment[11];
        else if (marker == 0xE1 && segment.Length > 6 && segment[..6].SequenceEqual("Exif\0\0"u8) && _exif is null)
            _exif = _budget.Copy(segment[6..]);
        else if (marker == 0xE2 && segment.Length >= 14 && segment[..12].SequenceEqual("ICC_PROFILE\0"u8))
        {
            var sequence = segment[12];
            var count = segment[13];
            if (sequence == 0 || count == 0 || sequence > count || (_iccCount != 0 && _iccCount != count) || _iccChunks[sequence] is not null)
                _iccInvalid = true;
            else
            {
                _iccCount = count;
                _iccChunks[sequence] = _budget.Copy(segment[14..]);
            }
        }
    }

    private byte[]? CollectIcc()
    {
        if (_iccInvalid || _iccCount == 0)
            return null;
        var length = 0;
        for (var i = 1; i <= _iccCount; i++)
        {
            if (_iccChunks[i] is not { } chunk)
                return null;
            length += chunk.Length;
        }
        if (length < 20)
            return null;
        var profile = _budget.Allocate<byte>(length);
        var offset = 0;
        for (var i = 1; i <= _iccCount; i++)
        {
            _iccChunks[i]!.CopyTo(profile, offset);
            offset += _iccChunks[i]!.Length;
            _budget.Release(_iccChunks[i]!.Length);
            _iccChunks[i] = null;
        }
        if (profile.AsSpan(16, 4).SequenceEqual("RGB "u8))
            return profile;
        _budget.Release(profile.Length);
        return null;
    }

    private static ImageCodecException Invalid() => new(ImageError.InvalidImage);
}
