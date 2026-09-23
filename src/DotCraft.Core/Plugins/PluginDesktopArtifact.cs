using System.IO.Compression;
using System.Text.Json;

namespace DotCraft.Plugins;

/// <summary>A bounded, immutable Desktop-only export of an installed plugin.</summary>
public sealed class PluginDesktopArtifact : IDisposable
{
    private readonly string _root;
    private readonly FileStream _stream;

    private PluginDesktopArtifact(string root, FileStream stream)
    {
        _root = root;
        _stream = stream;
    }

    public long Length => _stream.Length;

    public static PluginDesktopArtifact Create(PluginManifest manifest, string revision)
    {
        var desktop = manifest.Desktop ?? throw new InvalidOperationException("Plugin has no Desktop contribution.");
        if (desktop.Revision != revision)
            throw new InvalidOperationException("Desktop plugin revision changed.");
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-desktop-artifact-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bundle = Path.Combine(root, "bundle");
            var dist = Path.Combine(bundle, "desktop", "dist");
            var copiedRevision = PluginDesktopRevision.Copy(manifest.RootPath, dist, desktop.Entry, desktop.Styles);
            if (copiedRevision != revision)
                throw new IOException("Desktop plugin changed while its artifact was being created.");
            Directory.CreateDirectory(Path.Combine(bundle, ".craft-plugin"));
            File.WriteAllText(Path.Combine(bundle, ".craft-plugin", "plugin.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = manifest.SchemaVersion,
                id = manifest.Id,
                displayName = manifest.Id,
                version = manifest.Version,
                desktop = new { entry = desktop.Entry, styles = desktop.Styles }
            }));
            var archivePath = Path.Combine(root, "desktop.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var directory in Directory.EnumerateDirectories(bundle, "*", SearchOption.AllDirectories)
                             .Order(StringComparer.Ordinal))
                    archive.CreateEntry(Path.GetRelativePath(bundle, directory).Replace('\\', '/') + "/");
                foreach (var file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories)
                             .Order(StringComparer.Ordinal))
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(bundle, file).Replace('\\', '/'), CompressionLevel.Fastest);
            }
            Directory.Delete(bundle, recursive: true);
            return new PluginDesktopArtifact(root, File.OpenRead(archivePath));
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(long offset, CancellationToken ct)
    {
        if (offset < 0 || offset >= Length) throw new ArgumentOutOfRangeException(nameof(offset));
        _stream.Position = offset;
        var bytes = new byte[(int)Math.Min(1024 * 1024, Length - offset)];
        await _stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }

    public void Dispose()
    {
        _stream.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
