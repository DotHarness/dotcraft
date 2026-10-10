using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace DotCraft.Imaging.Tests;

internal static class CodecExpectations
{
    private static readonly JsonDocument Expected = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "expected.json")));

    public static IEnumerable<string> Names(string prefix) => Expected.RootElement.EnumerateObject()
        .Select(entry => entry.Name).Where(name => name.StartsWith(prefix, StringComparison.Ordinal));

    public static void Pixels(string name, DecodedImage image, int tolerance = 0)
    {
        var entry = Expected.RootElement.GetProperty(name);
        var size = entry.GetProperty("size");
        Assert.Equal(size[0].GetInt32(), image.Width);
        Assert.Equal(size[1].GetInt32(), image.Height);
        Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
        if (tolerance == 0)
        {
            Assert.Equal(entry.GetProperty("sha256").GetString(), Digest(image.Pixels));
            return;
        }
        foreach (var sample in entry.GetProperty("samples").EnumerateArray())
        {
            var offset = (sample[1].GetInt32() * image.Width + sample[0].GetInt32()) * 4;
            for (var channel = 0; channel < 4; channel++)
                Assert.InRange(Math.Abs(image.Pixels[offset + channel] - sample[channel + 2].GetInt32()), 0, channel == 3 ? 0 : tolerance);
        }
    }

    public static void Precision(string name, ushort[]? pixels)
    {
        if (!Expected.RootElement.GetProperty(name).TryGetProperty("rgba16Sha256", out var digest))
            return;
        Assert.NotNull(pixels);
        var bytes = new byte[pixels.Length * 2];
        for (var index = 0; index < pixels.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2), pixels[index]);
        Assert.Equal(digest.GetString(), Digest(bytes));
    }

    public static void Metadata(string name, DecodedImage image)
    {
        var entry = Expected.RootElement.GetProperty(name);
        if (entry.TryGetProperty("exifSha256", out var exif))
        {
            Assert.NotNull(image.Exif);
            Assert.Equal(exif.GetString(), Digest(image.Exif));
        }
        if (entry.TryGetProperty("iccSha256", out var icc))
        {
            Assert.NotNull(image.IccProfile);
            Assert.Equal(icc.GetString(), Digest(image.IccProfile));
        }
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
