using System.Buffers.Binary;
using System.Text;

namespace DotCraft.Plugins;

internal static class PluginDesktopRevision
{
    private static readonly byte[] Domain = "DotCraft.PluginDesktopRevision\0v1\0"u8.ToArray();

    public static string Compute(
        string pluginRoot,
        string entry,
        IReadOnlyList<string> styles)
    {
        var outputRoot = Path.Combine(pluginRoot, "desktop", "dist");
        return PluginContentTree.Fingerprint(outputRoot, BuildPrefix(entry, styles));
    }

    private static byte[] BuildPrefix(string entry, IReadOnlyList<string> styles)
    {
        using var stream = new MemoryStream();
        stream.Write(Domain);
        WriteString(stream, entry);
        WriteInt32(stream, styles.Count);
        foreach (var style in styles)
            WriteString(stream, style);
        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
