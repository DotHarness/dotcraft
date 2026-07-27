using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Shared helper for deserializing JSON-RPC request <c>params</c> into a typed wire DTO.
/// Used by the dispatcher and by every extracted domain handler so parameter handling is
/// identical across the AppServer surface.
/// </summary>
internal static class AppServerParams
{
    /// <summary>
    /// Deserializes <paramref name="msg"/>.<see cref="AppServerIncomingMessage.Params"/> into
    /// <typeparamref name="T"/>. Returns a fresh instance when params are absent or null, and
    /// throws an AppServer <c>InvalidParams</c> error on malformed JSON.
    /// </summary>
    public static T Get<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }
}
