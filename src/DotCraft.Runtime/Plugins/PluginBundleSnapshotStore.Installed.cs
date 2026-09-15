using DotCraft.Plugins;

namespace DotCraft.Runtime;

internal sealed partial class PluginBundleSnapshotStore
{
    private readonly string? _installedRoot;
    private readonly object _installedGate = new();
    private readonly Dictionary<string, int> _installedReferences = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    // A disconnected workspace may be recreated before its previous collectible context is collected.
    private static readonly Dictionary<string, List<WeakReference>> RetiredInstallations = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public PluginAcceptedSnapshot AcceptInstalled(string rootPath)
    {
        lock (_installedGate)
        {
            var root = Path.GetFullPath(rootPath);
            var parsed = PluginManifestParser.Load(root);
            if (parsed.Manifest?.Dotnet is null)
                throw new InvalidOperationException("The installed plugin identity is invalid.");
            var snapshot = new PluginAcceptedSnapshot(parsed.Manifest, root,
                PluginBundleFingerprint.Compute(root), PluginDotnetFingerprint.Compute(parsed.Manifest), parsed.Diagnostics);
            AddReference(root);
            return snapshot;
        }
    }

    private void AddReference(string path) =>
        _installedReferences[path] = _installedReferences.GetValueOrDefault(path) + 1;

    private void RemoveReference(string path)
    {
        if (_installedReferences[path] == 1) _installedReferences.Remove(path);
        else _installedReferences[path]--;
    }

    public void RetainUnloadedGeneration(PluginGenerationRemnant remnant)
    {
        if (_installedRoot is null || remnant.LoadContext is not { IsAlive: true } context) return;
        lock (RetiredInstallations)
        {
            if (!RetiredInstallations.TryGetValue(remnant.ShadowCopyPath, out var contexts))
                RetiredInstallations[remnant.ShadowCopyPath] = contexts = [];
            contexts.Add(context);
        }
    }

    private static bool HasRetiredGeneration(string path)
    {
        lock (RetiredInstallations)
        {
            if (!RetiredInstallations.TryGetValue(path, out var contexts)) return false;
            contexts.RemoveAll(context => !context.IsAlive);
            if (contexts.Count > 0) return true;
            RetiredInstallations.Remove(path);
            return false;
        }
    }

    public IEnumerable<string> PruneInstalled(IEnumerable<string> pluginIds)
    {
        if (_installedRoot is null || !Directory.Exists(_installedRoot)) return [];
        var pending = new List<string>();
        lock (_installedGate)
        {
            foreach (var pluginId in pluginIds)
            {
                var plugin = Path.Combine(_installedRoot, Sanitize(pluginId));
                if (!Directory.Exists(plugin)) continue;
                if ((File.GetAttributes(plugin) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var content in Directory.EnumerateDirectories(plugin))
                {
                    var name = Path.GetFileName(content);
                    if (name.Length != 64 || !name.All(char.IsAsciiHexDigit)
                        || (File.GetAttributes(content) & FileAttributes.ReparsePoint) != 0
                        || _installedReferences.ContainsKey(content) || HasRetiredGeneration(content)) continue;
                    if (!TryDeleteDirectory(content)) pending.Add(content);
                }
            }
        }
        return pending;
    }
}
