namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// Owns the on-disk layout of materialized marketplaces under the user-global craft home.
/// </summary>
internal static class MarketplaceStore
{
    public const string InstalledMarketplacesDirectory = "marketplaces";
    private const string StagingDirectoryName = ".staging";

    /// <summary>Root that holds one directory per materialized marketplace.</summary>
    public static string InstallRoot(string craftHome) =>
        Path.Combine(craftHome, InstalledMarketplacesDirectory);

    /// <summary>Resolves the installed root for a marketplace name, rejecting anything that escapes it.</summary>
    public static string ResolveRoot(string craftHome, string marketplaceName)
    {
        var installRoot = Path.GetFullPath(InstallRoot(craftHome));
        var resolved = Path.GetFullPath(Path.Combine(installRoot, SafeDirectoryName(marketplaceName)));
        if (!IsPathWithin(resolved, installRoot) || string.Equals(resolved, installRoot, StringComparison.Ordinal))
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.SourceInvalid,
                $"Marketplace '{marketplaceName}' does not resolve to a directory inside the installed marketplace root.");
        }

        return resolved;
    }

    /// <summary>Maps a marketplace name onto a directory name that is safe on every host.</summary>
    public static string SafeDirectoryName(string marketplaceName)
    {
        var mapped = new string(marketplaceName
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')
            .ToArray())
            .Trim('.');

        if (string.IsNullOrEmpty(mapped) || mapped == ".." || mapped.All(ch => ch == '-'))
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.SourceInvalid,
                $"Marketplace name '{marketplaceName}' cannot be used as a directory name.");
        }

        return mapped;
    }

    /// <summary>Creates a fresh empty staging directory inside the installed marketplace root.</summary>
    public static string CreateStagingDirectory(string craftHome)
    {
        var installRoot = InstallRoot(craftHome);
        var staging = Path.Combine(installRoot, $"{StagingDirectoryName}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        return staging;
    }

    /// <summary>
    /// Replaces <paramref name="destination"/> with <paramref name="stagedRoot"/>. The previous
    /// content is moved aside first so a failure cannot leave the destination half written.
    /// </summary>
    public static void ReplaceRoot(string stagedRoot, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var backup = $"{destination}.{Guid.NewGuid():N}.old";
        var hasBackup = Directory.Exists(destination);
        if (hasBackup)
            Directory.Move(destination, backup);

        try
        {
            Directory.Move(stagedRoot, destination);
        }
        catch
        {
            if (hasBackup && !Directory.Exists(destination))
                Directory.Move(backup, destination);
            throw;
        }

        if (hasBackup)
            TryDeleteDirectory(backup);
    }

    /// <summary>Deletes a directory tree, ignoring failures on already-removed content.</summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover directories are reported through diagnostics rather than failing the operation.
        }
    }

    /// <summary>Removes staging directories left behind by an interrupted fetch.</summary>
    public static void CleanStagingDirectories(string craftHome)
    {
        var installRoot = InstallRoot(craftHome);
        if (!Directory.Exists(installRoot))
            return;

        foreach (var directory in Directory.GetDirectories(installRoot, $"{StagingDirectoryName}.*"))
            TryDeleteDirectory(directory);
    }

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
