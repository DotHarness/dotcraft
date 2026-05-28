using System.Text;

namespace DotCraft.Skills;

/// <summary>
/// Routes self-learning skill updates to workspace-local variants while preserving source skills.
/// </summary>
public sealed class VariantSkillMutationApplier(
    ISkillMutationApplier sourceApplier,
    SkillsLoader skillsLoader,
    SkillVariantTarget target) : ISkillMutationApplier
{
    private static readonly HashSet<string> AllowedSupportingDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "scripts",
        "assets"
    };

    /// <inheritdoc />
    public Task<SkillMutationResult> CreateAsync(SkillCreateRequest request, CancellationToken cancellationToken = default) =>
        sourceApplier.CreateAsync(request, cancellationToken);

    /// <inheritdoc />
    public async Task<SkillMutationResult> EditAsync(SkillEditRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var variant = EnsureVariant(request.Name);
        if (!variant.Success)
            return SkillMutationResult.Fail(variant.Error!);

        var skillFile = Path.Combine(variant.SkillDir!, "SKILL.md");
        var original = await File.ReadAllTextAsync(skillFile, Encoding.UTF8, cancellationToken);
        try
        {
            await AtomicWriteTextAsync(skillFile, request.Content, cancellationToken);
            skillsLoader.VariantStore.Touch(variant.Manifest!);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"Skill '{request.Name}' updated. The original skill was not modified.");
        }
        catch
        {
            await AtomicWriteTextAsync(skillFile, original, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SkillMutationResult> PatchAsync(SkillPatchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var variant = EnsureVariant(request.Name);
        if (!variant.Success)
            return SkillMutationResult.Fail(variant.Error!);

        string? pathError = null;
        var targetPath = string.IsNullOrWhiteSpace(request.FilePath)
            ? Path.Combine(variant.SkillDir!, "SKILL.md")
            : ResolveSupportingFilePath(variant.SkillDir!, request.FilePath, requireExisting: true, out pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return SkillMutationResult.Fail(pathError);

        if (!File.Exists(targetPath))
            return SkillMutationResult.Fail($"File not found: {targetPath}");

        var original = await File.ReadAllTextAsync(targetPath, Encoding.UTF8, cancellationToken);
        var replacementCount = CountOccurrences(original, request.OldString);
        if (replacementCount == 0)
            return SkillMutationResult.Fail("The requested text was not found.");

        if (!request.ReplaceAll && replacementCount != 1)
            return SkillMutationResult.Fail($"The requested text matched {replacementCount} times. Provide more context or set replaceAll=true.");

        var updated = request.ReplaceAll
            ? original.Replace(request.OldString, request.NewString, StringComparison.Ordinal)
            : ReplaceFirst(original, request.OldString, request.NewString);

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            var validationError = SkillFrontmatter.ValidateContent(updated, request.Name, request.MaxSkillContentChars);
            if (validationError != null)
                return SkillMutationResult.Fail($"Patch would break SKILL.md structure: {validationError}");
        }
        else
        {
            var maxBytes = request.MaxSupportingFileBytes > 0
                ? request.MaxSupportingFileBytes
                : SkillFrontmatter.DefaultMaxSupportingFileBytes;
            var byteCount = Encoding.UTF8.GetByteCount(updated);
            if (byteCount > maxBytes)
                return SkillMutationResult.Fail($"Patch would make '{request.FilePath}' {byteCount:N0} bytes (limit: {maxBytes:N0}).");
        }

        try
        {
            await AtomicWriteTextAsync(targetPath, updated, cancellationToken);
            skillsLoader.VariantStore.Touch(variant.Manifest!);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"Patched skill '{request.Name}'. The original skill was not modified.", replacementCount: replacementCount);
        }
        catch
        {
            await AtomicWriteTextAsync(targetPath, original, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<SkillMutationResult> DeleteAsync(SkillDeleteRequest request, CancellationToken cancellationToken = default) =>
        sourceApplier.DeleteAsync(request, cancellationToken);

    /// <inheritdoc />
    public async Task<SkillMutationResult> WriteFileAsync(SkillWriteFileRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var variant = EnsureVariant(request.Name);
        if (!variant.Success)
            return SkillMutationResult.Fail(variant.Error!);

        var targetPath = ResolveSupportingFilePath(variant.SkillDir!, request.FilePath, requireExisting: false, out var pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return SkillMutationResult.Fail(pathError);

        string? original = null;
        if (File.Exists(targetPath))
            original = await File.ReadAllTextAsync(targetPath, Encoding.UTF8, cancellationToken);

        try
        {
            await AtomicWriteTextAsync(targetPath, request.FileContent, cancellationToken);
            skillsLoader.VariantStore.Touch(variant.Manifest!);
            skillsLoader.RefreshDescriptors();
            return SkillMutationResult.Ok($"File '{request.FilePath}' written to skill '{request.Name}'. The original skill was not modified.");
        }
        catch
        {
            if (original != null)
                await AtomicWriteTextAsync(targetPath, original, CancellationToken.None);
            else if (File.Exists(targetPath))
                File.Delete(targetPath);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<SkillMutationResult> RemoveFileAsync(SkillRemoveFileRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var variant = EnsureVariant(request.Name);
        if (!variant.Success)
            return Task.FromResult(SkillMutationResult.Fail(variant.Error!));

        var targetPath = ResolveSupportingFilePath(variant.SkillDir!, request.FilePath, requireExisting: true, out var pathError);
        if (!string.IsNullOrWhiteSpace(pathError))
            return Task.FromResult(SkillMutationResult.Fail(pathError));

        if (!File.Exists(targetPath))
            return Task.FromResult(SkillMutationResult.Fail($"File not found: {request.FilePath}"));

        File.Delete(targetPath);
        RemoveEmptyParents(new DirectoryInfo(Path.GetDirectoryName(targetPath)!), new DirectoryInfo(variant.SkillDir!));
        skillsLoader.VariantStore.Touch(variant.Manifest!);
        skillsLoader.RefreshDescriptors();
        return Task.FromResult(SkillMutationResult.Ok($"File '{request.FilePath}' removed from skill '{request.Name}'. The original skill was not modified."));
    }

    private VariantResolution EnsureVariant(string name)
    {
        var source = skillsLoader.ResolveSkillInfo(name);
        if (source == null)
            return VariantResolution.Fail($"Skill '{name}' not found.");

        var fingerprint = SkillVariantStore.ComputeSourceFingerprint(source.Path);
        var manifest = skillsLoader.VariantStore.EnsureCurrentVariant(source, fingerprint, target);
        var skillDir = skillsLoader.VariantStore.GetVariantSkillDir(manifest);
        return new VariantResolution(true, manifest, skillDir, null);
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

    private sealed record VariantResolution(
        bool Success,
        SkillVariantManifest? Manifest,
        string? SkillDir,
        string? Error)
    {
        public static VariantResolution Fail(string error) => new(false, null, null, error);
    }
}
