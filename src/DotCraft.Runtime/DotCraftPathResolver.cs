using DotCraft.Workspaces;

namespace DotCraft.Runtime;

internal static class DotCraftPathResolver
{
    internal static DotCraftPaths Resolve(DotCraftRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataPath);

        var workspacePath = Normalize(options.WorkspacePath);
        var dataPath = Normalize(Path.IsPathRooted(options.DataPath)
            ? options.DataPath
            : Path.Combine(workspacePath, options.DataPath));
        EnsureDirectChild(workspacePath, dataPath);
        EnsureExistingLinkDoesNotEscape(workspacePath, dataPath);

        var userDataPath = string.IsNullOrWhiteSpace(options.UserDataPath)
            ? null
            : Normalize(options.UserDataPath);
        return new DotCraftPaths(workspacePath, dataPath, userDataPath);
    }

    private static void EnsureDirectChild(string workspacePath, string dataPath)
    {
        var relative = Path.GetRelativePath(workspacePath, dataPath);
        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "DataPath must identify a direct child directory of WorkspacePath.",
                nameof(DotCraftRuntimeOptions.DataPath));
        }
    }

    private static void EnsureExistingLinkDoesNotEscape(string workspacePath, string dataPath)
    {
        var dataInfo = new DirectoryInfo(dataPath);
        if (!dataInfo.Exists || (dataInfo.Attributes & FileAttributes.ReparsePoint) == 0)
            return;

        var target = dataInfo.ResolveLinkTarget(returnFinalTarget: true);
        if (target is null)
            return;

        var resolvedWorkspace = ResolveFinalDirectoryLink(workspacePath);
        var relative = Path.GetRelativePath(resolvedWorkspace, Normalize(target.FullName));
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "DataPath must not link to a directory outside WorkspacePath.",
                nameof(DotCraftRuntimeOptions.DataPath));
        }
    }

    private static string ResolveFinalDirectoryLink(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0
            ? Normalize(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path)
            : Normalize(path);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
