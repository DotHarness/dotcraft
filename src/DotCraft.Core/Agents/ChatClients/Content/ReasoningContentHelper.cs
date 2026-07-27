using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Helper methods for extracting and formatting provider reasoning output.
/// </summary>
public static class ReasoningContentHelper
{
    /// <summary>
    /// Enumerates user-displayable reasoning text parts from streamed AI contents.
    /// </summary>
    public static IEnumerable<string> EnumerateTexts(IEnumerable<AIContent> contents)
    {
        foreach (var content in contents.OfType<TextReasoningContent>())
        {
            if (TryGetText(content, out var text))
                yield return text;
        }
    }

    /// <summary>
    /// Extracts provider reasoning text when it is safe to display.
    /// </summary>
    public static bool TryGetText(TextReasoningContent content, [NotNullWhen(true)] out string? text)
    {
        text = content.Text;
        return !string.IsNullOrEmpty(text);
    }

    /// <summary>
    /// Formats a reasoning chunk for plain text channels.
    /// </summary>
    public static string FormatBlock(string text)
    {
        return $"💭\n{text}";
    }

    /// <summary>
    /// Appends a reasoning block to a text buffer with spacing.
    /// </summary>
    public static void AppendBlock(StringBuilder buffer, string text)
    {
        if (buffer.Length > 0)
            buffer.AppendLine().AppendLine();

        buffer.Append(FormatBlock(text));
    }

    /// <summary>
    /// Produces a single-line preview for console diagnostics.
    /// </summary>
    public static string ToInlinePreview(string text, int maxLength = 200)
    {
        var normalized = text.Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return normalized.Length > maxLength
            ? normalized[..maxLength] + "..."
            : normalized;
    }

}
