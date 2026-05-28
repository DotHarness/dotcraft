namespace DotCraft.Sessions;

/// <summary>
/// Thrown when a pending request is evicted from a session queue because
/// the queue has reached its maximum size and a newer request arrived.
/// </summary>
public sealed class SessionGateOverflowException : Exception
{
    public string SessionId { get; }

    public SessionGateOverflowException(string sessionId)
        : base($"Request evicted from session queue '{sessionId}' due to overflow.")
    {
        SessionId = sessionId;
    }
}
