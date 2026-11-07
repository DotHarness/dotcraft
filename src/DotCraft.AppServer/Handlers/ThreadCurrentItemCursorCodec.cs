using System.Text.Json;

namespace DotCraft.AppServer;

internal static class ThreadCurrentItemCursorCodec
{
    private sealed record Cursor(string Scope, string ThreadId, string TurnId, string Direction, string AfterItemId);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Encode(string threadId, string turnId, string direction, string afterItemId) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new Cursor("current-items", threadId, turnId, direction, afterItemId), Options))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string? Decode(string? cursor, string threadId, string? turnId, string direction)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var token = cursor.Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(token));
            if (!document.RootElement.TryGetProperty("scope", out var scope)
                || scope.GetString() != "current-items")
                return null;
            var value = document.Deserialize<Cursor>(Options);
            if (value is null || value.ThreadId != threadId || value.TurnId != turnId
                || value.Direction != direction || string.IsNullOrWhiteSpace(value.AfterItemId))
                throw AppServerErrors.InvalidParams("'cursor' does not match the requested current Item scope.");
            return value.AfterItemId;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }
    }
}
