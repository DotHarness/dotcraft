using System.Text;
using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private sealed record PageCursor(string Kind, string? Thread, string? Turn, string Direction,
        string? Source, int Offset = 0, string? Anchor = null, bool Inclusive = false);
    private sealed record HistoryPage(
        [property: System.Text.Json.Serialization.JsonPropertyName("data")] object[] Data,
        [property: System.Text.Json.Serialization.JsonPropertyName("nextCursor")] string? NextCursor,
        [property: System.Text.Json.Serialization.JsonPropertyName("backwardsCursor")] string? BackwardsCursor);

    private static string EncodeCursor(PageCursor value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    private static PageCursor ParseCursor(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<PageCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(value)))
                ?? throw new ArgumentException("Invalid gateway history cursor.");
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            throw new ArgumentException("Invalid gateway history cursor.", e);
        }
    }

    private static PageCursor? DecodeCursor(string? value, string kind, string? thread, string? turn = null)
    {
        if (value is null) return null;
        var cursor = ParseCursor(value);
        if (cursor.Kind != kind || cursor.Thread != thread || cursor.Turn != turn || cursor.Offset < 0)
            throw new ArgumentException("History cursor belongs to a different scope.");
        return cursor;
    }

    private static string? WrapCursor(string kind, string? thread, string? turn, string direction, string? source) =>
        source is null ? null : EncodeCursor(new(kind, thread, turn, direction, source));

    private static PageCursor? ItemCursor(string? value, string thread, string? turn, string direction)
    {
        if (value is null) return null;
        var cursor = ParseCursor(value);
        if (cursor.Kind == "itemBootstrap")
        {
            if (cursor.Thread != thread || cursor.Turn is not null || cursor.Direction != direction
                || direction != "desc" || cursor.Offset != 0 || cursor.Source is null)
                throw new ArgumentException("Item bootstrap belongs to a different scope or direction.");
            return cursor with { Kind = "items", Turn = turn };
        }
        return DecodeCursor(value, "items", thread, turn);
    }

    private static string Direction(JsonElement p, string fallback)
    {
        var direction = p.Text("sortDirection") ?? fallback;
        return direction is "asc" or "desc" ? direction : throw new ArgumentException("sortDirection must be asc or desc.");
    }
}
