using System.Text.RegularExpressions;

namespace DotCraft.Protocol.InlineVisualizations;

/// <summary>Parses exact inline visualization directives from assistant Markdown.</summary>
public static partial class InlineVisualizationDirectiveParser
{
    /// <summary>Returns valid directives outside fenced code blocks in source order.</summary>
    public static IReadOnlyList<InlineVisualizationDirective> Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [];

        var result = new List<InlineVisualizationDirective>();
        var inFence = false;
        char fenceCharacter = default;
        var fenceLength = 0;
        var ordinal = 0;
        var offset = 0;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            var trimmedStart = line.TrimStart();
            var leading = line.Length - trimmedStart.Length;
            if (TryFence(trimmedStart, out var character, out var length))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = character;
                    fenceLength = length;
                }
                else if (character == fenceCharacter && length >= fenceLength
                         && trimmedStart[length..].Trim().Length == 0)
                {
                    inFence = false;
                }
            }
            else if (!inFence)
            {
                var match = DirectiveRegex().Match(line);
                if (match.Success)
                {
                    result.Add(new InlineVisualizationDirective(
                        match.Groups["file"].Value,
                        ordinal++,
                        offset + leading,
                        offset + line.Length));
                }
            }

            offset += rawLine.Length + 1;
        }

        return result;
    }

    /// <summary>Returns whether a file name is a safe visualization basename.</summary>
    public static bool IsValidFileName(string? file) =>
        !string.IsNullOrWhiteSpace(file) && FileNameRegex().IsMatch(file);

    private static bool TryFence(string line, out char character, out int length)
    {
        character = default;
        length = 0;
        if (line.Length < 3 || line[0] is not ('`' or '~'))
            return false;
        character = line[0];
        while (length < line.Length && line[length] == character)
            length++;
        return length >= 3;
    }

    [GeneratedRegex("^\\s*::dotcraft-inline-vis\\{file=\\\"(?<file>[a-z0-9]+(?:-[a-z0-9]+)*\\.html)\\\"\\}\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveRegex();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*\\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();
}

/// <summary>A validated inline visualization directive.</summary>
public sealed record InlineVisualizationDirective(string File, int Ordinal, int Start, int End);
