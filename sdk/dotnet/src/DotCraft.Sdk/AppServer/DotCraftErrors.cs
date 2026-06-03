namespace DotCraft.Sdk.AppServer;

/// <summary>
/// AppServer JSON-RPC error codes. See AppServer Protocol, Section "Error Codes".
/// </summary>
public static class AppServerErrorCodes
{
    /// <summary>Method called before the initialize handshake.</summary>
    public const int NotInitialized = -32002;

    /// <summary>initialize called more than once on the same connection.</summary>
    public const int AlreadyInitialized = -32003;

    /// <summary>The specified threadId does not exist.</summary>
    public const int ThreadNotFound = -32010;

    /// <summary>Operation requires an active thread but the thread is paused or archived.</summary>
    public const int ThreadNotActive = -32011;

    /// <summary>A turn is already running or waiting for approval on this thread.</summary>
    public const int TurnInProgress = -32012;

    /// <summary>The specified turnId does not exist on the thread.</summary>
    public const int TurnNotFound = -32013;

    /// <summary>turn/interrupt called on a turn that is not in progress.</summary>
    public const int TurnNotRunning = -32014;

    /// <summary>The client took too long to respond to an approval request.</summary>
    public const int ApprovalTimeout = -32020;
}

/// <summary>
/// Base class for stable DotCraft SDK errors. The <see cref="Code"/> string is stable API.
/// </summary>
public class DotCraftSdkException : Exception
{
    /// <summary>Creates a DotCraft SDK exception with a stable code.</summary>
    public DotCraftSdkException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stable SDK error code.</summary>
    public string Code { get; }
}

/// <summary>The AppServer initialize handshake failed.</summary>
public sealed class InitializationError(string message, Exception? innerException = null)
    : DotCraftSdkException("initializationError", message, innerException);

/// <summary>The server rejected turn start because a turn is already running or waiting.</summary>
public sealed class TurnInProgressError(string message, Exception? innerException = null)
    : DotCraftSdkException("turnInProgress", message, innerException);

/// <summary>The specified thread does not exist.</summary>
public sealed class ThreadNotFoundError(string message, Exception? innerException = null)
    : DotCraftSdkException("threadNotFound", message, innerException);

/// <summary>The thread cannot accept turns because it is paused or archived.</summary>
public sealed class ThreadNotActiveError(string message, Exception? innerException = null)
    : DotCraftSdkException("threadNotActive", message, innerException);

/// <summary>Agent execution failed after turn/start succeeded.</summary>
public sealed class TurnFailedError(string message, string threadId, string? turnId, Exception? innerException = null)
    : DotCraftSdkException("turnFailed", message, innerException)
{
    /// <summary>Thread that owns the failed turn.</summary>
    public string ThreadId { get; } = threadId;

    /// <summary>Identifier of the failed turn when known.</summary>
    public string? TurnId { get; } = turnId;
}

/// <summary>The turn was cancelled before completing successfully.</summary>
public sealed class TurnCancelledError(string threadId, string? turnId, string? reason = null)
    : DotCraftSdkException("turnCancelled", reason ?? "The turn was cancelled.")
{
    /// <summary>Thread that owns the cancelled turn.</summary>
    public string ThreadId { get; } = threadId;

    /// <summary>Identifier of the cancelled turn when known.</summary>
    public string? TurnId { get; } = turnId;
}

/// <summary>The client did not answer an approval request in time.</summary>
public sealed class ApprovalTimeoutError(string message, Exception? innerException = null)
    : DotCraftSdkException("approvalTimeout", message, innerException);
