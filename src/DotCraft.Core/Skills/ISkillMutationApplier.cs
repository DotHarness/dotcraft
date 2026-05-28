namespace DotCraft.Skills;

/// <summary>
/// Applies skill content mutations. The default implementation writes workspace
/// files directly; future Skill Graph implementations can wrap the same surface
/// with revisions, proposals, and rollback metadata.
/// </summary>
public interface ISkillMutationApplier
{
    /// <summary>
    /// Creates a new workspace skill.
    /// </summary>
    Task<SkillMutationResult> CreateAsync(SkillCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an existing workspace skill's <c>SKILL.md</c>.
    /// </summary>
    Task<SkillMutationResult> EditAsync(SkillEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches <c>SKILL.md</c> or a supporting file in an existing workspace skill.
    /// </summary>
    Task<SkillMutationResult> PatchAsync(SkillPatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing workspace skill.
    /// </summary>
    Task<SkillMutationResult> DeleteAsync(SkillDeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a supporting file in an existing workspace skill.
    /// </summary>
    Task<SkillMutationResult> WriteFileAsync(SkillWriteFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a supporting file from an existing workspace skill.
    /// </summary>
    Task<SkillMutationResult> RemoveFileAsync(SkillRemoveFileRequest request, CancellationToken cancellationToken = default);
}
