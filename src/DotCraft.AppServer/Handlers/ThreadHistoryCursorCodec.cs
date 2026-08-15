using System.Text;
using System.Text.Json;

namespace DotCraft.AppServer;

internal static class ThreadHistoryCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(
        string threadId,
        string scope,
        string? turnId,
        string direction,
        long exclusiveOrdinal)
    {
        var payload = new CursorPayload(1, threadId, scope, turnId, direction, exclusiveOrdinal);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static long? Decode(
        string? cursor,
        string threadId,
        string scope,
        string? turnId,
        string direction)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Convert.FromBase64String(token), JsonOptions);
            if (payload is null
                || payload.Version != 1
                || payload.ExclusiveRolloutOrdinal <= 0
                || !string.Equals(payload.ThreadId, threadId, StringComparison.Ordinal)
                || !string.Equals(payload.Scope, scope, StringComparison.Ordinal)
                || !string.Equals(payload.TurnId, turnId, StringComparison.Ordinal)
                || !string.Equals(payload.Direction, direction, StringComparison.Ordinal))
            {
                throw AppServerErrors.InvalidParams("'cursor' does not match the requested history scope.");
            }
            return payload.ExclusiveRolloutOrdinal;
        }
        catch (AppServerException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }
    }

    private sealed record CursorPayload(
        int Version,
        string ThreadId,
        string Scope,
        string? TurnId,
        string Direction,
        long ExclusiveRolloutOrdinal);
}
