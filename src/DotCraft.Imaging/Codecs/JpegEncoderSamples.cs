namespace DotCraft.Imaging;

internal static partial class JpegEncoder
{
    private static void ReadMcu444(DecodedImage image, int originX, int originY, Span<double> samples)
    {
        for (var y = 0; y < 8; y++)
        {
            var row = Math.Min(originY + y, image.Height - 1) * image.Width * 4;
            for (var x = 0; x < 8; x++)
            {
                var offset = row + Math.Min(originX + x, image.Width - 1) * 4;
                var r = image.Pixels[offset];
                var g = image.Pixels[offset + 1];
                var b = image.Pixels[offset + 2];
                var sample = y * 8 + x;
                samples[sample] = .299 * r + .587 * g + .114 * b;
                samples[64 + sample] = -.168736 * r - .331264 * g + .5 * b + 128;
                samples[128 + sample] = .5 * r - .418688 * g - .081312 * b + 128;
            }
        }
    }

    private static void ReadMcu420(DecodedImage image, int originX, int originY, Span<double> samples)
    {
        for (var y = 0; y < 16; y += 2)
        {
            var upper = Math.Min(originY + y, image.Height - 1) * image.Width * 4;
            var lower = Math.Min(originY + y + 1, image.Height - 1) * image.Width * 4;
            for (var x = 0; x < 16; x += 2)
            {
                var left = Math.Min(originX + x, image.Width - 1) * 4;
                var right = Math.Min(originX + x + 1, image.Width - 1) * 4;
                var a = upper + left;
                var b = upper + right;
                var c = lower + left;
                var d = lower + right;
                var r0 = image.Pixels[a];
                var g0 = image.Pixels[a + 1];
                var b0 = image.Pixels[a + 2];
                var r1 = image.Pixels[b];
                var g1 = image.Pixels[b + 1];
                var b1 = image.Pixels[b + 2];
                var r2 = image.Pixels[c];
                var g2 = image.Pixels[c + 1];
                var b2 = image.Pixels[c + 2];
                var r3 = image.Pixels[d];
                var g3 = image.Pixels[d + 1];
                var b3 = image.Pixels[d + 2];
                var luma = ((y / 8) * 2 + x / 8) * 64 + (y & 7) * 8 + (x & 7);
                samples[luma] = .299 * r0 + .587 * g0 + .114 * b0;
                samples[luma + 1] = .299 * r1 + .587 * g1 + .114 * b1;
                samples[luma + 8] = .299 * r2 + .587 * g2 + .114 * b2;
                samples[luma + 9] = .299 * r3 + .587 * g3 + .114 * b3;
                var r = (r0 + r1 + r2 + r3) * .25;
                var g = (g0 + g1 + g2 + g3) * .25;
                var blue = (b0 + b1 + b2 + b3) * .25;
                var chroma = y / 2 * 8 + x / 2;
                samples[256 + chroma] = -.168736 * r - .331264 * g + .5 * blue + 128;
                samples[320 + chroma] = .5 * r - .418688 * g - .081312 * blue + 128;
            }
        }
    }
}
