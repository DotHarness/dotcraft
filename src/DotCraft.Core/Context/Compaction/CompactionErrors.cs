using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DotCraft.Context.Compaction;

internal static class CompactionErrors
{
    private static readonly string[] ExplicitMarkers =
    [
        "prompt_too_long",
        "context_length_exceeded",
        "context_length_error",
        "context_window_exceeded",
        "model_context_window_exceeded",
        "max_context_length_exceeded"
    ];

    private static readonly Regex[] OverflowPatterns =
    [
        new(@"prompt\s+is\s+too\s+long", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"prompt\s+too\s+long;\s+exceeded\s+(?:max\s+)?context\s+length", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"input\s+is\s+too\s+long\s+for\s+requested\s+model", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:input|request|messages?)\s+exceeds?\s+the\s+context\s+window", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"exceeds?\s+the\s+available\s+context\s+size", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"input\s+token\s+count.*exceeds?\s+the\s+maximum", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline),
        new(@"maximum\s+context\s+length\s+is\s+\d+\s+tokens?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"context\s+length\s+is\s+only\s+\d+\s+tokens?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"input\s+length.*exceeds?.*context\s+length", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline),
        new(@"greater\s+than\s+the\s+context\s+length", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"context\s+window\s+exceeds?\s+limit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"exceeded\s+model\s+token\s+limit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"maximum\s+prompt\s+length\s+is\s+\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"too\s+large\s+for\s+model\s+with\s+\d+\s+maximum\s+context\s+length", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"reduce\s+the\s+length\s+of\s+the\s+messages", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    public static bool IsPromptTooLong(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            foreach (var message in EnumerateErrorTexts(current))
            {
                if (IsPromptTooLongMessage(message))
                    return true;
            }
        }

        return false;
    }

    internal static bool IsPromptTooLongMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var marker in ExplicitMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var pattern in OverflowPatterns)
        {
            if (pattern.IsMatch(message))
                return true;
        }

        return false;
    }

    private static IEnumerable<string?> EnumerateErrorTexts(Exception ex)
    {
        yield return ex.Message;

        foreach (var name in new[] { "ResponseBody", "ResponseContent", "Body", "Content", "ErrorCode", "Code" })
        {
            var value = ReadStringProperty(ex, name);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        foreach (DictionaryEntry entry in ex.Data)
        {
            if (entry.Value is string value && !string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static string? ReadStringProperty(Exception ex, string propertyName)
    {
        var property = ex.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property?.GetValue(ex) as string;
    }
}
