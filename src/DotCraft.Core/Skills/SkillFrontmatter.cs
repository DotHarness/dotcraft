namespace DotCraft.Skills;

/// <summary>
/// Size and length limits applied when verifying a <c>SKILL.md</c> bundle.
/// </summary>
public static class SkillFrontmatter
{
    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 1024;

    public const int DefaultMaxSkillContentChars = 100_000;

    public const int DefaultMaxSupportingFileBytes = 1_048_576;
}
