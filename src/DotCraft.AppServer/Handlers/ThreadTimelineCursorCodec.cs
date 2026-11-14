using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal static class ThreadTimelineCursorCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private sealed record Payload(int Version, string ThreadId, string Scope, ThreadTimelineCursor Key);

    public static string Encode(string threadId, ThreadTimelineCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(1, threadId, "timeline", cursor), Options))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static ThreadTimelineCursor? Decode(string? cursor, string threadId)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            var payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(token), Options);
            if (payload is { Version: 1, Scope: "timeline", Key: { } key }
                && payload.ThreadId == threadId && key.Position > 0 && key.SnapshotOrdinal >= key.Position
                && Enum.IsDefined(key.Kind) && !string.IsNullOrWhiteSpace(key.TurnId)
                && (key.Kind == ThreadTimelineEntryKind.Item ? !string.IsNullOrWhiteSpace(key.ItemId) : key.ItemId == ""))
                return key;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }
        throw AppServerErrors.InvalidParams("'cursor' does not match the requested timeline scope.");
    }
}
