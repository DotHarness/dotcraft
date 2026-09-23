namespace DotCraft.Plugins;

public static class PluginDirectoryDeleter
{
    public static void Delete(string pluginRoot, string workspaceTempPath)
    {
        if (!Directory.Exists(pluginRoot))
            return;

        Directory.CreateDirectory(workspaceTempPath);
        var tombstone = Path.Combine(
            workspaceTempPath,
            $"plugin-remove.{Path.GetFileName(pluginRoot)}.{Guid.NewGuid():N}.removed");

        // The same-volume rename is the removal commit; tombstone cleanup is not part of it.
        Directory.Move(pluginRoot, tombstone);
        try
        {
            ClearReadOnlyAttributes(tombstone);
            Directory.Delete(tombstone, recursive: true);
        }
        catch
        {
            // A stale tombstone is outside plugin discovery and can be cleaned later.
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            ClearReadOnlyAttribute(directory);

            foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                ClearReadOnlyAttribute(entry);

                if (entry is DirectoryInfo childDirectory
                    && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(childDirectory);
                }
            }
        }
    }

    private static void ClearReadOnlyAttribute(FileSystemInfo entry)
    {
        var attributes = entry.Attributes;
        if (!attributes.HasFlag(FileAttributes.ReadOnly))
            return;

        entry.Attributes = attributes & ~FileAttributes.ReadOnly;
    }
}
