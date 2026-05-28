namespace DotCraft.Dreams;

internal static class DreamsMarkdownValidation
{
    public const int MaxIndexBytes = 25 * 1024;
    public const int MaxIndexLines = 200;

    public static bool IsValid(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return false;

        var normalized = markdown.TrimStart();
        if (!normalized.StartsWith("# Dream Memory", StringComparison.OrdinalIgnoreCase))
            return false;

        if (System.Text.Encoding.UTF8.GetByteCount(markdown) > MaxIndexBytes)
            return false;

        return CountLines(markdown) <= MaxIndexLines;
    }

    public static string Normalize(string markdown) =>
        markdown.TrimEnd() + Environment.NewLine;

    private static int CountLines(string markdown)
    {
        if (markdown.Length == 0)
            return 0;

        var lines = 1;
        foreach (var ch in markdown)
        {
            if (ch == '\n')
                lines++;
        }

        return lines;
    }
}
