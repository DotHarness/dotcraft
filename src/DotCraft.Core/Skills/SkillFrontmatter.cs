using System.Text.RegularExpressions;

namespace DotCraft.Skills;

/// <summary>
/// Validation helpers for generated <c>SKILL.md</c> files.
/// </summary>
public static partial class SkillFrontmatter
{
    /// <summary>
    /// Default maximum length for a skill name.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Default maximum length for a skill description.
    /// </summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>
    /// Default maximum length for a <c>SKILL.md</c> file, in characters.
    /// </summary>
    public const int DefaultMaxSkillContentChars = 100_000;

    /// <summary>
    /// Default maximum size for a supporting file, in bytes.
    /// </summary>
    public const int DefaultMaxSupportingFileBytes = 1_048_576;

    /// <summary>
    /// Validates a filesystem-safe skill name.
    /// </summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Skill name is required.";

        if (name.Length > MaxNameLength)
            return $"Skill name exceeds {MaxNameLength} characters.";

        if (!SkillNameRegex().IsMatch(name))
            return $"Invalid skill name '{name}'. Use lowercase letters, numbers, hyphens, dots, and underscores. Must start with a letter or digit.";

        return null;
    }

    /// <summary>
    /// Validates complete <c>SKILL.md</c> content and required frontmatter.
    /// </summary>
    public static string? ValidateContent(string content, string expectedName, int maxSkillContentChars)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "SKILL.md content cannot be empty.";

        var maxChars = maxSkillContentChars > 0 ? maxSkillContentChars : DefaultMaxSkillContentChars;
        if (content.Length > maxChars)
            return $"SKILL.md content is {content.Length:N0} characters (limit: {maxChars:N0}).";

        if (!content.StartsWith("---", StringComparison.Ordinal))
            return "SKILL.md must start with YAML frontmatter (---).";

        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return "SKILL.md frontmatter is not closed. Ensure there is a closing '---' line.";

        var metadata = ParseFrontmatter(match.Groups["yaml"].Value);
        if (!metadata.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return "Frontmatter must include a 'name' field.";

        if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            return $"Frontmatter name '{name}' must match requested skill name '{expectedName}'.";

        if (!metadata.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            return "Frontmatter must include a 'description' field.";

        if (description.Length > MaxDescriptionLength)
            return $"Description exceeds {MaxDescriptionLength} characters.";

        var body = content[match.Length..].Trim();
        if (string.IsNullOrWhiteSpace(body))
            return "SKILL.md must include instructions after the frontmatter.";

        return null;
    }

    private static Dictionary<string, string> ParseFrontmatter(string yaml)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in yaml.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            metadata[key] = value;
        }

        return metadata;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillNameRegex();

    [GeneratedRegex("^---\\r?\\n(?<yaml>.*?)\\r?\\n---\\r?\\n", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterRegex();
}
