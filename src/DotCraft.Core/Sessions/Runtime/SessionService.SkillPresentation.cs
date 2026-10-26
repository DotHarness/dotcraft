using System.Text.Json.Nodes;
using DotCraft.Skills;
using DotCraft.Tools;

namespace DotCraft.Sessions;

internal readonly record struct SkillPresentationMark(string Kind, string SkillName, string SkillFile);

public sealed partial class SessionService
{
    internal const string SkillPresentationOption = "skill";

    internal const string SkillNamePresentationOption = "skillName";

    internal const string SkillFilePresentationOption = "skillFile";

    private static SkillPresentationMark? ResolveSkillPresentation(
        ToolDefinition definition,
        JsonObject? arguments,
        string? workspacePath,
        SkillsLoader skillsLoader)
    {
        if (definition.Id.Kind != ToolSourceKind.CoreNative
            || definition.Presentation?.Id.Value is not ("core.read-file" or "core.file-write")
            || !FileToolAccess.TryResolveTarget(definition.Id.SourceToolId.Value, arguments, workspacePath, out var access, out var target)
            || !skillsLoader.TryResolveSkillDirectory(target, out var skillName, out var skillDirectory))
        {
            return null;
        }

        var skillFile = Path.GetFullPath(Path.Combine(skillDirectory, "SKILL.md"));
        var kind = access.Operation switch
        {
            "read" => "read",
            "write" when IsSamePath(target, skillFile) && !File.Exists(target) => "create",
            _ => "update"
        };
        return new SkillPresentationMark(kind, skillName, skillFile);
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
