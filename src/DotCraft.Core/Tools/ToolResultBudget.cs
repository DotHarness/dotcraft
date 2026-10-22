using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

public static class ToolResultBudget
{
    public const int MaxCharsPerRound = 200_000;

    public static void Apply(
        IList<AIContent> contents,
        string workspacePath,
        string dataPath,
        string? sessionId,
        int previewLines)
    {
        var texts = contents
            .Select(static (content, index) => (Result: content as FunctionResultContent, Text: SpillableText(content), Index: index))
            .Where(static entry => entry.Text != null)
            .ToList();
        var total = texts.Sum(static entry => entry.Text!.Length);
        if (total <= MaxCharsPerRound)
            return;

        var largestFirst = texts
            .Where(static entry => !entry.Text!.Contains("full output at:", StringComparison.Ordinal))
            .OrderByDescending(static entry => entry.Text!.Length);
        foreach (var (result, text, index) in largestFirst)
        {
            if (total <= MaxCharsPerRound)
                return;

            var path = ToolResultProcessor.SpillToDisk(text!, workspacePath, dataPath, sessionId, "tool", result!.CallId);
            var preview = ToolResultProcessor.BuildPreview(text!, previewLines, path, ToolResultProcessor.SpillPreviewMaxChars);
            contents[index] = new FunctionResultContent(result.CallId, preview)
            {
                Exception = result.Exception,
                AdditionalProperties = result.AdditionalProperties,
                RawRepresentation = result.RawRepresentation
            };
            total -= text!.Length - preview.Length;
        }
    }

    private static string? SpillableText(AIContent content) => content switch
    {
        FunctionResultContent { Result: string text } => text,
        FunctionResultContent { Result: IEnumerable<AIContent> items } when items.Any() && items.All(static item => item is TextContent) =>
            string.Join('\n', items.Cast<TextContent>().Select(static item => item.Text)),
        _ => null
    };
}
