using System.Globalization;
using System.Text.Json.Nodes;
using TimeZoneConverter;

namespace DotCraft.Context.WorldState;

internal sealed class EnvironmentSection : IWorldStateSection
{
    public const string SectionId = "environment";

    private const string CurrentDateKey = "currentDate";
    private const string TimeZoneKey = "timeZone";
    private const string WorkingDirectoryKey = "workingDirectory";
    private const string WorkspaceRootsKey = "workspaceRoots";
    private const string WorktreeKey = "worktree";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        var values = new JsonObject
        {
            [CurrentDateKey] = CurrentDate(),
            [TimeZoneKey] = GetLocalTimeZoneId()
        };
        if (WorkingDirectory(context) is { } workingDirectory)
            values[WorkingDirectoryKey] = workingDirectory;
        if (WorkspaceRoots(context, WorkingDirectory(context)) is { } roots)
            values[WorkspaceRootsKey] = string.Join("\n", roots);
        if (WorktreeBranch(context) is { } branch)
            values[WorktreeKey] = branch;
        return values;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var current = Snapshot(context)!.AsObject();
        var before = previous.TryGetKnown(out var value) ? value as JsonObject : null;
        var lines = new List<string>();

        AddIfChanged(lines, current, before, CurrentDateKey, date => $"CurrentDate: {date}");
        AddIfChanged(lines, current, before, TimeZoneKey, zone => $"TimeZone: {zone}");
        AddIfChanged(lines, current, before, WorkingDirectoryKey, path => $"WorkingDirectory: {path}");
        AddIfChanged(
            lines,
            current,
            before,
            WorkspaceRootsKey,
            roots => roots == null
                ? "WorkspaceRoots: none"
                : "WorkspaceRoots:\n" + string.Join("\n", roots.Split('\n').Select(static root => $"- {root}")));
        AddIfChanged(
            lines,
            current,
            before,
            WorktreeKey,
            branch => branch == null
                ? "Worktree: none"
                : $"Worktree: {branch}. Run commands in this worktree, not in the local checkout.");

        return lines.Count == 0 ? null : "## Environment\n" + string.Join("\n", lines);
    }

    private static void AddIfChanged(
        List<string> lines,
        JsonObject current,
        JsonObject? before,
        string key,
        Func<string?, string> render)
    {
        var now = current[key]?.GetValue<string>();
        if (before != null && string.Equals(now, before[key]?.GetValue<string>(), StringComparison.Ordinal))
            return;
        if (before == null && now == null)
            return;
        lines.Add(render(now));
    }

    private static string CurrentDate() => DateTime.Now.ToString("yyyy-MM-dd (dddd)");

    private static string? WorkingDirectory(WorldStateContext context) =>
        string.IsNullOrWhiteSpace(context.WorkspacePath)
            ? null
            : Path.GetFullPath(context.WorkspacePath);

    private static List<string>? WorkspaceRoots(WorldStateContext context, string? workingDirectory)
    {
        List<string> roots = [.. context.WorkspaceRoots.Select(Path.GetFullPath)];
        return roots.Any(root => !string.Equals(root, workingDirectory, PathComparison)) ? roots : null;
    }

    private static string? WorktreeBranch(WorldStateContext context) =>
        context.Thread.Worktree is { BranchName: { Length: > 0 } branch } ? branch : null;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string GetLocalTimeZoneId()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
            return local.Id;

        var region = GetCurrentRegionCode();
        if (region != null && TZConvert.TryWindowsToIana(local.Id, region, out var regionalIana))
            return regionalIana;

        return TZConvert.TryWindowsToIana(local.Id, out var iana)
            ? iana
            : local.Id;
    }

    private static string? GetCurrentRegionCode()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
