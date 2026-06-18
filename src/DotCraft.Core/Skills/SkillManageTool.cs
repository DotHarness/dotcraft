using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.Skills;

/// <summary>
/// Agent-facing tools for creating and maintaining workspace skills.
/// </summary>
public sealed class SkillManageTool(
    ISkillMutationApplier mutationApplier,
    AppConfig.SelfLearningConfig config,
    IApprovalService? approvalService = null,
    IContextPageManager? contextPageManager = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string SkillManageDescription =
        """
        Manage workspace skills. Updates normally write a workspace adaptation so the original skill is not modified.
        The 'action' parameter must be one of:
        'create', 'edit', 'patch', 'write_file', 'remove_file' or `delete`.

        Examples:
        SkillManage(action: "create", name: "my-skill", content: "<full SKILL.md>") - create a new workspace skill;
        SkillManage(action: "patch", name: "my-skill", oldString: "...", newString: "...") - targeted fix in the effective SKILL.md;
        SkillManage(action: "edit", name: "my-skill", content: "<full updated SKILL.md>") - full rewrite of the effective SKILL.md;
        SkillManage(action: "write_file", name: "my-skill", filePath: "scripts/check.sh", fileContent: "...") - write supporting file in the workspace adaptation;
        SkillManage(action: "remove_file", name: "my-skill", filePath: "assets/example.json") - remove supporting file from the workspace adaptation;
        SkillManage(action: "delete", name: "my-skill") - delete the skill.

        Create when a complex task succeeded, a tricky error was fixed, a user correction produced a stable workflow,
        or the user asks you to remember a procedure. Update when a skill is stale, incomplete, wrong, or missing a pitfall.
        Skip simple one-offs.
        Prefer 'patch' for small fixes. For major rewrites, load the current skill with SkillView first and use 'edit'.
        Read the skill-authoring skill when authoring or restructuring skills for frontmatter, supporting-file rules,
        pitfalls, and verification guidance.
        """;

    /// <summary>
    /// Creates and maintains reusable workspace skills.
    /// </summary>
    [GeneratedTool]
    [Description(SkillManageDescription)]
    [StreamArguments(false)]
    public async Task<string> SkillManage(
        [Description("Must be one of: 'create', 'edit', 'patch', 'write_file', 'remove_file' or `delete`.")]
        string action,
        [Description("Lowercase skill name using letters, numbers, hyphens, dots, or underscores. Required for every action.")]
        string name,
        [Description("Full SKILL.md content. Required for 'create' and 'edit'.")]
        string? content = null,
        [Description("Text to replace. Required for 'patch'. Include enough context to make it unique unless replaceAll=true.")]
        string? oldString = null,
        [Description("Replacement text. Required for 'patch'. Use an empty string to delete matched text.")]
        string? newString = null,
        [Description("Optional supporting file path under scripts/ or assets/. Omit to patch SKILL.md.")]
        string? filePath = null,
        [Description("Content to write. Required for 'write_file'.")]
        string? fileContent = null,
        [Description("Replace every occurrence instead of requiring exactly one match.")]
        bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action))
            return Error("action is required. Use one of: create, edit, patch, write_file, remove_file, delete.");

        var normalizedAction = action.Trim().ToLowerInvariant();
        var validationError = ValidateNameOnly(name);
        if (validationError != null)
            return Error(validationError);

        return normalizedAction switch
        {
            "create" => await CreateAsync(name, content, cancellationToken),
            "edit" => await EditAsync(name, content, cancellationToken),
            "patch" => await PatchAsync(name, oldString, newString, filePath, replaceAll, cancellationToken),
            "delete" => await DeleteAsync(name, cancellationToken),
            "write_file" => await WriteFileAsync(name, filePath, fileContent, cancellationToken),
            "remove_file" => await RemoveFileAsync(name, filePath, cancellationToken),
            _ => Error($"Unknown action '{action}'. Use: create, edit, patch, delete, write_file, remove_file.")
        };
    }

    private async Task<string> CreateAsync(string name, string? content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Error("content is required for action 'create'. Provide the full SKILL.md text.");

        var validationError = ValidateSkillContent(name, content);
        if (validationError != null)
            return Error(validationError);

        var approved = await RequestSkillApprovalAsync("create", name);
        if (!approved)
            return Error($"Skill create for '{name}' was rejected by user.");

        return SerializeAndMark(await mutationApplier.CreateAsync(new SkillCreateRequest(name, content), cancellationToken));
    }

    private async Task<string> EditAsync(string name, string? content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Error("content is required for action 'edit'. Provide the full updated SKILL.md text.");

        var validationError = ValidateSkillContent(name, content);
        if (validationError != null)
            return Error(validationError);

        return SerializeAndMark(await mutationApplier.EditAsync(new SkillEditRequest(name, content), cancellationToken));
    }

    private async Task<string> PatchAsync(
        string name,
        string? oldString,
        string? newString,
        string? filePath,
        bool replaceAll,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(oldString))
            return Error("oldString is required for action 'patch'.");

        if (newString == null)
            return Error("newString is required for action 'patch'. Use an empty string to delete matched text.");

        return SerializeAndMark(await mutationApplier.PatchAsync(
            new SkillPatchRequest(
                name,
                oldString,
                newString,
                filePath,
                replaceAll,
                config.MaxSkillContentChars,
                config.MaxSupportingFileBytes),
            cancellationToken));
    }

    private async Task<string> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var approved = await RequestSkillApprovalAsync("delete", name);
        if (!approved)
            return Error($"Skill delete for '{name}' was rejected by user.");

        return SerializeAndMark(await mutationApplier.DeleteAsync(new SkillDeleteRequest(name), cancellationToken));
    }

    private async Task<string> WriteFileAsync(
        string name,
        string? filePath,
        string? fileContent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Error("filePath is required for action 'write_file'.");

        if (fileContent == null)
            return Error("fileContent is required for action 'write_file'.");

        var byteCount = Encoding.UTF8.GetByteCount(fileContent);
        var maxBytes = config.MaxSupportingFileBytes > 0
            ? config.MaxSupportingFileBytes
            : SkillFrontmatter.DefaultMaxSupportingFileBytes;
        if (byteCount > maxBytes)
            return Error($"File content is {byteCount:N0} bytes (limit: {maxBytes:N0}).");

        return SerializeAndMark(await mutationApplier.WriteFileAsync(
            new SkillWriteFileRequest(name, filePath, fileContent),
            cancellationToken));
    }

    private async Task<string> RemoveFileAsync(string name, string? filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Error("filePath is required for action 'remove_file'.");

        return SerializeAndMark(await mutationApplier.RemoveFileAsync(
            new SkillRemoveFileRequest(name, filePath),
            cancellationToken));
    }

    private string? ValidateSkillContent(string name, string content)
    {
        var validationError = ValidateNameOnly(name);
        if (validationError != null)
            return validationError;

        var maxChars = config.MaxSkillContentChars > 0
            ? config.MaxSkillContentChars
            : SkillFrontmatter.DefaultMaxSkillContentChars;
        return SkillFrontmatter.ValidateContent(content, name, maxChars);
    }

    private static string? ValidateNameOnly(string name) => SkillFrontmatter.ValidateName(name);

    private async Task<bool> RequestSkillApprovalAsync(string operation, string name)
    {
        if (approvalService == null)
            return true;

        return await approvalService.RequestResourceApprovalAsync(
            "skill",
            operation,
            name,
            ApprovalContextScope.Current);
    }

    private static string Serialize(SkillMutationResult result) =>
        JsonSerializer.Serialize(result, JsonOptions);

    private string SerializeAndMark(SkillMutationResult result)
    {
        if (result.Success)
            contextPageManager?.MarkDirty(ContextPageKeys.SkillsWildcard());
        return Serialize(result);
    }

    private static string Error(string message) =>
        Serialize(SkillMutationResult.Fail(message));
}
