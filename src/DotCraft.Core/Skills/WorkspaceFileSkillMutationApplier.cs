using System.Text;

namespace DotCraft.Skills;

/// <summary>
/// Applies skill mutations directly to the workspace skill directory.
/// </summary>
public sealed class WorkspaceFileSkillMutationApplier(SkillsLoader skillsLoader) : ISkillMutationApplier
{
    private static readonly HashSet<string> AllowedSupportingDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "scripts",
        "assets"
    };

    /// <inheritdoc />
    public async Task<SkillMutationResult> CreateAsync(SkillCreateRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = FindSkill(request.Name);
        if (existing != null)
            return SkillMutationResult.Fail($"A skill named '{request.Name}' already exists at {existing.Path}.");

        var skillDir = skillsLoader.ResolveWorkspaceSkillDir(request.Name);
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        try
        {
            Directory.CreateDirectory(skillDir);
            await AtomicWriteTextAsync(skillFile, request.Content, cancellationToken);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"Skill '{request.Name}' created.", skillFile);
        }
        catch
        {
            if (Directory.Exists(skillDir))
                Directory.Delete(skillDir, recursive: true);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SkillMutationResult> EditAsync(SkillEditRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skill = FindMutableWorkspaceSkill(request.Name);
        if (!skill.Success)
            return SkillMutationResult.Fail(skill.Error!);

        var skillFile = skill.Path!;
        var originalContent = File.Exists(skillFile)
            ? await File.ReadAllTextAsync(skillFile, Encoding.UTF8, cancellationToken)
            : null;

        try
        {
            await AtomicWriteTextAsync(skillFile, request.Content, cancellationToken);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"Skill '{request.Name}' updated.", skillFile);
        }
        catch
        {
            if (originalContent != null)
                await AtomicWriteTextAsync(skillFile, originalContent, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SkillMutationResult> PatchAsync(SkillPatchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skill = FindMutableWorkspaceSkill(request.Name);
        if (!skill.Success)
            return SkillMutationResult.Fail(skill.Error!);

        var skillDir = Path.GetDirectoryName(skill.Path!)!;
        string? pathError = null;
        var target = string.IsNullOrWhiteSpace(request.FilePath)
            ? skill.Path!
            : ResolveSupportingFilePath(skillDir, request.FilePath, requireExisting: true, out pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return SkillMutationResult.Fail(pathError);

        if (!File.Exists(target))
            return SkillMutationResult.Fail($"File not found: {target}");

        var originalContent = await File.ReadAllTextAsync(target, Encoding.UTF8, cancellationToken);
        var replacementCount = CountOccurrences(originalContent, request.OldString);
        if (replacementCount == 0)
            return SkillMutationResult.Fail("The requested text was not found.");

        if (!request.ReplaceAll && replacementCount != 1)
            return SkillMutationResult.Fail($"The requested text matched {replacementCount} times. Provide more context or set replaceAll=true.");

        var updatedContent = request.ReplaceAll
            ? originalContent.Replace(request.OldString, request.NewString, StringComparison.Ordinal)
            : ReplaceFirst(originalContent, request.OldString, request.NewString);

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            var validationError = SkillFrontmatter.ValidateContent(
                updatedContent,
                request.Name,
                request.MaxSkillContentChars);
            if (validationError != null)
                return SkillMutationResult.Fail($"Patch would break SKILL.md structure: {validationError}");
        }
        else
        {
            var maxBytes = request.MaxSupportingFileBytes > 0
                ? request.MaxSupportingFileBytes
                : SkillFrontmatter.DefaultMaxSupportingFileBytes;
            var byteCount = Encoding.UTF8.GetByteCount(updatedContent);
            if (byteCount > maxBytes)
                return SkillMutationResult.Fail($"Patch would make '{request.FilePath}' {byteCount:N0} bytes (limit: {maxBytes:N0}).");
        }

        try
        {
            await AtomicWriteTextAsync(target, updatedContent, cancellationToken);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"Patched skill '{request.Name}'.", target, replacementCount);
        }
        catch
        {
            await AtomicWriteTextAsync(target, originalContent, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<SkillMutationResult> DeleteAsync(SkillDeleteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skill = FindMutableWorkspaceSkill(request.Name);
        if (!skill.Success)
            return Task.FromResult(SkillMutationResult.Fail(skill.Error!));

        var skillDir = Path.GetDirectoryName(skill.Path!)!;
        Directory.Delete(skillDir, recursive: true);
        skillsLoader.RefreshDescriptors();
        return Task.FromResult(SkillMutationResult.Ok($"Skill '{request.Name}' deleted.", skillDir));
    }

    /// <inheritdoc />
    public async Task<SkillMutationResult> WriteFileAsync(SkillWriteFileRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skill = FindMutableWorkspaceSkill(request.Name);
        if (!skill.Success)
            return SkillMutationResult.Fail(skill.Error!);

        var skillDir = Path.GetDirectoryName(skill.Path!)!;
        var target = ResolveSupportingFilePath(skillDir, request.FilePath, requireExisting: false, out var pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return SkillMutationResult.Fail(pathError);

        string? originalContent = null;
        if (File.Exists(target))
            originalContent = await File.ReadAllTextAsync(target, Encoding.UTF8, cancellationToken);

        try
        {
            await AtomicWriteTextAsync(target, request.FileContent, cancellationToken);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"File '{request.FilePath}' written to skill '{request.Name}'.", target);
        }
        catch
        {
            if (originalContent != null)
                await AtomicWriteTextAsync(target, originalContent, CancellationToken.None);
            else if (File.Exists(target))
                File.Delete(target);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<SkillMutationResult> RemoveFileAsync(SkillRemoveFileRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var skill = FindMutableWorkspaceSkill(request.Name);
        if (!skill.Success)
            return Task.FromResult(SkillMutationResult.Fail(skill.Error!));

        var skillDir = Path.GetDirectoryName(skill.Path!)!;
        var target = ResolveSupportingFilePath(skillDir, request.FilePath, requireExisting: true, out var pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return Task.FromResult(SkillMutationResult.Fail(pathError));

        if (!File.Exists(target))
            return Task.FromResult(SkillMutationResult.Fail($"File not found: {request.FilePath}"));

        File.Delete(target);
        RemoveEmptyParents(new DirectoryInfo(Path.GetDirectoryName(target)!), new DirectoryInfo(skillDir));
        skillsLoader.RefreshDescriptors();
        return Task.FromResult(SkillMutationResult.Ok($"File '{request.FilePath}' removed from skill '{request.Name}'.", target));
    }

    private SkillsLoader.SkillInfo? FindSkill(string name) =>
        skillsLoader.ListSkills(filterUnavailable: false)
            .FirstOrDefault(skill => string.Equals(skill.Name, name, StringComparison.OrdinalIgnoreCase));

    private SkillMutationResult FindMutableWorkspaceSkill(string name)
    {
        var skill = FindSkill(name);
        if (skill == null)
            return SkillMutationResult.Fail($"Skill '{name}' not found.");

        if (!string.Equals(skill.Source, "workspace", StringComparison.OrdinalIgnoreCase))
            return SkillMutationResult.Fail($"Skill '{name}' is {skill.Source} and cannot be modified by self-learning tools. Create a workspace copy first.");

        return SkillMutationResult.Ok("Skill is mutable.", skill.Path);
    }

    private static string ResolveSupportingFilePath(
        string skillDir,
        string filePath,
        bool requireExisting,
        out string? error)
    {
        error = ValidateSupportingFilePath(filePath);
        if (error != null)
            return string.Empty;

        var target = Path.GetFullPath(Path.Combine(skillDir, filePath));
        var normalizedSkillDir = Path.GetFullPath(skillDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!target.StartsWith(normalizedSkillDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith(normalizedSkillDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = "Path traversal outside the skill directory is not allowed.";
            return string.Empty;
        }

        if (requireExisting && !File.Exists(target))
        {
            error = $"File not found: {filePath}";
            return string.Empty;
        }

        return target;
    }

    private static string? ValidateSupportingFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "filePath is required.";

        if (Path.IsPathRooted(filePath))
            return "Absolute paths are not allowed for supporting files.";

        var normalized = filePath.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "Provide a file path under scripts/ or assets/.";

        if (parts.Any(part => part == ".."))
            return "Path traversal ('..') is not allowed.";

        if (!AllowedSupportingDirectories.Contains(parts[0]))
            return "File must be under one of: scripts, assets.";

        return null;
    }

    private static async Task AtomicWriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static int CountOccurrences(string content, string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReplaceFirst(string content, string oldValue, string newValue)
    {
        var index = content.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0
            ? content
            : string.Concat(content.AsSpan(0, index), newValue, content.AsSpan(index + oldValue.Length));
    }

    private static void RemoveEmptyParents(DirectoryInfo current, DirectoryInfo boundary)
    {
        while (current.Exists
               && !string.Equals(current.FullName, boundary.FullName, StringComparison.OrdinalIgnoreCase)
               && !current.EnumerateFileSystemInfos().Any())
        {
            var parent = current.Parent;
            current.Delete();
            if (parent == null)
                break;
            current = parent;
        }
    }
}
