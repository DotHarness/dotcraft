using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal static partial class JpegEncoder
{
    private static ReadOnlySpan<byte> LumaQuantization => [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99];
    private static ReadOnlySpan<byte> ChromaQuantization => [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99];

    public static byte[] Encode(DecodedImage image, int quality, JpegSubsampling subsampling, ImageBudget budget)
    {
        if (image.Width > 65535 || image.Height > 65535)
            throw new ImageCodecException(ImageError.EncodeFailed);
        if (image.Width <= 0 || image.Height <= 0 || image.Pixels.Length != ImageBudget.BufferLength(image.Width, image.Height, 4))
            throw new ArgumentException("Invalid decoded image.", nameof(image));
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        if (subsampling is not (JpegSubsampling.Yuv444 or JpegSubsampling.Yuv420))
            throw new ArgumentOutOfRangeException(nameof(subsampling));
        Span<int> quantization = stackalloc int[128];
        var scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        for (var i = 0; i < 64; i++)
        {
            quantization[i] = Math.Clamp((LumaQuantization[i] * scale + 50) / 100, 1, 255);
            quantization[64 + i] = Math.Clamp((ChromaQuantization[i] * scale + 50) / 100, 1, 255);
        }
        using var output = new BudgetMemoryStream(budget);
        Marker(output, 0xD8);
        Segment(output, 0xE0, "JFIF\0\x01\x01\0\0\x01\0\x01\0\0"u8);
        WriteMetadata(output, image);
        WriteQuantization(output, quantization);
        WriteHuffman(output);
        Span<byte> frame = stackalloc byte[15];
        frame[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame[1..], (ushort)image.Height);
        BinaryPrimitives.WriteUInt16BigEndian(frame[3..], (ushort)image.Width);
        frame[5] = 3;
        for (var c = 0; c < 3; c++)
        {
            frame[6 + c * 3] = (byte)(c + 1);
            frame[7 + c * 3] = (byte)(c == 0 && subsampling == JpegSubsampling.Yuv420 ? 0x22 : 0x11);
            frame[8 + c * 3] = (byte)(c == 0 ? 0 : 1);
        }
        Segment(output, 0xC0, frame);
        Segment(output, 0xDA, [3, 1, 0, 2, 0, 3, 0, 0, 63, 0]);
        EncodeScans(output, image, subsampling, quantization);
        Marker(output, 0xD9);
        return output.ToArray();
    }

    private static void EncodeScans(Stream output, DecodedImage image, JpegSubsampling subsampling, ReadOnlySpan<int> quantization)
    {
        var writer = new JpegEntropyWriter(output);
        Span<double> samples = stackalloc double[384];
        Span<int> coefficients = stackalloc int[64];
        Span<int> predictors = stackalloc int[3];
        predictors.Clear();
        var mcuSize = subsampling == JpegSubsampling.Yuv420 ? 16 : 8;
        for (var mcuY = 0; mcuY < image.Height; mcuY += mcuSize)
            for (var mcuX = 0; mcuX < image.Width; mcuX += mcuSize)
            {
                if (mcuSize == 16)
                    ReadMcu420(image, mcuX, mcuY, samples);
                else
                    ReadMcu444(image, mcuX, mcuY, samples);
                for (var component = 0; component < 3; component++)
                {
                    var blocks = component == 0 && mcuSize == 16 ? 4 : 1;
                    var offset = component == 0 ? 0 : (mcuSize == 16 ? 256 : 64) + (component - 1) * 64;
                    for (var block = 0; block < blocks; block++)
                    {
                        JpegTransform.Forward(samples.Slice(offset + block * 64, 64), quantization.Slice(component == 0 ? 0 : 64, 64), coefficients);
                        WriteBlock(writer, coefficients, ref predictors[component]);
                    }
                }
            }
        writer.Finish();
    }

    private static void WriteBlock(JpegEntropyWriter writer, ReadOnlySpan<int> coefficients, ref int predictor)
    {
        var delta = coefficients[0] - predictor;
        predictor = coefficients[0];
        var size = Category(delta);
        if (size > 11)
            throw new ImageCodecException(ImageError.EncodeFailed);
        writer.Bits(size, 4);
        WriteAmplitude(writer, delta, size);
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            var value = coefficients[JpegTransform.ZigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }
            while (run >= 16)
            {
                writer.Bits(1, 8);
                run -= 16;
            }
            size = Category(value);
            if (size > 10)
                throw new ImageCodecException(ImageError.EncodeFailed);
            writer.Bits(2 + run * 10 + size - 1, 8);
            WriteAmplitude(writer, value, size);
            run = 0;
        }
        if (run > 0)
            writer.Bits(0, 8);
    }

    private static int Category(int value)
    {
        var absolute = Math.Abs(value);
        var size = 0;
        for (; absolute != 0; absolute >>= 1)
            size++;
        return size;
    }

    private static void WriteAmplitude(JpegEntropyWriter writer, int value, int size)
    {
        if (size > 0)
            writer.Bits(value < 0 ? value + (1 << size) - 1 : value, size);
    }

    private static void WriteQuantization(Stream output, ReadOnlySpan<int> values)
    {
        Span<byte> segment = stackalloc byte[130];
        for (var table = 0; table < 2; table++)
        {
            segment[table * 65] = (byte)table;
            for (var i = 0; i < 64; i++)
                segment[table * 65 + i + 1] = (byte)values[table * 64 + JpegTransform.ZigZag[i]];
        }
        Segment(output, 0xDB, segment);
    }

    private static void WriteHuffman(Stream output)
    {
        Span<byte> dc = stackalloc byte[29];
        dc.Clear();
        dc[4] = 12;
        for (var i = 0; i < 12; i++)
            dc[17 + i] = (byte)i;
        Segment(output, 0xC4, dc);
        Span<byte> ac = stackalloc byte[179];
        ac.Clear();
        ac[0] = 0x10;
        ac[8] = 162;
        ac[18] = 0xF0;
        for (var run = 0; run < 16; run++)
            for (var size = 1; size <= 10; size++)
                ac[19 + run * 10 + size - 1] = (byte)(run * 16 + size);
        Segment(output, 0xC4, ac);
    }

    private static void WriteMetadata(Stream output, DecodedImage image)
    {
        if (image.Exif is { Length: > 0 } exif)
        {
            if (exif.Length > 65527)
                throw new ImageCodecException(ImageError.EncodeFailed);
            Marker(output, 0xE1);
            Length(output, exif.Length + 8);
            output.Write("Exif\0\0"u8);
            output.Write(exif);
        }
        if (image.IccProfile is { Length: >= 20 } icc && icc.AsSpan(16, 4).SequenceEqual("RGB "u8))
        {
            const int maximumChunk = 65519;
            var chunks = (icc.Length + maximumChunk - 1L) / maximumChunk;
            if (chunks > 255)
                throw new ImageCodecException(ImageError.EncodeFailed);
            for (var sequence = 1; sequence <= chunks; sequence++)
            {
                var offset = (sequence - 1) * maximumChunk;
                var length = Math.Min(maximumChunk, icc.Length - offset);
                Marker(output, 0xE2);
                Length(output, length + 16);
                output.Write("ICC_PROFILE\0"u8);
                output.WriteByte((byte)sequence);
                output.WriteByte((byte)chunks);
                output.Write(icc.AsSpan(offset, length));
            }
        }
    }

    private static void Marker(Stream output, byte marker)
    {
        output.WriteByte(255);
        output.WriteByte(marker);
    }

    private static void Length(Stream output, int length)
    {
        output.WriteByte((byte)(length >> 8));
        output.WriteByte((byte)length);
    }

    private static void Segment(Stream output, byte marker, ReadOnlySpan<byte> data)
    {
        Marker(output, marker);
        Length(output, data.Length + 2);
        output.Write(data);
    }
}

internal sealed class JpegEntropyWriter(Stream output)
{
    private int _value;
    private int _bits;

    public void Bits(int value, int count)
    {
        for (var bit = count - 1; bit >= 0; bit--)
        {
            _value = (_value << 1) | ((value >> bit) & 1);
            if (++_bits != 8)
                continue;
            output.WriteByte((byte)_value);
            if ((_value & 255) == 255)
                output.WriteByte(0);
            _bits = 0;
            _value = 0;
        }
    }

    public void Finish()
    {
        if (_bits != 0)
            Bits((1 << (8 - _bits)) - 1, 8 - _bits);
    }
}
