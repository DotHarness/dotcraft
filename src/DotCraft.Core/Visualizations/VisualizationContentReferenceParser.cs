using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotCraft.Visualizations;

/// <summary>Parses visualization content references from assistant Markdown.</summary>
public static partial class VisualizationContentReferenceParser
{
    private const string WideMode = "wide";
    public const int MaxTitleLength = 200;

    private const string Keyword = "visualize";
    private const char WrappedStart = (char)0xE200;
    private const char WrappedSeparator = (char)0xE202;
    private const char WrappedEnd = (char)0xE201;
    private static readonly string WrappedPrefix = $"{WrappedStart}{Keyword}{WrappedSeparator}";

    /// <summary>Returns valid references on their own lines outside fenced code blocks in source order.</summary>
    public static IReadOnlyList<VisualizationContentReference> Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [];

        var result = new List<VisualizationContentReference>();
        var inFence = false;
        char fenceCharacter = default;
        var fenceLength = 0;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            var trimmedStart = line.TrimStart();
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
            else if (!inFence && TryParseLine(line, out var path, out var title, out var mode))
            {
                result.Add(new VisualizationContentReference(path, title, mode));
            }
        }

        return result;
    }

    /// <summary>Returns whether a path is absolute, has no dot segments, and names a safe visualization file.</summary>
    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !AbsolutePathRegex().IsMatch(path))
            return false;

        var segments = path.Split('/', '\\');
        return !segments.Any(segment => segment is "." or "..")
               && FileNameRegex().IsMatch(segments[^1]);
    }

    private static bool TryParseLine(string line, out string path, out string? title, out string? mode)
    {
        path = string.Empty;
        title = null;
        mode = null;
        var trimmed = line.Trim();
        string json;
        if (trimmed.StartsWith(Keyword + "{", StringComparison.Ordinal))
            json = trimmed[Keyword.Length..];
        else if (trimmed.Length > WrappedPrefix.Length + 1
                 && trimmed.StartsWith(WrappedPrefix, StringComparison.Ordinal)
                 && trimmed[^1] == WrappedEnd)
            json = trimmed[WrappedPrefix.Length..^1];
        else
            return false;

        string? parsedPath = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return false;
                var value = property.Value.GetString();
                switch (property.Name)
                {
                    case "path":
                        parsedPath = value;
                        break;
                    case "title":
                        title = value;
                        break;
                    case "mode":
                        mode = value;
                        break;
                    default:
                        return false;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (!IsValidPath(parsedPath)
            || title is not null && (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
            || mode is not null && mode != WideMode)
        {
            return false;
        }

        path = parsedPath!;
        return true;
    }

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

    [GeneratedRegex("^(?:[A-Za-z]:[\\\\/]|\\\\\\\\|/)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*\\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();
}

/// <summary>A validated visualization content reference.</summary>
public sealed record VisualizationContentReference(string Path, string? Title, string? Mode);
