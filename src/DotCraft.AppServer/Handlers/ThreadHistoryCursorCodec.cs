using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal static class ThreadHistoryCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(
        string threadId,
        string scope,
        string? turnId,
        string direction,
        long exclusiveOrdinal,
        long? snapshotOrdinal = null) =>
        Serialize(new CursorPayload(1, threadId, scope, turnId, direction, exclusiveOrdinal, null, snapshotOrdinal));

    public static string EncodeBackwards(string threadId, string scope, long inclusiveOrdinal, long? snapshotOrdinal = null) =>
        Serialize(new CursorPayload(1, threadId, scope, null, null, null, inclusiveOrdinal, snapshotOrdinal));

    public static string EncodeBootstrap(string threadId, string scope, ThreadHistoryCursor anchor) =>
        Serialize(new CursorPayload(1, threadId, scope == "items" ? "item-bootstrap" : scope,
            null, "descending", null, anchor.RolloutOrdinal, anchor.SnapshotOrdinal));

    public static ThreadHistoryCursor? Decode(
        string? cursor,
        string threadId,
        string scope,
        string? turnId,
        string direction)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        CursorPayload? payload;
        try
        {
            var token = cursor.Trim().Replace('-', '+').Replace('_', '/');
            token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
            payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(token), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            throw AppServerErrors.InvalidParams("'cursor' is invalid.");
        }

        if (payload is { Version: 1, Scope: "item-bootstrap", TurnId: null, Direction: "descending",
                InclusiveRolloutOrdinal: > 0 and long bootstrap, ExclusiveRolloutOrdinal: null,
                SnapshotOrdinal: > 0 and long snapshot }
            && snapshot >= bootstrap && payload.ThreadId == threadId && direction == "descending"
            && scope is "items" or "turn-items")
            return new ThreadHistoryCursor(bootstrap, Inclusive: true, SnapshotOrdinal: snapshot);

        if (payload is not null
            && payload.Version == 1
            && string.Equals(payload.ThreadId, threadId, StringComparison.Ordinal)
            && string.Equals(payload.Scope, scope, StringComparison.Ordinal)
            && string.Equals(payload.TurnId, turnId, StringComparison.Ordinal))
        {
            if (payload is { InclusiveRolloutOrdinal: > 0 and long anchor, ExclusiveRolloutOrdinal: null }
                && (payload.Direction is null || payload.Direction == direction)
                && (payload.SnapshotOrdinal is null || payload.SnapshotOrdinal >= anchor))
                return new ThreadHistoryCursor(anchor, Inclusive: true, SnapshotOrdinal: payload.SnapshotOrdinal);
            if (payload is { ExclusiveRolloutOrdinal: > 0 and long next, InclusiveRolloutOrdinal: null }
                && string.Equals(payload.Direction, direction, StringComparison.Ordinal)
                && (payload.SnapshotOrdinal is null || payload.SnapshotOrdinal >= next))
                return new ThreadHistoryCursor(next, SnapshotOrdinal: payload.SnapshotOrdinal);
        }
        throw AppServerErrors.InvalidParams("'cursor' does not match the requested history scope.");
    }

    private static string Serialize(CursorPayload payload) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record CursorPayload(
        int Version,
        string ThreadId,
        string Scope,
        string? TurnId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Direction,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExclusiveRolloutOrdinal,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? InclusiveRolloutOrdinal,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SnapshotOrdinal = null);
}
