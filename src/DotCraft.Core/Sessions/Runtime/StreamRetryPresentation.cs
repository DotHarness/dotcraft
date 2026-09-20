using DotCraft.Agents;

namespace DotCraft.Sessions;

internal static class StreamRetryPresentation
{
    internal const string ReconnectingKey = "system.streamError";
    internal const string ServerBusyKey = "system.streamError.serverBusy";

    internal static (string MessageKey, string FallbackText, IReadOnlyDictionary<string, object?> Params) For(
        ModelStreamRetryNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var classification = notification.Classification;
        var serverBusy = classification?.Kind is ProviderFailureKind.ServerOverloaded
                             or ProviderFailureKind.RateLimitExceeded
                         || classification?.HttpStatus == 429;

        var parameters = new Dictionary<string, object?>
        {
            ["attempt"] = notification.Attempt,
            ["max"] = notification.MaxAttempts
        };
        if (classification is not null)
        {
            parameters["providerError"] = ProviderFailureKinds.ToWireName(classification.Kind);
            if (classification.HttpStatus is { } status)
                parameters["httpStatus"] = status;
        }

        var detail = notification.Failure.Message;
        if (!string.IsNullOrWhiteSpace(detail))
            parameters["detail"] = detail;

        return serverBusy
            ? (ServerBusyKey,
                $"Server is busy, reconnecting... {notification.Attempt}/{notification.MaxAttempts}",
                parameters)
            : (ReconnectingKey,
                $"Reconnecting... {notification.Attempt}/{notification.MaxAttempts}",
                parameters);
    }
}
