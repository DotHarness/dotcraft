using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Graphics;

namespace DotCraft.Satellite.Island;

/// <summary>Where the owner left the capsule, remembered per display in <c>~/.craft/satellite.json</c>.</summary>
internal static class IslandPlacement
{
    private const int TopInsetDips = 10;
    private const string Section = "island";

    /// <summary>Identifies one display by its work area, so a rearranged desktop forgets rather than misplaces.</summary>
    public static string Key(RectInt32 workArea) =>
        $"{workArea.X},{workArea.Y},{workArea.Width}x{workArea.Height}";

    public static PointInt32 Default(RectInt32 workArea, int width, double scale) => new(
        workArea.X + ((workArea.Width - width) / 2),
        workArea.Y + (int)Math.Round(TopInsetDips * scale));

    public static PointInt32? Read(string key)
    {
        var root = Load();
        if (root?[Section] is not JsonObject section || section[key] is not JsonObject stored)
            return null;
        if (stored["x"]?.GetValue<int>() is not { } x || stored["y"]?.GetValue<int>() is not { } y)
            return null;
        return new PointInt32(x, y);
    }

    public static void Write(string key, PointInt32 position)
    {
        try
        {
            var root = Load() ?? [];
            if (root[Section] is not JsonObject section)
            {
                section = [];
                root[Section] = section;
            }
            section[key] = new JsonObject { ["x"] = position.X, ["y"] = position.Y };
            var path = Path();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A machine that cannot remember the position still shows the island.
        }
    }

    private static JsonObject? Load()
    {
        try
        {
            var path = Path();
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Path() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft",
        "satellite.json");
}
