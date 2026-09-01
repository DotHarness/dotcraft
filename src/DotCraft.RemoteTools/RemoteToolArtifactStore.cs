using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.RemoteTools;

internal static class RemoteToolArtifactStore
{
    private static readonly string[] ArtifactSegments = [".craft", "remote-tool-host", "artifacts"];

    public static RemoteMaterializedResult Materialize(
        string workspacePath,
        RemoteInvocationMeta invocation,
        string toolName,
        ToolExecutionResult result)
    {
        var limit = invocation.MaxResultChars <= 0
            ? RemoteToolHostProtocol.MaxTransportResultChars
            : Math.Min(invocation.MaxResultChars, RemoteToolHostProtocol.MaxTransportResultChars);
        var content = SelectFullText(result);
        if (content is not { Length: > 0 } || content.Length <= limit)
            return new RemoteMaterializedResult(result, null);

        try
        {
            var leaseDataPath = GetLeaseDataPath(workspacePath, invocation.LeaseId);
            EnsureWorkspaceBound(workspacePath, leaseDataPath);
            Directory.CreateDirectory(leaseDataPath);
            EnsureWorkspaceBound(workspacePath, leaseDataPath);

            var relativePath = ToolResultProcessor.SpillToDisk(
                content,
                workspacePath,
                leaseDataPath,
                invocation.ThreadId,
                toolName,
                invocation.InvocationId);
            var preview = BuildBoundedPreview(
                content,
                Math.Clamp(invocation.SpillPreviewLines, 1, 500),
                relativePath,
                limit);
            var materialized = new ToolExecutionResult(
                result.Success,
                preview,
                result.StructuredContent,
                result.Meta,
                result.RawSourceResult,
                result.Error,
                result.ProviderResult,
                ReplaceTextContent(result.ContentItems, preview),
                result.Directive);
            return new RemoteMaterializedResult(
                materialized,
                new RemoteToolArtifactMeta(relativePath, content.Length));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or InvalidOperationException)
        {
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.RemoteResultMaterializationFailed,
                "The Remote Tool Host could not store the oversized tool result in the leased workspace.",
                invocation.InvocationId,
                ex);
        }
    }

    public static bool CleanupLeaseArtifacts(string workspacePath, string leaseId) =>
        DeleteArtifactDirectory(workspacePath, GetLeaseDataPath(workspacePath, leaseId));

    public static bool CleanupStaleArtifacts(string workspacePath) =>
        DeleteArtifactDirectory(workspacePath, GetArtifactsRoot(workspacePath));

    private static string GetArtifactsRoot(string workspacePath) =>
        Path.GetFullPath(Path.Combine(workspacePath, Path.Combine(ArtifactSegments)));

    private static string GetLeaseDataPath(string workspacePath, string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)
            || leaseId is "." or ".."
            || !string.Equals(Path.GetFileName(leaseId), leaseId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace lease identifier is invalid.");
        }

        return Path.GetFullPath(Path.Combine(GetArtifactsRoot(workspacePath), leaseId));
    }

    private static bool DeleteArtifactDirectory(string workspacePath, string directory)
    {
        try
        {
            EnsureWorkspaceBound(workspacePath, directory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void EnsureWorkspaceBound(string workspacePath, string path)
    {
        var guard = new FileAccessGuard(
            workspacePath,
            requireApprovalOutsideWorkspace: false,
            workspaceRoots: [workspacePath]);
        if (guard.RequiresOutsideWorkspaceApproval(path, "write"))
            throw new InvalidOperationException("Remote tool artifacts must remain inside the workspace.");
    }

    private static IReadOnlyList<AIContent>? ReplaceTextContent(
        IReadOnlyList<AIContent>? contentItems,
        string preview)
    {
        if (contentItems is not { Count: > 0 })
            return null;

        var result = new List<AIContent>(contentItems.Count + 1) { new TextContent(preview) };
        result.AddRange(contentItems.Where(item => item is not TextContent));
        return result;
    }

    private static string? SelectFullText(ToolExecutionResult result)
    {
        var richText = result.ContentItems is { Count: > 0 }
            ? string.Join(Environment.NewLine, result.ContentItems.OfType<TextContent>().Select(item => item.Text))
            : null;
        return richText is { Length: > 0 } && richText.Length > (result.Content?.Length ?? 0)
            ? richText
            : result.Content;
    }

    private static string BuildBoundedPreview(
        string content,
        int previewLines,
        string relativePath,
        int limit)
    {
        var preview = ToolResultProcessor.BuildPreview(
            content,
            previewLines,
            relativePath,
            Math.Max(1, limit - 512));
        if (preview.Length <= limit)
            return preview;

        var marker = $"\n\n... (full output at: {relativePath})";
        if (marker.Length >= limit)
            return marker.Length <= RemoteToolHostProtocol.MaxTransportResultChars
                ? marker
                : marker[..RemoteToolHostProtocol.MaxTransportResultChars];
        return content[..(limit - marker.Length)] + marker;
    }
}

internal sealed record RemoteMaterializedResult(
    ToolExecutionResult Result,
    RemoteToolArtifactMeta? Artifact);
