using System.Text;
using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal static class ThreadMessageSearchCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(string threadId, string searchTerm, ThreadMessageSearchCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new CursorPayload(1, threadId, searchTerm, cursor.ItemOrdinal, cursor.TextOffset), JsonOptions))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static ThreadMessageSearchCursor? Decode(string? cursor, string threadId, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        CursorPayload? payload;
        try
        {
            var token = cursor.Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(token), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }

        if (payload is { Version: 1, ItemOrdinal: > 0, TextOffset: >= 0 }
            && string.Equals(payload.ThreadId, threadId, StringComparison.Ordinal)
            && string.Equals(payload.SearchTerm, searchTerm, StringComparison.Ordinal))
            return new ThreadMessageSearchCursor(payload.ItemOrdinal, payload.TextOffset);
        throw AppServerErrors.InvalidParams("'cursor' does not match the requested thread and search term.");
    }

    private sealed record CursorPayload(int Version, string ThreadId, string SearchTerm, long ItemOrdinal, int TextOffset);
}
