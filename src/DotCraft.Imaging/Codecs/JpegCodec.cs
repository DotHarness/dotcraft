using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal static class JpegCodec
{
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 255 || data[1] != 216)
            throw new ImageCodecException(ImageError.InvalidImage);
        var position = 2;
        while (position < data.Length)
        {
            var marker = JpegMarkers.Read(data, ref position);
            if (marker is 0xD9 or 0xDA)
                break;
            var segment = JpegMarkers.Segment(data, ref position);
            if (JpegMarkers.IsFrame(marker))
            {
                if (marker is not (0xC0 or 0xC1 or 0xC2) || segment.Length < 6 || segment[0] != 8)
                    throw new ImageCodecException(ImageError.UnsupportedFormat);
                var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                if (width == 0 || height == 0)
                    throw new ImageCodecException(ImageError.InvalidImage);
                return new(ImageFormat.Jpeg, new(width, height));
            }
        }
        throw new ImageCodecException(ImageError.InvalidImage);
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        var decoder = new JpegDecoder(data, budget);
        return decoder.Decode();
    }

    public static byte[] Encode(DecodedImage image, int quality, JpegSubsampling subsampling, ImageBudget budget)
        => JpegEncoder.Encode(image, quality, subsampling, budget);
}

internal static class JpegMarkers
{
    public static bool IsFrame(int marker) => marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    public static int Read(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length || data[position++] != 255)
            throw new ImageCodecException(ImageError.InvalidImage);
        while (position < data.Length && data[position] == 255)
            position++;
        if (position >= data.Length || data[position] == 0)
            throw new ImageCodecException(ImageError.InvalidImage);
        return data[position++];
    }

    public static ReadOnlySpan<byte> Segment(ReadOnlySpan<byte> data, ref int position)
    {
        if (data.Length - position < 2)
            throw new ImageCodecException(ImageError.InvalidImage);
        var length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        if (length < 2 || length > data.Length - position)
            throw new ImageCodecException(ImageError.InvalidImage);
        var result = data.Slice(position + 2, length - 2);
        position += length;
        return result;
    }
}

internal sealed class JpegComponent
{
    public required int Id { get; init; }
    public required int Horizontal { get; init; }
    public required int Vertical { get; init; }
    public required int Quantization { get; init; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SampleWidth { get; set; }
    public int SampleHeight { get; set; }
    public int Predictor { get; set; }
    public int[] Coefficients { get; set; } = [];
    public byte[] Samples { get; set; } = [];
    public int[]? QuantValues { get; set; }
    public int[] Approximation { get; } = Enumerable.Repeat(-1, 64).ToArray();
}
