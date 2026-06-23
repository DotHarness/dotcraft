namespace DotCraft.Plugins;

/// <summary>
/// Installs a plugin from a user-selected local directory into a workspace's
/// <c>.craft/plugins</c> directory as a user-owned plugin. Unlike
/// <see cref="BuiltInPluginDeployer"/>, the copy carries no <c>.builtin</c> marker,
/// so the plugin is discovered as a removable workspace plugin.
/// </summary>
public sealed class LocalPluginInstaller(string workspacePluginsPath)
{
    private static readonly Lock InstallLock = new();

    /// <summary>
    /// Validates the plugin directory at <paramref name="sourcePath"/> and, when valid,
    /// copies it into the workspace plugins directory. On success the result carries the
    /// installed plugin id; on failure it carries diagnostics with at least one error and
    /// nothing is written.
    /// </summary>
    public LocalPluginInstallResult Install(string sourcePath)
    {
        var diagnostics = new List<PluginDiagnostic>();

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "LocalPluginPathRequired",
                "A plugin folder path is required."));
            return new LocalPluginInstallResult(null, diagnostics);
        }

        var candidateSource = sourcePath.Trim();
        if (!Path.IsPathFullyQualified(candidateSource))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "LocalPluginPathNotAbsolute",
                "The plugin folder path must be an absolute path.",
                path: candidateSource));
            return new LocalPluginInstallResult(null, diagnostics);
        }

        var fullSource = Path.GetFullPath(candidateSource);
        if (!Directory.Exists(fullSource))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "LocalPluginPathMissing",
                "The selected plugin folder does not exist.",
                path: fullSource));
            return new LocalPluginInstallResult(null, diagnostics);
        }

        if (TryFindReparsePoint(fullSource, out var linkPath))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "LocalPluginPathContainsLink",
                "The selected plugin folder cannot contain symbolic links or reparse points.",
                path: linkPath));
            return new LocalPluginInstallResult(null, diagnostics);
        }

        if (!PluginManifestParser.IsValidPluginRoot(fullSource))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "LocalPluginManifestMissing",
                "The selected folder is not a plugin: it has no .craft-plugin/plugin.json.",
                path: fullSource));
            return new LocalPluginInstallResult(null, diagnostics);
        }

        var parse = PluginManifestParser.Load(fullSource);
        diagnostics.AddRange(parse.Diagnostics);
        if (parse.Manifest == null)
        {
            if (diagnostics.All(d => d.Severity != PluginDiagnosticSeverity.Error))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "LocalPluginManifestInvalid",
                    "The plugin manifest is invalid.",
                    path: fullSource));
            }

            return new LocalPluginInstallResult(null, diagnostics);
        }

        var pluginId = PluginIds.Canonicalize(parse.Manifest.Id);
        var fullTarget = Path.GetFullPath(Path.Combine(workspacePluginsPath, pluginId));

        lock (InstallLock)
        {
            if (Directory.Exists(fullTarget))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "LocalPluginAlreadyInstalled",
                    $"A plugin with id '{pluginId}' is already installed. Remove it before reinstalling.",
                    pluginId,
                    path: fullTarget));
                return new LocalPluginInstallResult(null, diagnostics);
            }

            // Refuse to copy a directory onto itself or into one of its own descendants.
            if (PathsEqual(fullSource, fullTarget) || IsWithin(fullTarget, fullSource))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "LocalPluginPathInvalid",
                    "The plugin folder cannot be installed onto itself.",
                    pluginId,
                    path: fullSource));
                return new LocalPluginInstallResult(null, diagnostics);
            }

            if (TryFindReparsePoint(fullSource, out linkPath))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "LocalPluginPathContainsLink",
                    "The selected plugin folder cannot contain symbolic links or reparse points.",
                    pluginId,
                    path: linkPath));
                return new LocalPluginInstallResult(null, diagnostics);
            }

            Directory.CreateDirectory(workspacePluginsPath);
            CopyDirectoryAtomic(fullSource, fullTarget);
        }

        return new LocalPluginInstallResult(pluginId, diagnostics);
    }

    private static void CopyDirectoryAtomic(string sourceRoot, string targetRoot)
    {
        var parent = Path.GetDirectoryName(targetRoot)!;
        var tempRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyDirectory(sourceRoot, tempRoot);
            if (Directory.Exists(targetRoot))
                Directory.Delete(targetRoot, recursive: true);
            Directory.Move(tempRoot, targetRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            // Local installs are user-owned; never carry over a built-in source marker.
            if (string.Equals(Path.GetFileName(file), BuiltInPluginDeployer.MarkerFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var targetFile = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static bool TryFindReparsePoint(string root, out string path)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                path = directory.FullName;
                return true;
            }

            foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    path = entry.FullName;
                    return true;
                }

                if (entry is DirectoryInfo childDirectory)
                    pending.Push(childDirectory);
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && relative != "."
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

/// <summary>
/// Outcome of a <see cref="LocalPluginInstaller.Install"/> call: the installed plugin id
/// (null when validation failed) plus any diagnostics produced while validating or copying.
/// </summary>
public sealed record LocalPluginInstallResult(string? PluginId, IReadOnlyList<PluginDiagnostic> Diagnostics);
