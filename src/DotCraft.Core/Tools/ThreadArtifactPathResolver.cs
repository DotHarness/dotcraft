using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.Tools;

/// <summary>
/// Resolves current-protocol thread artifact paths. Artifact directories are owned by the
/// canonical thread identity; no legacy sanitized-path lookup is supported.
/// </summary>
public static class ThreadArtifactPathResolver
{
    public const string ToolResultsDirectoryName = "tool-results";

    public static string GetToolResultsRoot(string workspacePath, string dataPath)
        => CombineUnderWorkspace(workspacePath, dataPath, ToolResultsDirectoryName);

    public static string GetToolResultsThreadDirectory(string workspacePath, string dataPath, string? threadId)
        => CombineUnderWorkspace(workspacePath, dataPath, ToolResultsDirectoryName, GetCanonicalThreadSegment(threadId));

    public static string GetToolResultPath(string workspacePath, string dataPath, string? threadId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
            throw new ArgumentException("A valid artifact file name is required.", nameof(fileName));
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Artifact file names must not contain directory separators.", nameof(fileName));

        return CombineUnderWorkspace(workspacePath, dataPath, ToolResultsDirectoryName, GetCanonicalThreadSegment(threadId), fileName);
    }

    public static string GetToolResultRelativePath(string workspacePath, string dataPath, string? threadId, string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Artifact file names must not contain directory separators.", nameof(fileName));
        var absolutePath = GetToolResultPath(workspacePath, dataPath, threadId, fileName);
        return Path.GetRelativePath(Path.GetFullPath(workspacePath), absolutePath).Replace('\\', '/');
    }

    /// <summary>Returns a collision-resistant directory segment for the current thread identity.</summary>
    public static string GetCanonicalThreadSegment(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return "_unsession";

        var value = threadId.Trim();
        if (value is "." or ".."
            || value.Length > 80
            || value.EndsWith(' ') || value.EndsWith('.')
            || Regex.IsMatch(value, @"[^A-Za-z0-9_.-]"))
        {
            return "thread-" + Sha256(value);
        }

        return value;
    }

    /// <summary>Deletes one current-protocol thread artifact directory, idempotently.</summary>
    public static ArtifactCleanupResult DeleteToolResultsThreadDirectory(string workspacePath, string dataPath, string? threadId)
        => DeleteDirectory(GetToolResultsThreadDirectory(workspacePath, dataPath, threadId));

    private static ArtifactCleanupResult DeleteDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return ArtifactCleanupResult.Empty;

            Directory.Delete(directory, recursive: true);
            return new ArtifactCleanupResult(1, 0, Array.Empty<string>());
        }
        catch (DirectoryNotFoundException)
        {
            return ArtifactCleanupResult.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ArtifactCleanupResult(0, 1, new[] { ex.Message });
        }
    }

    private static string CombineUnderWorkspace(string workspacePath, string dataPath, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("A workspace path is required.", nameof(workspacePath));

        var workspace = Path.GetFullPath(workspacePath);
        var data = Path.IsPathRooted(dataPath) ? Path.GetFullPath(dataPath) : Path.GetFullPath(Path.Combine(workspace, dataPath));
        var path = Path.GetFullPath(Path.Combine(new[] { data }.Concat(segments).ToArray()));
        var root = workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Artifact path escaped the workspace root.");
        return path;
    }

    private static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public readonly record struct ArtifactCleanupResult(int DirectoriesDeleted, int Errors, IReadOnlyList<string> ErrorMessages)
{
    public static ArtifactCleanupResult Empty { get; } = new(0, 0, Array.Empty<string>());
}
