using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>
/// Normalizes empty tool results, applies per-tool size limits, and spills oversized text to disk
/// under the configured workspace data directory with a head/tail preview.
/// </summary>
public static class ToolResultProcessor
{
    /// <summary>
    /// Marker substring included in spill previews so UIs can detect them.
    /// </summary>
    public const string SpillPreviewMarker = "lines omitted, full output at:";

    /// <summary>
    /// Returns the standardized empty-result message for a tool.
    /// </summary>
    public static string EmptyResultMessage(string toolName)
        => $"({toolName} completed with no output)";

    /// <summary>
    /// Converts a tool result to a string for length measurement and spill content.
    /// </summary>
    public static string ToStringForLimit(object? rawResult)
    {
        if (rawResult is string s)
            return s;
        return ImageContentSanitizingChatClient.DescribeResult(rawResult);
    }

    /// <summary>
    /// Whether the described tool output should be treated as empty for normalization.
    /// </summary>
    public static bool IsEffectivelyEmpty(string text)
        => string.IsNullOrWhiteSpace(text) || text == "(no output)";

    /// <summary>
    /// Applies empty normalization, optional size limiting with spill-to-disk, and returns the value
    /// to pass to the model (string or original object when unchanged).
    /// </summary>
    /// <param name="toolName">Tool function name.</param>
    /// <param name="rawResult">Raw return value from the tool.</param>
    /// <param name="maxResultChars">
    /// Maximum length of the string form before spill; <c>0</c> means unlimited (only empty normalization).
    /// </param>
    /// <param name="workspacePath">Workspace root.</param>
    /// <param name="dataPath">Validated workspace data directory.</param>
    /// <param name="sessionId">Session/thread id for the spill subdirectory, or null for <c>_unsession</c>.</param>
    /// <param name="previewLines">Head and tail line count for the preview.</param>
    public static object? Process(
        string toolName,
        object? rawResult,
        int maxResultChars,
        string workspacePath,
        string dataPath,
        string? sessionId,
        int previewLines,
        string? callId = null)
    {
        var text = ToStringForLimit(rawResult);
        if (IsEffectivelyEmpty(text))
            return EmptyResultMessage(toolName);
        if (maxResultChars <= 0 || text.Length <= maxResultChars)
            return rawResult;

        var relativePath = SpillToDisk(text, workspacePath, dataPath, sessionId, toolName, callId);
        return BuildPreview(text, previewLines, relativePath, maxResultChars);
    }

    /// <summary>
    /// Writes full text to disk and returns the workspace-relative path (forward slashes).
    /// </summary>
    public static string SpillToDisk(
        string text,
        string workspacePath,
        string dataPath,
        string? sessionId,
        string toolName,
        string? callId = null)
    {
        var spillDir = ThreadArtifactPathResolver.GetToolResultsThreadDirectory(workspacePath, dataPath, sessionId);
        Directory.CreateDirectory(spillDir);
        var fileName = $"{SafeFileSegment(toolName)}_{GetStableCallSegment(callId, text)}.txt";
        var absolutePath = ThreadArtifactPathResolver.GetToolResultPath(workspacePath, dataPath, sessionId, fileName);
        try
        {
            using var stream = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }
        catch (IOException) when (File.Exists(absolutePath))
        {
        }

        return ThreadArtifactPathResolver.GetToolResultRelativePath(workspacePath, dataPath, sessionId, fileName);
    }

    /// <summary>Deletes the current-protocol tool-result directory for a thread.</summary>
    public static ArtifactCleanupResult CleanupThreadArtifacts(string workspacePath, string dataPath, string? sessionId)
        => ThreadArtifactPathResolver.DeleteToolResultsThreadDirectory(workspacePath, dataPath, sessionId);

    /// <summary>
    /// Builds a head + tail preview with a reference to the spill file path.
    /// </summary>
    /// <param name="maxPreviewChars">
    /// Maximum characters of normalized body text in the preview (short form: one block; long form: head + tail excerpts combined).
    /// Excess is truncated. Use <see cref="int.MaxValue"/> for no cap.
    /// </param>
    public static string BuildPreview(
        string text,
        int previewLines,
        string spillRelativePath,
        int maxPreviewChars = int.MaxValue)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var totalLines = lines.Length;

        if (previewLines < 1)
            previewLines = 1;

        if (totalLines <= previewLines * 2)
        {
            var body = normalized.Length > maxPreviewChars ? normalized[..maxPreviewChars] : normalized;
            return body + "\n\n" + $"... (full output at: {spillRelativePath})";
        }

        var headText = string.Join("\n", lines[..previewLines]);
        var tailText = string.Join("\n", lines[^previewLines..]);
        TruncateHeadTailPreview(ref headText, ref tailText, maxPreviewChars);
        var omitted = totalLines - previewLines * 2;

        return headText
               + "\n\n... ("
               + omitted
               + " "
               + SpillPreviewMarker
               + " "
               + spillRelativePath
               + ") ...\n\n"
               + tailText;
    }

    /// <summary>
    /// Resolves the default maximum result length: per-tool attribute, then global config.
    /// A value of <c>0</c> means unlimited.
    /// </summary>
    public static int ResolveMaxResultChars(string toolName, int globalMaxToolResultChars)
    {
        var perTool = ToolRegistry.GetMaxResultChars(toolName);
        var limit = perTool ?? globalMaxToolResultChars;
        return limit <= 0 ? 0 : limit;
    }

    /// <summary>
    /// Caps head and tail excerpt length so their combined character count does not exceed
    /// <paramref name="maxPreviewChars"/> (marker line is outside this budget).
    /// </summary>
    private static void TruncateHeadTailPreview(ref string headText, ref string tailText, int maxPreviewChars)
    {
        var total = headText.Length + tailText.Length;
        if (maxPreviewChars >= int.MaxValue || total == 0 || total <= maxPreviewChars)
            return;

        var headLen = (int)((long)headText.Length * maxPreviewChars / total);
        var tailLen = maxPreviewChars - headLen;

        if (headText.Length > 0 && headLen == 0)
        {
            headLen = 1;
            tailLen = maxPreviewChars - headLen;
        }

        if (tailText.Length > 0 && tailLen == 0 && maxPreviewChars > headLen)
        {
            tailLen = 1;
            headLen = maxPreviewChars - tailLen;
        }

        headLen = Math.Min(headLen, headText.Length);
        tailLen = Math.Min(maxPreviewChars - headLen, tailText.Length);
        headText = headText[..headLen];
        tailText = tailText[^tailLen..];
    }

    private static string SafeFileSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "tool";

        var trimmed = value.Trim();
        if (trimmed is not "." and not ".." && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            return trimmed;

        return "tool-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
    }

    private static string GetStableCallSegment(string? callId, string text)
    {
        var value = string.IsNullOrWhiteSpace(callId) ? null : callId.Trim();
        if (value is not null && value is not "." and not ".." && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            return value;

        var hashInput = value is null ? text : value;
        return "call-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
    }
}
