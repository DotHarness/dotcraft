using System.Buffers.Binary;

namespace DotCraft.Imaging;

internal static class WebpVp8Decoder
{
    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        if (data.Length < 10 || (data[0] & 1) != 0 || (data[0] & 0x10) == 0 || data[3] != 157 || data[4] != 1 || data[5] != 42)
            throw new ImageCodecException(ImageError.InvalidImage);
        if (((data[0] >> 1) & 7) > 3) throw new ImageCodecException(ImageError.UnsupportedFormat);
        var partitionLength = (data[0] | (data[1] << 8) | (data[2] << 16)) >> 5;
        if (partitionLength < 2 || partitionLength > data.Length - 10)
            throw new ImageCodecException(ImageError.InvalidImage);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 16383;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 16383;
        var columns = (width + 15) / 16;
        var rows = (height + 15) / 16;
        var yStride = columns * 16;
        var uvStride = columns * 8;
        var encoded = budget.Copy(data);
        var control = new WebpVp8Bits(encoded, 10, partitionLength);
        var header = WebpVp8Header.Parse(control, budget);
        var tokens = ReadPartitions(encoded, 10 + partitionLength, header.PartitionCount, budget);
        var yPlane = budget.Allocate<byte>(ImageBudget.BufferLength(yStride, rows * 16, 1));
        var uPlane = budget.Allocate<byte>(ImageBudget.BufferLength(uvStride, rows * 8, 1));
        var vPlane = budget.Allocate<byte>(uPlane.Length);
        var filters = budget.Allocate<byte>(ImageBudget.BufferLength(columns, rows, 1));
        var aboveModes = budget.Allocate<byte>(columns * 4);
        var aboveCoefficients = budget.Allocate<byte>(columns * 9);
        try
        {
            DecodeBlocks(control, tokens, header, columns, rows, yPlane, uPlane, vPlane, filters, aboveModes, aboveCoefficients);
            if (header.FilterLevel != 0)
                WebpVp8Filter.Apply(yPlane, uPlane, vPlane, columns, rows, filters, header.SimpleFilter, header.Sharpness);
            var image = DecodedImage.Create(width, height, budget);
            ConvertToRgba(image, yPlane, uPlane, vPlane, yStride, uvStride);
            return image;
        }
        finally
        {
            budget.Release(encoded.Length + (long)yPlane.Length + uPlane.Length + vPlane.Length + filters.Length + aboveModes.Length + aboveCoefficients.Length);
            budget.Release(header.Probabilities.Length + 128);
            budget.Release((long)tokens.Length * (IntPtr.Size + 64));
        }
    }

    private static WebpVp8Bits[] ReadPartitions(byte[] encoded, int start, int count, ImageBudget budget)
    {
        if ((count - 1) * 3 > encoded.Length - start) throw new ImageCodecException(ImageError.InvalidImage);
        budget.Reserve((long)count * (IntPtr.Size + 64));
        var result = new WebpVp8Bits[count];
        var offset = start + (count - 1) * 3;
        for (var i = 0; i < count; i++)
        {
            var length = i == count - 1 ? encoded.Length - offset : encoded[start + i * 3] | (encoded[start + i * 3 + 1] << 8) | (encoded[start + i * 3 + 2] << 16);
            if (length > encoded.Length - offset) throw new ImageCodecException(ImageError.InvalidImage);
            result[i] = new WebpVp8Bits(encoded, offset, length);
            offset += length;
        }
        return result;
    }

    private static void DecodeBlocks(WebpVp8Bits control, WebpVp8Bits[] partitions, WebpVp8Header header, int columns, int rows,
        byte[] yPlane, byte[] uPlane, byte[] vPlane, byte[] filters, byte[] aboveModes, byte[] aboveCoefficients)
    {
        ReadOnlySpan<sbyte> segmentTree = [2, 4, 0, -1, -2, -3];
        ReadOnlySpan<sbyte> yTree = [-4, 2, 4, 6, 0, -1, -2, -3];
        ReadOnlySpan<byte> yProbabilities = [145, 156, 163, 128];
        ReadOnlySpan<sbyte> uvTree = [0, 2, -1, 4, -2, -3];
        ReadOnlySpan<byte> uvProbabilities = [142, 114, 183];
        ReadOnlySpan<sbyte> blockTree = [0, 2, -1, 4, -2, 6, 8, 12, -3, 10, -5, -6, -4, 14, -7, 16, -8, -9];
        ReadOnlySpan<byte> mappedModes = [0, 2, 3, 1];
        Span<byte> leftModes = stackalloc byte[4];
        Span<byte> leftCoefficients = stackalloc byte[9];
        Span<byte> modes = stackalloc byte[16];
        Span<int> coefficients = stackalloc int[16];
        Span<int> dcCoefficients = stackalloc int[16];
        for (var row = 0; row < rows; row++)
        {
            leftModes.Clear(); leftCoefficients.Clear();
            var tokens = partitions[row % partitions.Length];
            for (var column = 0; column < columns; column++)
            {
                var segment = header.UpdateMap ? control.Tree(segmentTree, header.SegmentProbabilities) : 0;
                var skip = header.MaySkip && control.Read(header.SkipProbability) != 0;
                var yMode = control.Tree(yTree, yProbabilities);
                if (yMode == 4)
                {
                    for (var block = 0; block < 16; block++)
                    {
                        var bx = block & 3;
                        var by = block >> 2;
                        var offset = (aboveModes[column * 4 + bx] * 10 + leftModes[by]) * 9;
                        modes[block] = (byte)control.Tree(blockTree, WebpVp8Tables.SubblockProbabilities.Slice(offset, 9));
                        aboveModes[column * 4 + bx] = leftModes[by] = modes[block];
                    }
                }
                else
                {
                    for (var i = 0; i < 4; i++) aboveModes[column * 4 + i] = leftModes[i] = mappedModes[yMode];
                }
                var uvMode = control.Tree(uvTree, uvProbabilities);
                var quantizer = header.GetQuantizer(segment);
                var dcTable = WebpVp8Tables.DcQuantizers;
                var acTable = WebpVp8Tables.AcQuantizers;
                var yDc = dcTable[Math.Clamp(quantizer + header.QuantizerDeltas[0], 0, 127)];
                var yAc = acTable[quantizer];
                var uvDc = Math.Min(132, (int)dcTable[Math.Clamp(quantizer + header.QuantizerDeltas[3], 0, 127)]);
                var uvAc = acTable[Math.Clamp(quantizer + header.QuantizerDeltas[4], 0, 127)];
                var hasCoefficients = false;
                dcCoefficients.Clear();
                if (yMode != 4)
                {
                    var y2Dc = dcTable[Math.Clamp(quantizer + header.QuantizerDeltas[1], 0, 127)] * 2;
                    var y2Ac = Math.Max(8, acTable[Math.Clamp(quantizer + header.QuantizerDeltas[2], 0, 127)] * 155 / 100);
                    var nonzero = !skip && WebpVp8Coefficients.Read(tokens, header.Probabilities, 1,
                        aboveCoefficients[column * 9 + 8] + leftCoefficients[8], dcCoefficients, y2Dc, y2Ac);
                    aboveCoefficients[column * 9 + 8] = leftCoefficients[8] = nonzero ? (byte)1 : (byte)0;
                    hasCoefficients |= nonzero;
                    WebpVp8Transform.Walsh(dcCoefficients);
                    WebpVp8Prediction.Full(yPlane, columns * 16, column * 16, row * 16, 16, yMode);
                }
                for (var block = 0; block < 24; block++)
                {
                    coefficients.Clear();
                    var chroma = block >= 16;
                    var grid = chroma ? 2 : 4;
                    var index = chroma ? (block - 16) & 3 : block;
                    var bx = index % grid;
                    var by = index / grid;
                    var contextOffset = block < 16 ? 0 : block < 20 ? 4 : 6;
                    var nonzero = !skip && WebpVp8Coefficients.Read(tokens, header.Probabilities, chroma ? 2 : yMode == 4 ? 3 : 0,
                        aboveCoefficients[column * 9 + contextOffset + bx] + leftCoefficients[contextOffset + by], coefficients,
                        chroma ? uvDc : yDc, chroma ? uvAc : yAc);
                    aboveCoefficients[column * 9 + contextOffset + bx] = leftCoefficients[contextOffset + by] = nonzero ? (byte)1 : (byte)0;
                    hasCoefficients |= nonzero;
                    var plane = block < 16 ? yPlane : block < 20 ? uPlane : vPlane;
                    var stride = columns * (chroma ? 8 : 16);
                    var x = column * grid * 4 + bx * 4;
                    var y = row * grid * 4 + by * 4;
                    if (!chroma)
                    {
                        if (yMode == 4) WebpVp8Prediction.Subblock(plane, stride, x, y, row * 16, modes[block]);
                        else coefficients[0] = dcCoefficients[block];
                    }
                    else if (index == 0) WebpVp8Prediction.Full(plane, stride, column * 8, row * 8, 8, uvMode);
                    WebpVp8Transform.AddDct(coefficients, plane, y * stride + x, stride);
                }
                filters[row * columns + column] = (byte)(header.GetFilter(segment, yMode == 4) | (hasCoefficients || yMode == 4 ? 64 : 0));
            }
        }
    }

    private static void ConvertToRgba(DecodedImage image, byte[] yPlane, byte[] uPlane, byte[] vPlane, int yStride, int uvStride)
    {
        var chromaWidth = (image.Width + 1) / 2;
        var chromaHeight = (image.Height + 1) / 2;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var u = Chroma(uPlane, uvStride, chromaWidth, chromaHeight, x, y) - 128;
                var v = Chroma(vPlane, uvStride, chromaWidth, chromaHeight, x, y) - 128;
                var luminance = yPlane[y * yStride + x] - 16;
                var offset = (y * image.Width + x) * 4;
                image.Pixels[offset] = (byte)Math.Clamp((298 * luminance + 409 * v + 128) >> 8, 0, 255);
                image.Pixels[offset + 1] = (byte)Math.Clamp((298 * luminance - 100 * u - 208 * v + 128) >> 8, 0, 255);
                image.Pixels[offset + 2] = (byte)Math.Clamp((298 * luminance + 516 * u + 128) >> 8, 0, 255);
                image.Pixels[offset + 3] = 255;
            }
    }

    private static int Chroma(byte[] plane, int stride, int width, int height, int x, int y)
    {
        var left = (x - 1) >> 1;
        var top = (y - 1) >> 1;
        var xWeight = (x & 1) == 0 ? 3 : 1;
        var yWeight = (y & 1) == 0 ? 3 : 1;
        var x0 = Math.Clamp(left, 0, width - 1);
        var x1 = Math.Clamp(left + 1, 0, width - 1);
        var y0 = Math.Clamp(top, 0, height - 1);
        var y1 = Math.Clamp(top + 1, 0, height - 1);
        return (plane[y0 * stride + x0] * (4 - xWeight) * (4 - yWeight)
            + plane[y0 * stride + x1] * xWeight * (4 - yWeight)
            + plane[y1 * stride + x0] * (4 - xWeight) * yWeight
            + plane[y1 * stride + x1] * xWeight * yWeight + 8) >> 4;
    }
}
