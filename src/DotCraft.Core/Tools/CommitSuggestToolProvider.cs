using System.ComponentModel;
using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Single-tool profile for ephemeral commit-message suggestion threads.
/// </summary>
public sealed class CommitSuggestToolProvider : IAgentToolProvider
{
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        yield return AIFunctionFactory.Create(CommitSuggestMethods.CommitSuggest);
    }
}

/// <summary>
/// Tool invoked by the model to submit the suggested git commit message.
/// </summary>
public static class CommitSuggestMethods
{
    public const string ToolName = "CommitSuggest";

    [Description(
        "Submit the suggested git commit message. Call once with a concise summary line (Conventional Commits style) and an optional body.")]
    public static string CommitSuggest(
        [Description("Short subject line, ~72 characters or less.")] string summary,
        [Description("Optional body: bullet points or paragraphs separated by newlines.")] string? body = null)
    {
        _ = summary;
        _ = body;
        return "Recorded.";
    }
}
