namespace DotCraft.Imaging;

internal static class WebpLosslessDecoder
{
    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageBudget budget)
    {
        if (data.Length < 5 || data[0] != 0x2f)
            throw new ImageCodecException(ImageError.InvalidImage);
        var bits = new WebpBits(data[1..]);
        var width = bits.Read(14) + 1;
        var height = bits.Read(14) + 1;
        _ = bits.Read(1);
        if (bits.Read(3) != 0) throw new ImageCodecException(ImageError.InvalidImage);
        return DecodeStream(ref bits, width, height, budget);
    }

    public static DecodedImage DecodeStream(ref WebpBits bits, int width, int height, ImageBudget budget)
    {
        var transforms = new List<WebpLosslessTransform>(4);
        var used = 0;
        var currentWidth = width;
        try
        {
            while (bits.Read(1) != 0)
            {
                var type = bits.Read(2);
                if ((used & (1 << type)) != 0) throw new ImageCodecException(ImageError.InvalidImage);
                used |= 1 << type;
                var sourceWidth = currentWidth;
                var sizeBits = 0;
                uint[]? pixels = null;
                if (type is 0 or 1)
                {
                    sizeBits = bits.Read(3) + 2;
                    pixels = ReadImage(ref bits, Subsample(currentWidth, sizeBits), Subsample(height, sizeBits), false, budget);
                }
                else if (type == 3)
                {
                    var count = bits.Read(8) + 1;
                    pixels = ReadImage(ref bits, count, 1, false, budget);
                    for (var i = 1; i < count; i++) pixels[i] = WebpLosslessTransform.Add(pixels[i], pixels[i - 1]);
                    sizeBits = count <= 2 ? 3 : count <= 4 ? 2 : count <= 16 ? 1 : 0;
                    currentWidth = Subsample(currentWidth, sizeBits);
                }
                transforms.Add(new(type, sourceWidth, sizeBits, pixels));
            }
            var decoded = ReadImage(ref bits, currentWidth, height, true, budget);
            for (var i = transforms.Count - 1; i >= 0; i--)
                decoded = transforms[i].Apply(decoded, height, budget);
            var result = DecodedImage.Create(width, height, budget);
            for (var i = 0; i < decoded.Length; i++)
            {
                result.Pixels[i * 4] = (byte)(decoded[i] >> 16);
                result.Pixels[i * 4 + 1] = (byte)(decoded[i] >> 8);
                result.Pixels[i * 4 + 2] = (byte)decoded[i];
                result.Pixels[i * 4 + 3] = (byte)(decoded[i] >> 24);
            }
            budget.Release((long)decoded.Length * sizeof(uint));
            return result;
        }
        finally
        {
            foreach (var transform in transforms)
                if (transform.Data is not null) budget.Release((long)transform.Data.Length * sizeof(uint));
        }
    }

    private static uint[] ReadImage(ref WebpBits bits, int width, int height, bool main, ImageBudget budget)
    {
        var count = ImageBudget.BufferLength(width, height, 1);
        var hasCache = bits.Read(1) != 0;
        var cacheBits = hasCache ? bits.Read(4) : 0;
        if (hasCache && cacheBits is < 1 or > 11) throw new ImageCodecException(ImageError.InvalidImage);
        var cache = cacheBits == 0 ? null : budget.Allocate<uint>(1 << cacheBits);
        uint[]? meta = null;
        var prefixBits = 0;
        var prefixWidth = 0;
        var groupCount = 1;
        if (main && bits.Read(1) != 0)
        {
            prefixBits = bits.Read(3) + 2;
            prefixWidth = Subsample(width, prefixBits);
            meta = ReadImage(ref bits, prefixWidth, Subsample(height, prefixBits), false, budget);
            foreach (var pixel in meta) groupCount = Math.Max(groupCount, (int)((pixel >> 8) & 0xffff) + 1);
        }
        budget.Reserve((long)groupCount * 5 * IntPtr.Size);
        var groups = new WebpHuffman[groupCount * 5];
        try
        {
            for (var group = 0; group < groupCount; group++)
            {
                groups[group * 5] = WebpHuffman.Parse(ref bits, 280 + (cache?.Length ?? 0), budget);
                for (var channel = 1; channel < 4; channel++)
                    groups[group * 5 + channel] = WebpHuffman.Parse(ref bits, 256, budget);
                groups[group * 5 + 4] = WebpHuffman.Parse(ref bits, 40, budget);
            }
            var pixels = budget.Allocate<uint>(count);
            var position = 0;
            while (position < count)
            {
                var group = meta is null ? 0 : (int)((meta[((position / width) >> prefixBits) * prefixWidth + ((position % width) >> prefixBits)] >> 8) & 0xffff) * 5;
                var symbol = groups[group].Read(ref bits);
                var from = position;
                if (symbol < 256)
                {
                    var red = groups[group + 1].Read(ref bits);
                    var blue = groups[group + 2].Read(ref bits);
                    var alpha = groups[group + 3].Read(ref bits);
                    pixels[position++] = (uint)((alpha << 24) | (red << 16) | (symbol << 8) | blue);
                }
                else if (symbol < 280)
                {
                    var length = ReadPrefix(ref bits, symbol - 256);
                    var distance = ReadPrefix(ref bits, groups[group + 4].Read(ref bits));
                    distance = MapDistance(distance, width);
                    if (distance > position || length > count - position)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    for (var i = 0; i < length; i++, position++) pixels[position] = pixels[position - distance];
                }
                else
                {
                    if (cache is null || symbol - 280 >= cache.Length)
                        throw new ImageCodecException(ImageError.InvalidImage);
                    pixels[position++] = cache[symbol - 280];
                }
                if (cache is not null)
                    for (var i = from; i < position; i++)
                        cache[(int)(unchecked(pixels[i] * 0x1e35a7bdu) >> (32 - cacheBits))] = pixels[i];
            }
            return pixels;
        }
        finally
        {
            foreach (var group in groups) group?.Dispose();
            budget.Release((long)groups.Length * IntPtr.Size);
            if (meta is not null) budget.Release((long)meta.Length * sizeof(uint));
            if (cache is not null) budget.Release((long)cache.Length * sizeof(uint));
        }
    }

    private static int ReadPrefix(ref WebpBits bits, int prefix)
    {
        if (prefix < 4) return prefix + 1;
        var extra = (prefix - 2) >> 1;
        return ((2 + (prefix & 1)) << extra) + bits.Read(extra) + 1;
    }

    private static int MapDistance(int code, int width)
    {
        if (code > 120) return code - 120;
        ReadOnlySpan<sbyte> offsets =
        [
            0,1, 1,0, 1,1, -1,1, 0,2, 2,0, 1,2, -1,2, 2,1, -2,1, 2,2, -2,2, 0,3, 3,0,
            1,3, -1,3, 3,1, -3,1, 2,3, -2,3, 3,2, -3,2, 0,4, 4,0, 1,4, -1,4, 4,1, -4,1,
            3,3, -3,3, 2,4, -2,4, 4,2, -4,2, 0,5, 3,4, -3,4, 4,3, -4,3, 5,0, 1,5, -1,5,
            5,1, -5,1, 2,5, -2,5, 5,2, -5,2, 4,4, -4,4, 3,5, -3,5, 5,3, -5,3, 0,6, 6,0,
            1,6, -1,6, 6,1, -6,1, 2,6, -2,6, 6,2, -6,2, 4,5, -4,5, 5,4, -5,4, 3,6, -3,6,
            6,3, -6,3, 0,7, 7,0, 1,7, -1,7, 5,5, -5,5, 7,1, -7,1, 4,6, -4,6, 6,4, -6,4,
            2,7, -2,7, 7,2, -7,2, 3,7, -3,7, 7,3, -7,3, 5,6, -5,6, 6,5, -6,5, 8,0, 4,7,
            -4,7, 7,4, -7,4, 8,1, 8,2, 6,6, -6,6, 8,3, 5,7, -5,7, 7,5, -7,5, 8,4, 6,7,
            -6,7, 7,6, -7,6, 8,5, 7,7, -7,7, 8,6, 8,7
        ];
        return Math.Max(1, offsets[(code - 1) * 2] + offsets[(code - 1) * 2 + 1] * width);
    }

    private static int Subsample(int value, int bits) => (value + (1 << bits) - 1) >> bits;
}
