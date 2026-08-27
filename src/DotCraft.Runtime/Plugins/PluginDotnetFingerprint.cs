using System.Buffers.Binary;
using System.Text;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>Identifies the bytes and manifest contract that may affect in-process .NET execution.</summary>
internal static class PluginDotnetFingerprint
{
    private const string ManifestPath = ".craft-plugin/plugin.json";
    private static readonly byte[] Domain = "DotCraft.PluginDotnetFingerprint\0v1\0"u8.ToArray();

    public static string Compute(string pluginRoot)
    {
        var parsed = PluginManifestParser.Load(pluginRoot);
        var manifest = parsed.Manifest;
        if (manifest?.Dotnet == null)
            throw new InvalidOperationException("A valid .NET plugin manifest is required.");

        return Compute(manifest);
    }

    public static string Compute(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var dotnet = manifest.Dotnet
                     ?? throw new InvalidOperationException("A .NET plugin manifest is required.");
        return PluginContentTree.Fingerprint(
            manifest.RootPath,
            BuildPrefix(manifest, dotnet),
            IncludeEntry);
    }

    private static bool IncludeEntry(string path) =>
        !string.Equals(path, ".builtin", StringComparison.Ordinal)
        && !string.Equals(path, ManifestPath, StringComparison.Ordinal)
        && !string.Equals(path, "desktop", StringComparison.Ordinal)
        && !path.StartsWith("desktop/", StringComparison.Ordinal);

    private static byte[] BuildPrefix(PluginManifest manifest, PluginDotnetManifest dotnet)
    {
        using var stream = new MemoryStream();
        stream.Write(Domain);
        WriteInt32(stream, manifest.SchemaVersion);
        WriteString(stream, PluginIds.Canonicalize(manifest.Id));
        WriteString(stream, manifest.Version!);
        WriteString(stream, dotnet.MinHostVersion);
        WriteString(stream, dotnet.EntryAssembly);
        WriteString(stream, dotnet.EntryType);
        WriteInt32(stream, dotnet.ExportedApiAssemblies.Count);
        foreach (var assembly in dotnet.ExportedApiAssemblies)
            WriteString(stream, assembly);

        var dependencies = manifest.Dependencies
            .Select(static dependency => (
                Id: PluginIds.Canonicalize(dependency.Key),
                Version: dependency.Value))
            .OrderBy(static dependency => dependency.Id, StringComparer.Ordinal)
            .ToArray();
        WriteInt32(stream, dependencies.Length);
        foreach (var dependency in dependencies)
        {
            WriteString(stream, dependency.Id);
            WriteString(stream, dependency.Version);
        }
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
