namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Surface used by the OpenAI HTTP pipeline policy, CLI commands, AppServer JSON-RPC handlers,
/// and Desktop IPC bridge to authenticate ChatGPT subscription accounts.
/// </summary>
public interface IOpenAIAuthService
{
    /// <summary>True when a valid token bundle is on disk.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Snapshot of the currently logged-in account (no secrets).</summary>
    OpenAIAuthStatus GetStatus();

    /// <summary>
    /// Starts a PKCE login: opens a loopback listener, prints/launches the authorize URL, awaits
    /// the redirect, exchanges the code, persists the tokens. Returns the resulting status.
    /// </summary>
    /// <param name="openBrowser">When true, attempts to launch the system browser. Tests can disable this.</param>
    /// <param name="onAuthorizationUrl">Optional callback receiving the URL before the browser is opened.</param>
    Task<OpenAIAuthStatus> LoginAsync(
        bool openBrowser,
        Action<string>? onAuthorizationUrl,
        CancellationToken cancellationToken);

    /// <summary>Revokes the refresh token at the issuer and clears the local auth.json.</summary>
    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a fresh access token, refreshing automatically when it is older than
    /// <see cref="OpenAIAuthConstants.RefreshInterval"/> or when <paramref name="forceRefresh"/> is true.
    /// Throws <see cref="OpenAIAuthException"/> when no tokens are on disk or refresh fails permanently.
    /// </summary>
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>Account ID extracted from the most recent id_token; <c>null</c> when not signed in.</summary>
    string? GetAccountId();

    /// <summary>
    /// Raised after a successful login. Subscribers can use this to kick off background work
    /// scoped to the lifetime of a logged-in session (e.g. usage polling).
    /// </summary>
    event Action<OpenAIAuthStatus>? LoggedIn;

    /// <summary>
    /// Raised after a successful logout. Subscribers should tear down any session-scoped state.
    /// </summary>
    event Action? LoggedOut;
}

/// <summary>
/// Reads + caches the ChatGPT subscription rate-limit snapshot for the currently signed-in account.
/// Injected separately from <see cref="IOpenAIAuthService"/> so authentication and usage telemetry
/// have clean lifetime boundaries.
/// </summary>
public interface IOpenAIUsageService
{
    /// <summary>The most recent usage snapshot, or <c>null</c> if none has been fetched yet.</summary>
    OpenAIUsageSnapshot? CurrentSnapshot { get; }

    /// <summary>
    /// Forces an immediate refresh, bypassing the polling debounce. Returns the new snapshot
    /// on success, or <c>null</c> when the fetch fails (transient error, not signed in, etc.).
    /// Errors are logged but never thrown.
    /// </summary>
    Task<OpenAIUsageSnapshot?> RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fires whenever <see cref="CurrentSnapshot"/> changes. <c>null</c> signals "no longer
    /// available" (logout, repeated failure, etc.).
    /// </summary>
    event Action<OpenAIUsageSnapshot?>? SnapshotChanged;
}

/// <summary>
/// Raised when ChatGPT auth cannot be obtained or refreshed.
/// </summary>
public sealed class OpenAIAuthException : Exception
{
    public OpenAIAuthFailureReason Reason { get; }

    public OpenAIAuthException(OpenAIAuthFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public OpenAIAuthException(OpenAIAuthFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }
}

public enum OpenAIAuthFailureReason
{
    NotSignedIn,
    RefreshTokenExpired,
    RefreshTokenReused,
    RefreshTokenRevoked,
    Network,
    Unknown
}
