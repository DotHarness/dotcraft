using DotCraft.Sessions;

namespace DotCraft.Plugins;

internal static class PluginDirectoryDeleter
{
    internal static void Delete(string pluginRoot)
    {
        if (!Directory.Exists(pluginRoot))
            return;

        ClearReadOnlyAttributes(pluginRoot);
        Directory.Delete(pluginRoot, recursive: true);
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
