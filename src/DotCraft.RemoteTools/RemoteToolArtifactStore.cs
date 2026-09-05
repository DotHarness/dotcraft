using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.RemoteTools;

internal static class RemoteToolArtifactStore
{
    public static RemoteMaterializedResult Materialize(
        string artifactsRoot,
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
            var leaseDataPath = GetLeaseDataPath(artifactsRoot, invocation.LeaseId);
            EnsureArtifactBound(artifactsRoot, leaseDataPath);
            Directory.CreateDirectory(leaseDataPath);

            var artifactPath = Path.Combine(
                leaseDataPath,
                ToolResultProcessor.SpillToDisk(
                    content,
                    leaseDataPath,
                    leaseDataPath,
                    invocation.ThreadId,
                    toolName,
                    invocation.InvocationId).Replace('/', Path.DirectorySeparatorChar));
            var preview = BuildBoundedPreview(
                content,
                Math.Clamp(invocation.SpillPreviewLines, 1, 500),
                artifactPath,
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
                new RemoteToolArtifactMeta(artifactPath, content.Length));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or InvalidOperationException)
        {
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.RemoteResultMaterializationFailed,
                "The Remote Tool Host could not store the oversized tool result under its state directory.",
                invocation.InvocationId,
                ex);
        }
    }

    public static bool CleanupLeaseArtifacts(string artifactsRoot, string leaseId) =>
        DeleteArtifactDirectory(GetLeaseDataPath(artifactsRoot, leaseId));

    public static bool CleanupStaleArtifacts(string artifactsRoot) =>
        DeleteArtifactDirectory(Path.GetFullPath(artifactsRoot));

    private static string GetLeaseDataPath(string artifactsRoot, string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)
            || leaseId is "." or ".."
            || !string.Equals(Path.GetFileName(leaseId), leaseId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace lease identifier is invalid.");
        }

        return Path.GetFullPath(Path.Combine(artifactsRoot, leaseId));
    }

    /// <summary>Rejects a lease directory that leaves the artifact root through a link or reparse point.</summary>
    private static void EnsureArtifactBound(string artifactsRoot, string path)
    {
        var root = Path.GetFullPath(artifactsRoot);
        var guard = new FileAccessGuard(
            root,
            requireApprovalOutsideWorkspace: false,
            workspaceRoots: [root]);
        if (guard.RequiresOutsideWorkspaceApproval(path, "write"))
            throw new InvalidOperationException("Remote tool artifacts must remain inside the Host state root.");
    }

    private static bool DeleteArtifactDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
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
        string artifactPath,
        int limit)
    {
        var preview = ToolResultProcessor.BuildPreview(
            content,
            previewLines,
            artifactPath,
            Math.Max(1, limit - 512));
        if (preview.Length <= limit)
            return preview;

        var marker = $"\n\n... (full output at: {artifactPath})";
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
