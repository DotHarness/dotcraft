namespace DotCraft.Context.Compaction;

/// <summary>
/// Built-in tool names whose results may be cleared by the microcompact pass.
/// Kept next to the compaction code (instead of each tool file) so renaming a
/// tool surfaces immediately via the <c>MicroCompactorTests</c> assertion set.
/// MCP tools are additionally matched by the <see cref="McpToolPrefix"/>.
/// </summary>
public static class CompactableToolNames
{
    /// <summary>
    /// Prefix used by <see cref="DotCraft.Mcp"/> to namespace external tool
    /// names (e.g. <c>mcp__filesystem__read_file</c>). Any tool whose name
    /// starts with this prefix is considered compactable.
    /// </summary>
    public const string McpToolPrefix = "mcp__";

    /// <summary>
    /// Placeholder text substituted for a cleared tool-result payload.
    /// </summary>
    public const string ClearedResultMarker = "[Old tool result content cleared]";

    /// <summary>
    /// Built-in tool names whose output is typically noisy and safe to drop
    /// from mid-conversation once it is N calls old.
    /// </summary>
    public static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.Ordinal)
    {
        "ReadFile",
        "WriteFile",
        "EditFile",
        "Exec",
        "GrepFiles",
        "FindFiles",
        "WebFetch",
        "WebSearch",
    };

    /// <summary>
    /// True when <paramref name="toolName"/> is eligible for microcompact.
    /// </summary>
    public static bool IsCompactable(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return false;

        if (BuiltIn.Contains(toolName))
            return true;

        return toolName.StartsWith(McpToolPrefix, StringComparison.Ordinal);
    }
}
