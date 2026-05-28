namespace DotCraft.Skills;

/// <summary>
/// Request to create a new workspace skill.
/// </summary>
public sealed record SkillCreateRequest(string Name, string Content);

/// <summary>
/// Request to replace an existing workspace skill's <c>SKILL.md</c>.
/// </summary>
public sealed record SkillEditRequest(string Name, string Content);

/// <summary>
/// Request to patch <c>SKILL.md</c> or a supporting file in a workspace skill.
/// </summary>
public sealed record SkillPatchRequest(
    string Name,
    string OldString,
    string NewString,
    string? FilePath,
    bool ReplaceAll,
    int MaxSkillContentChars = SkillFrontmatter.DefaultMaxSkillContentChars,
    int MaxSupportingFileBytes = SkillFrontmatter.DefaultMaxSupportingFileBytes);

/// <summary>
/// Request to delete a workspace skill.
/// </summary>
public sealed record SkillDeleteRequest(string Name);

/// <summary>
/// Request to write a supporting file in a workspace skill.
/// </summary>
public sealed record SkillWriteFileRequest(string Name, string FilePath, string FileContent);

/// <summary>
/// Request to remove a supporting file from a workspace skill.
/// </summary>
public sealed record SkillRemoveFileRequest(string Name, string FilePath);

/// <summary>
/// Result returned by skill mutation operations.
/// </summary>
public sealed record SkillMutationResult(
    bool Success,
    string Message,
    string? Path = null,
    string? Error = null,
    int? ReplacementCount = null)
{
    /// <summary>
    /// Creates a successful mutation result.
    /// </summary>
    public static SkillMutationResult Ok(string message, string? path = null, int? replacementCount = null) =>
        new(true, message, path, ReplacementCount: replacementCount);

    /// <summary>
    /// Creates a failed mutation result.
    /// </summary>
    public static SkillMutationResult Fail(string error) =>
        new(false, error, Error: error);
}
