using System.Text.RegularExpressions;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Workspaces;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Visualizations;

/// <summary>Resolves, reads, and deletes thread-scoped visualization files.</summary>
public sealed partial class VisualizationAssetStore
{
    private readonly string _dataDirectoryName;

    public VisualizationAssetStore(DotCraftPaths paths)
    {
        _dataDirectoryName = DataDirectoryName(paths.Data.RootPath);
    }

    /// <summary>Returns the authoring directory owned by a thread.</summary>
    public string GetAuthoringDirectory(SessionThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ValidateThreadId(thread.Id);
        var directory = ResolveThreadDirectory(ResolveWorkspacePath(thread.WorkspacePath), _dataDirectoryName, thread.Id);
        RejectReparsePoints(directory);
        return directory;
    }

    /// <summary>Deletes the visualization directory of a thread without following reparse points.</summary>
    public static ArtifactCleanupResult DeleteThreadDirectory(string workspacePath, string dataPath, string threadId)
    {
        if (!ThreadIdRegex().IsMatch(threadId))
            return ArtifactCleanupResult.Empty;

        try
        {
            var directory = ResolveThreadDirectory(ResolveWorkspacePath(workspacePath), DataDirectoryName(dataPath), threadId);
            if (!Directory.Exists(directory))
                return ArtifactCleanupResult.Empty;
            RejectReparsePoints(directory);
            Directory.Delete(directory, recursive: true);
            return new ArtifactCleanupResult(1, 0, []);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or VisualizationException { Code: "not_found" })
        {
            return ArtifactCleanupResult.Empty;
        }
        catch (Exception ex) when (ex is VisualizationException or IOException or UnauthorizedAccessException)
        {
            return new ArtifactCleanupResult(0, 1, [ex.Message]);
        }
    }

    /// <summary>Reads the current file at a path referenced by a completed assistant item.</summary>
    public async Task<string> ReadReferencedFragmentAsync(
        SessionThread thread,
        SessionTurn turn,
        SessionItem item,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (turn.Status != TurnStatus.Completed || item.Status != ItemStatus.Completed
            || item.Type != ItemType.AgentMessage || item.AsAgentMessage is not { Text: { } text })
        {
            throw new VisualizationException(
                "not_referenced",
                "The visualization is not referenced by a completed assistant message.");
        }

        var requested = NormalizeReferencedPath(path);
        if (requested is null
            || !VisualizationContentReferenceParser.Parse(text)
                .Any(reference => NormalizeReferencedPath(reference.Path) is { } referenced && PathsEqual(referenced, requested)))
        {
            throw new VisualizationException(
                "not_referenced",
                "The visualization is not referenced by this assistant message.");
        }

        var directory = GetAuthoringDirectory(thread);
        if (!PathsEqual(Path.GetDirectoryName(requested) ?? string.Empty, directory))
            throw new VisualizationException("unsafe_path", "The visualization file path is unsafe.");
        RejectUnsafeFile(requested);

        return await File.ReadAllTextAsync(requested, cancellationToken);
    }

    private static string? NormalizeReferencedPath(string? path)
    {
        if (!VisualizationContentReferenceParser.IsValidPath(path) || !Path.IsPathFullyQualified(path!))
            return null;
        try
        {
            return Path.GetFullPath(path!);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ResolveThreadDirectory(string workspace, string dataDirectoryName, string threadId)
    {
        var root = Path.GetFullPath(Path.Combine(workspace, dataDirectoryName, "visualizations"));
        var directory = Path.GetFullPath(Path.Combine(root, threadId));
        EnsureWithinDirectory(directory, root);
        return directory;
    }

    private static string DataDirectoryName(string dataPath) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(dataPath));

    private static void EnsureWithinDirectory(string path, string directory)
    {
        var boundary = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(boundary + Path.DirectorySeparatorChar, comparison)
            && !path.StartsWith(boundary + Path.AltDirectorySeparatorChar, comparison))
        {
            throw new VisualizationException("unsafe_path", "The visualization file path is unsafe.");
        }
    }

    private static string ResolveWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Path.IsPathFullyQualified(workspacePath))
            throw new VisualizationException("unsafe_path", "The visualization workspace path is unsafe.");

        var workspace = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(workspace))
            throw new VisualizationException("not_found", "The visualization workspace is unavailable.");
        RejectReparsePoints(workspace);
        return workspace;
    }

    private static void ValidateThreadId(string threadId)
    {
        if (!ThreadIdRegex().IsMatch(threadId))
            throw new VisualizationException("unsafe_path", "The visualization thread path is unsafe.");
    }

    private static void RejectUnsafeFile(string path)
    {
        if (!File.Exists(path))
            throw new VisualizationException("not_found", "The visualization fragment is unavailable.");
        RejectReparsePoints(Path.GetDirectoryName(path)!);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new VisualizationException("unsafe_path", "The visualization file path is unsafe.");
    }

    private static void RejectReparsePoints(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new VisualizationException("unsafe_path", "The visualization directory path is unsafe.");
            current = current.Parent;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ThreadIdRegex();
}

public sealed class VisualizationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
