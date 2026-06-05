using System.Text.Json;

namespace DotCraft.Hub;

internal sealed class HubRuntimeToolsStore(string path)
{
    public HubRuntimeToolsRequest Load()
    {
        try
        {
            if (!File.Exists(path))
                return new HubRuntimeToolsRequest();

            return JsonSerializer.Deserialize<HubRuntimeToolsDocument>(
                    File.ReadAllText(path),
                    HubJson.Options)
                ?.RuntimeTools
                ?? new HubRuntimeToolsRequest();
        }
        catch
        {
            return new HubRuntimeToolsRequest();
        }
    }

    public HubRuntimeToolsRequest MergeAndSave(HubRuntimeToolsRequest? update)
    {
        var current = Load();
        var merged = Merge(current, update);
        Save(merged);
        return merged;
    }

    public void Save(HubRuntimeToolsRequest tools)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + "." + Environment.ProcessId + ".tmp";
        var doc = new HubRuntimeToolsDocument(1, DateTimeOffset.UtcNow, tools);
        File.WriteAllText(tempPath, JsonSerializer.Serialize(doc, HubJson.Options));
        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    public static HubRuntimeToolsRequest Merge(HubRuntimeToolsRequest current, HubRuntimeToolsRequest? update)
    {
        if (update == null)
            return current;

        return new HubRuntimeToolsRequest
        {
            RipgrepPath = ExistingFileOrDirectory(update.RipgrepPath) ?? current.RipgrepPath,
            NodeBin = ExistingFile(update.NodeBin) ?? current.NodeBin,
            NodeRunAsNode = update.NodeRunAsNode ?? current.NodeRunAsNode,
            ModulesDir = ExistingDirectory(update.ModulesDir) ?? current.ModulesDir,
            BuiltInPluginRoots = ExistingDirectoryPathList(update.BuiltInPluginRoots) ?? current.BuiltInPluginRoots,
            BuiltInPluginCatalogs = ExistingFilePathList(update.BuiltInPluginCatalogs) ?? current.BuiltInPluginCatalogs
        };
    }

    private static string? ExistingFile(string? pathValue)
    {
        var normalized = Normalize(pathValue);
        return normalized != null && File.Exists(normalized) ? normalized : null;
    }

    private static string? ExistingDirectory(string? pathValue)
    {
        var normalized = Normalize(pathValue);
        return normalized != null && Directory.Exists(normalized) ? normalized : null;
    }

    private static string? ExistingFileOrDirectory(string? pathValue)
    {
        var normalized = Normalize(pathValue);
        return normalized != null && (File.Exists(normalized) || Directory.Exists(normalized))
            ? normalized
            : null;
    }

    private static string? ExistingDirectoryPathList(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        var roots = pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(path => path != null && Directory.Exists(path))
            .Select(path => path!)
            .ToArray();

        return roots.Length == 0 ? null : string.Join(Path.PathSeparator, roots);
    }

    private static string? ExistingFilePathList(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        var roots = pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(path => path != null && File.Exists(path))
            .Select(path => path!)
            .ToArray();

        return roots.Length == 0 ? null : string.Join(Path.PathSeparator, roots);
    }

    private static string? Normalize(string? pathValue) =>
        string.IsNullOrWhiteSpace(pathValue) ? null : Path.GetFullPath(pathValue.Trim());
}

internal sealed record HubRuntimeToolsDocument(
    int Version,
    DateTimeOffset UpdatedAt,
    HubRuntimeToolsRequest RuntimeTools);
