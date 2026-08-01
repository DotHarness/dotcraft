namespace DotCraft.Sdk.Wire;

/// <summary>Observable state of an AppServer Wire session.</summary>
public enum WireConnectionState
{
    /// <summary>The transport is opening.</summary>
    Connecting,
    /// <summary>The initialize handshake is running.</summary>
    Initializing,
    /// <summary>The Wire session can carry application messages.</summary>
    Ready,
    /// <summary>The transport ended unexpectedly.</summary>
    Disconnected,
    /// <summary>A replacement transport is being opened.</summary>
    Reconnecting,
    /// <summary>A reconnect attempt failed.</summary>
    ReconnectError,
    /// <summary>The client was explicitly closed.</summary>
    Closed
}

/// <summary>Raised when a Wire request exceeds its configured timeout.</summary>
public sealed class WireRequestTimeoutException(string method, TimeSpan timeout)
    : TimeoutException($"Request '{method}' timed out after {timeout.TotalMilliseconds:0}ms.")
{
    /// <summary>The JSON-RPC method that timed out.</summary>
    public string Method { get; } = method;

    /// <summary>The effective request timeout.</summary>
    public TimeSpan Timeout { get; } = timeout;
}

/// <summary>Raised when the reconnect request queue has reached its configured capacity.</summary>
public sealed class WireReconnectQueueFullException(int capacity)
    : InvalidOperationException($"Wire reconnect request queue is full (capacity {capacity}).")
{
    /// <summary>Maximum number of requests accepted while reconnecting.</summary>
    public int Capacity { get; } = capacity;
}

/// <summary>Connection lifecycle behavior for a raw Wire client.</summary>
public sealed class DotCraftWireClientOptions
{
    /// <summary>Reconnect and repeat initialize after an unexpected close.</summary>
    public bool AutoReconnect { get; init; }

    /// <summary>Default timeout for ordinary requests.</summary>
    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum requests waiting for a reconnect handshake.</summary>
    public int MaxReconnectQueueSize { get; init; } = 1024;

    /// <summary>Initial reconnect delay.</summary>
    public TimeSpan ReconnectInitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum reconnect delay.</summary>
    public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(30);
}
