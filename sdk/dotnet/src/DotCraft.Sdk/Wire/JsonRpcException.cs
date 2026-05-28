using System.Text.Json;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// Exception raised when a DotCraft JSON-RPC endpoint returns an error response.
/// </summary>
public sealed class JsonRpcException(int code, string message, JsonElement? data = null) : Exception(message)
{
    /// <summary>
    /// JSON-RPC error code.
    /// </summary>
    public int Code { get; } = code;

    /// <summary>
    /// Optional error data supplied by the server.
    /// </summary>
    public JsonElement? ErrorData { get; } = data;

    internal static JsonRpcException FromError(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            ? codeElement.GetInt32()
            : -32603;
        var message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString() ?? "Unknown JSON-RPC error."
            : "Unknown JSON-RPC error.";
        var data = error.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : (JsonElement?)null;
        return new JsonRpcException(code, message, data);
    }
}
