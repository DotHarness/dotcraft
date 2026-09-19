using System.Globalization;
using System.Text.Json.Nodes;
using TimeZoneConverter;

namespace DotCraft.Context.WorldState;

internal sealed class EnvironmentSection : IWorldStateSection
{
    public const string SectionId = "environment";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        var values = new JsonObject
        {
            ["currentDate"] = CurrentDate(),
            ["timeZone"] = GetLocalTimeZoneId()
        };
        if (WorkingDirectory(context) is { } workingDirectory)
            values["workingDirectory"] = workingDirectory;
        return values;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var lines = new List<string>
        {
            $"CurrentDate: {CurrentDate()}",
            $"TimeZone: {GetLocalTimeZoneId()}"
        };
        if (WorkingDirectory(context) is { } workingDirectory)
            lines.Add($"WorkingDirectory: {workingDirectory}");

        return "## Environment\n" + string.Join("\n", lines);
    }

    private static string CurrentDate() => DateTime.Now.ToString("yyyy-MM-dd (dddd)");

    private static string? WorkingDirectory(WorldStateContext context) =>
        string.IsNullOrWhiteSpace(context.WorkspacePath)
            ? null
            : Path.GetFullPath(context.WorkspacePath);

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
