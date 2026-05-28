using System.ComponentModel;
using System.Text;

namespace DotCraft.Tools;

/// <summary>
/// Provides the <c>SearchTools</c> AI function that allows the model to discover
/// deferred MCP tools on demand. Matching tools are immediately activated so that
/// they appear in subsequent LLM calls and can be invoked directly.
/// </summary>
public sealed class ToolSearchTool(DeferredToolRegistry registry, int maxSearchResults = 5)
{
    /// <summary>
    /// Search for available MCP tools by keyword. Call this when you need a
    /// tool that is not in your current tool list. Returns the matching tool
    /// names and descriptions. After calling this, the matched tools become
    /// available for use in subsequent calls.
    /// </summary>
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SearchTools))]
    [Description(
        "Search for available MCP tools by keyword. " +
        "Call this when you need a tool that is not in your current tool list. " +
        "Returns matching tool names and descriptions. " +
        "After calling this, the matched tools become available for use in subsequent calls.")]
    public string SearchTools(
        [Description("Keywords to search for, e.g. 'github pull request', 'slack message', 'database query'")]
        string query,
        [Description("Maximum number of tools to return. Default: 5")]
        int maxResults = 5)
    {
        // Clamp model-supplied maxResults to the configured upper bound.
        var results = registry.SearchAndActivate(query, Math.Min(maxResults, maxSearchResults));

        if (results.Count == 0)
            return "No matching tools found. Try different keywords.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} matching tool(s) — they are now available:");
        foreach (var r in results)
        {
            sb.Append("- **");
            sb.Append(r.Name);
            sb.Append("**");
            if (!string.IsNullOrWhiteSpace(r.Description))
            {
                sb.Append(": ");
                sb.Append(r.Description);
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.Append("You can call these tools directly in your next action.");

        return sb.ToString();
    }
}
