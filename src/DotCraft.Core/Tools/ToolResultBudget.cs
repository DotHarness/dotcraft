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
        var total = contents.Sum(static content => content is FunctionResultContent { Result: string text } ? text.Length : 0);
        if (total <= MaxCharsPerRound)
            return;

        var largestFirst = contents
            .Select(static (content, index) => (Content: content as FunctionResultContent, Index: index))
            .Where(static entry => entry.Content is { Result: string text } && !text.Contains("full output at:", StringComparison.Ordinal))
            .OrderByDescending(static entry => ((string)entry.Content!.Result!).Length)
            .ToList();
        foreach (var (result, index) in largestFirst)
        {
            if (total <= MaxCharsPerRound)
                return;

            var text = (string)result!.Result!;
            var path = ToolResultProcessor.SpillToDisk(text, workspacePath, dataPath, sessionId, "tool", result.CallId);
            var preview = ToolResultProcessor.BuildPreview(text, previewLines, path, ToolResultProcessor.SpillPreviewMaxChars);
            contents[index] = new FunctionResultContent(result.CallId, preview)
            {
                Exception = result.Exception,
                AdditionalProperties = result.AdditionalProperties,
                RawRepresentation = result.RawRepresentation
            };
            total -= text.Length - preview.Length;
        }
    }
}
