namespace DotCraft.Sdk.Wire;

/// <summary>A received JSON-RPC message exceeded the local transport limit, not a server RPC error.</summary>
public sealed class JsonRpcMessageTooLargeException(long limitBytes, long bytesReceived)
    : IOException($"dotcraft.transport.message_too_large: AppServer message exceeds {limitBytes} bytes.")
{
    public long LimitBytes { get; } = limitBytes;
    public long BytesReceived { get; } = bytesReceived;
}
