using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace DotCraft.Plugins;

internal static class PluginDesktopRevision
{
    private static readonly byte[] Domain = "DotCraft.PluginDesktopRevision\0v1\0"u8.ToArray();

    // Files written this close to hashing may change again without a visible size or timestamp change.
    private static readonly TimeSpan SettledWriteAge = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, (string Signature, string Revision)> SettledRevisions =
        new(StringComparer.Ordinal);

    internal static string Copy(string pluginRoot, string destination, string entry, IReadOnlyList<string> styles) =>
        PluginContentTree.CopyAndFingerprint(Path.Combine(pluginRoot, "desktop", "dist"), destination, BuildPrefix(entry, styles));

    public static string Compute(
        string pluginRoot,
        string entry,
        IReadOnlyList<string> styles)
    {
        var outputRoot = Path.GetFullPath(Path.Combine(pluginRoot, "desktop", "dist"));
        var prefix = BuildPrefix(entry, styles);
        var key = outputRoot + "\0" + Convert.ToHexString(prefix);
        var startedUtc = DateTime.UtcNow;
        var metadata = PluginContentTree.ReadMetadata(outputRoot);
        if (SettledRevisions.TryGetValue(key, out var settled) && settled.Signature == metadata.Signature)
            return settled.Revision;

        var revision = PluginContentTree.Fingerprint(outputRoot, prefix);
        if (metadata.LatestWriteUtc < startedUtc - SettledWriteAge)
            SettledRevisions[key] = (metadata.Signature, revision);
        return revision;
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
