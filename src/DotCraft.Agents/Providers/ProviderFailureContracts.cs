using System.Net;
using System.Net.Sockets;
using DotCraft.Context.Compaction;

namespace DotCraft.Agents;

public enum ProviderFailureKind
{
    Other = 0,
    ContextWindowExceeded,
    UsageLimitExceeded,
    RateLimitExceeded,
    ServerOverloaded,
    InternalServerError,
    Unauthorized,
    BadRequest,
    HttpConnectionFailed,
    ResponseStreamConnectionFailed,
    ResponseStreamDisconnected,
    ResponseTooManyFailedAttempts
}

/// <summary>Wire names for <see cref="ProviderFailureKind"/>; these are part of the client contract.</summary>
public static class ProviderFailureKinds
{
    public static string ToWireName(ProviderFailureKind kind) => kind switch
    {
        ProviderFailureKind.ContextWindowExceeded => "contextWindowExceeded",
        ProviderFailureKind.UsageLimitExceeded => "usageLimitExceeded",
        ProviderFailureKind.RateLimitExceeded => "rateLimitExceeded",
        ProviderFailureKind.ServerOverloaded => "serverOverloaded",
        ProviderFailureKind.InternalServerError => "internalServerError",
        ProviderFailureKind.Unauthorized => "unauthorized",
        ProviderFailureKind.BadRequest => "badRequest",
        ProviderFailureKind.HttpConnectionFailed => "httpConnectionFailed",
        ProviderFailureKind.ResponseStreamConnectionFailed => "responseStreamConnectionFailed",
        ProviderFailureKind.ResponseStreamDisconnected => "responseStreamDisconnected",
        ProviderFailureKind.ResponseTooManyFailedAttempts => "responseTooManyFailedAttempts",
        _ => "other"
    };
}

public sealed record ProviderFailure(
    ProviderFailureKind Kind,
    int? HttpStatus = null,
    string? ProviderErrorCode = null,
    string? RequestId = null,
    TimeSpan? ServerRetryAfter = null,
    DateTimeOffset? ResetsAt = null)
{
    private const int InitialDelayMs = 200;

    /// <summary>
    /// An unrecognized failure carrying no HTTP status did not come from the wire, so reissuing
    /// it would hide a local defect behind a retry.
    /// </summary>
    public bool IsTerminal => Kind is ProviderFailureKind.ContextWindowExceeded
        or ProviderFailureKind.UsageLimitExceeded
        or ProviderFailureKind.ServerOverloaded
        or ProviderFailureKind.Unauthorized
        or ProviderFailureKind.BadRequest
        or ProviderFailureKind.ResponseTooManyFailedAttempts
        || (Kind is ProviderFailureKind.Other && HttpStatus is null);

    public TimeSpan? GetRetryDelay(int attemptNumber) =>
        IsTerminal ? null : ServerRetryAfter ?? Backoff(attemptNumber);

    /// <summary>
    /// Rate limiting is excluded because replaying it immediately cannot succeed; the turn layer
    /// owns that wait.
    /// </summary>
    public bool AllowsTransportReplay => Kind switch
    {
        ProviderFailureKind.InternalServerError => true,
        ProviderFailureKind.HttpConnectionFailed => true,
        ProviderFailureKind.ResponseStreamConnectionFailed => true,
        ProviderFailureKind.ResponseStreamDisconnected => true,
        ProviderFailureKind.Other => HttpStatus >= 500,
        _ => false
    };

    public static TimeSpan Backoff(int attemptNumber)
    {
        var exponent = Math.Pow(2, Math.Max(0, attemptNumber - 1));
        var baseDelay = InitialDelayMs * exponent;
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(baseDelay * jitter);
    }
}

public interface IProviderFailureClassifier
{
    ProviderFailure Classify(Exception exception);
}

public interface IStreamRetryBudget
{
    int MaxStreamRetries { get; }
}

/// <summary>Used when a provider integration registers no classifier of its own.</summary>
public sealed class DefaultProviderFailureClassifier : IProviderFailureClassifier
{
    public static readonly DefaultProviderFailureClassifier Instance = new();

    public ProviderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsStreamDisconnect(exception))
            return new ProviderFailure(ProviderFailureKind.ResponseStreamDisconnected);

        if (CompactionErrors.IsPromptTooLongMessage(exception.Message))
            return new ProviderFailure(ProviderFailureKind.ContextWindowExceeded);

        var status = TryReadStatusCode(exception);
        if (status.HasValue)
            return new ProviderFailure(FromStatusCode(status.Value), (int)status.Value);

        if (exception is TimeoutException or OperationCanceledException)
            return new ProviderFailure(ProviderFailureKind.ResponseStreamDisconnected);

        if (exception is HttpRequestException
            or IOException
            or SocketException
            || ContainsInner<IOException>(exception)
            || ContainsInner<SocketException>(exception))
        {
            return new ProviderFailure(ProviderFailureKind.HttpConnectionFailed);
        }

        return new ProviderFailure(ProviderFailureKind.Other);
    }

    public static ProviderFailureKind FromStatusCode(HttpStatusCode statusCode) => (int)statusCode switch
    {
        400 => ProviderFailureKind.BadRequest,
        401 or 403 => ProviderFailureKind.Unauthorized,
        408 => ProviderFailureKind.ResponseStreamDisconnected,
        429 => ProviderFailureKind.RateLimitExceeded,
        >= 500 => ProviderFailureKind.InternalServerError,
        _ => ProviderFailureKind.Other
    };

    public static bool IsStreamDisconnect(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (string.Equals(current.GetType().Name, "ResponseEnded", StringComparison.Ordinal)
                || ContainsInvariant(current.Message, "response ended prematurely")
                || ContainsInvariant(current.Message, "response ended before")
                || ContainsInvariant(current.Message, "stream ended prematurely")
                || ContainsInvariant(current.Message, "Provider stream idle for"))
            {
                return true;
            }
        }

        return false;
    }

    public static HttpStatusCode? TryReadStatusCode(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } requestStatus })
                return requestStatus;

            var statusCode = TryReadStatusCodeProperty(current, "StatusCode")
                ?? TryReadStatusCodeProperty(current, "Status");
            if (statusCode.HasValue)
                return statusCode.Value;
        }

        return null;
    }

    public static bool ContainsInner<T>(Exception exception)
        where T : Exception
    {
        for (var current = exception.InnerException; current != null; current = current.InnerException)
        {
            if (current is T)
                return true;
        }

        return false;
    }

    private static HttpStatusCode? TryReadStatusCodeProperty(Exception exception, string propertyName)
    {
        var property = exception.GetType().GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property == null)
            return null;

        return property.GetValue(exception) switch
        {
            HttpStatusCode statusCode => statusCode,
            int status => (HttpStatusCode)status,
            _ => null
        };
    }

    private static bool ContainsInvariant(string? value, string needle) =>
        value?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
