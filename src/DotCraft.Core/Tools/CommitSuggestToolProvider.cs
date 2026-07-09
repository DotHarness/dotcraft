using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.GeneratedTools.Core;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Single-tool profile for ephemeral source-control summary suggestion threads.
/// </summary>
public sealed class CommitSuggestToolProvider : IAgentToolProvider
{
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return GeneratedToolFunctions.CommitSuggestMethods_CommitSuggest();
    }
}

/// <summary>
/// Tool invoked by the model to submit the suggested source-control summary.
/// </summary>
public static class CommitSuggestMethods
{
    public const string ToolName = "CommitSuggest";

    [GeneratedTool]
    [Description(
        "Submit the suggested source-control summary. Call once with a concise summary line and an optional body.")]
    public static string CommitSuggest(
        [Description("Short subject or description line, ~72 characters or less.")] string summary,
        [Description("Optional body: bullet points or paragraphs separated by newlines.")] string? body = null)
    {
        _ = summary;
        _ = body;
        return "Recorded.";
    }
}
