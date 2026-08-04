using System.Text.RegularExpressions;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.InlineVisualizations;

/// <summary>Resolves and safely reads thread-scoped inline visualization files.</summary>
public sealed partial class InlineVisualizationAssetStore
{
    /// <summary>Returns the authoring directory owned by a thread.</summary>
    public string GetAuthoringDirectory(SessionThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var workspace = ResolveWorkspacePath(thread.WorkspacePath);
        ValidateThreadId(thread.Id);
        var root = Path.GetFullPath(Path.Combine(workspace, ".craft", "visualizations"));
        var directory = Path.GetFullPath(Path.Combine(root, thread.Id));
        EnsureWithinDirectory(directory, root);
        RejectReparsePoints(directory);
        return directory;
    }

    /// <summary>Creates and returns the authoring directory owned by a thread.</summary>
    public string EnsureAuthoringDirectory(SessionThread thread)
    {
        var directory = GetAuthoringDirectory(thread);
        Directory.CreateDirectory(directory);
        RejectReparsePoints(directory);
        return directory;
    }

    /// <summary>Reads the current file referenced by a completed assistant item.</summary>
    public async Task<string> ReadReferencedFragmentAsync(
        SessionThread thread,
        SessionTurn turn,
        SessionItem item,
        string file,
        CancellationToken cancellationToken = default)
    {
        if (turn.Status != TurnStatus.Completed || item.Status != ItemStatus.Completed
            || item.Type != ItemType.AgentMessage || item.AsAgentMessage is not { Text: { } text })
        {
            throw new InlineVisualizationException(
                "not_referenced",
                "The visualization is not referenced by a completed assistant message.");
        }

        if (!InlineVisualizationDirectiveParser.IsValidFileName(file)
            || !InlineVisualizationDirectiveParser.Parse(text).Any(d => d.File == file))
        {
            throw new InlineVisualizationException(
                "not_referenced",
                "The visualization is not referenced by this assistant message.");
        }

        var directory = GetAuthoringDirectory(thread);
        RejectReparsePoints(directory);
        var path = Path.GetFullPath(Path.Combine(directory, file));
        EnsureWithinDirectory(path, directory);
        RejectUnsafeFile(path);

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

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
            throw new InlineVisualizationException("unsafe_path", "The visualization file path is unsafe.");
        }
    }

    private static string ResolveWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Path.IsPathFullyQualified(workspacePath))
            throw new InlineVisualizationException("unsafe_path", "The visualization workspace path is unsafe.");

        var workspace = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(workspace))
            throw new InlineVisualizationException("not_found", "The visualization workspace is unavailable.");
        RejectReparsePoints(workspace);
        return workspace;
    }

    private static void ValidateThreadId(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || !ThreadIdRegex().IsMatch(threadId))
            throw new InlineVisualizationException("unsafe_path", "The visualization thread path is unsafe.");
    }

    private static void RejectUnsafeFile(string path)
    {
        if (!File.Exists(path))
            throw new InlineVisualizationException("not_found", "The visualization fragment is unavailable.");
        RejectReparsePoints(Path.GetDirectoryName(path)!);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InlineVisualizationException("unsafe_path", "The visualization file path is unsafe.");
    }

    private static void RejectReparsePoints(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InlineVisualizationException("unsafe_path", "The visualization directory path is unsafe.");
            current = current.Parent;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ThreadIdRegex();
}

public sealed class InlineVisualizationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
