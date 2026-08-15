namespace DotCraft.AppBinding;

/// <summary>Identifies a stable App Binding domain failure.</summary>
public enum AppBindingError
{
    InvalidInput,
    Unauthorized,
    Conflict,
    PolicyDenied,
    SurfaceUnavailable
}

/// <summary>Represents an App Binding failure without coupling the domain to a transport.</summary>
public sealed class AppBindingException(
    AppBindingError error,
    string message,
    string? appId = null,
    string? surfaceId = null) : Exception(message)
{
    public AppBindingError Error { get; } = error;

    public string? AppId { get; } = appId;

    public string? SurfaceId { get; } = surfaceId;
}

internal static class AppBindingErrors
{
    public static AppBindingException InvalidInput(string message) =>
        new(AppBindingError.InvalidInput, message);

    public static AppBindingException Unauthorized(string message) =>
        new(AppBindingError.Unauthorized, message);

    public static AppBindingException Conflict(string message) =>
        new(AppBindingError.Conflict, message);

    public static AppBindingException PolicyDenied(string message) =>
        new(AppBindingError.PolicyDenied, message);

    public static AppBindingException SurfaceUnavailable(string appId, string surfaceId) =>
        new(AppBindingError.SurfaceUnavailable, "The requested app surface is unavailable.", appId, surfaceId);
}

/// <summary>Describes an App Binding state change raised by the domain coordinator.</summary>
public sealed record AppBindingStatusChanged(
    string ThreadId,
    string BindingId,
    string AppId,
    string State,
    string? FailureReason,
    long AuthorityRevision);
