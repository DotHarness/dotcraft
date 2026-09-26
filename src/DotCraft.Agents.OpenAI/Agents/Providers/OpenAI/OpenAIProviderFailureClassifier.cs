using System.ClientModel;

namespace DotCraft.Agents;

internal sealed class OpenAIProviderFailureClassifier(bool isSubscriptionBackend) : IProviderFailureClassifier
{
    public ProviderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ProviderFailureException carried)
            return carried.Failure;

        var fallback = DefaultProviderFailureClassifier.Instance.Classify(exception);
        var result = FindClientResultException(exception);
        if (result is null)
            return Refine(fallback, Detail(exception), retryDeadline: null);

        var response = TryGetRawResponse(result);
        var retryDeadline = OpenAIRetryAdvicePipelinePolicy.Capture(response);
        var status = response?.Status ?? result.Status;
        var detail = $"{Detail(exception)} {TryReadBody(response)}";
        var baseline = fallback with
        {
            Kind = DefaultProviderFailureClassifier.FromStatusCode((System.Net.HttpStatusCode)status),
            HttpStatus = status,
            RequestId = TryGetHeader(response, "x-request-id")
        };

        return Refine(baseline, detail, retryDeadline);
    }

    private ProviderFailure Refine(ProviderFailure failure, string detail, ProviderRetryDeadline? retryDeadline)
    {
        var refined = failure with { Kind = ResolveKind(failure, detail) };
        if (refined.IsTerminal) return refined;
        if (retryDeadline is null && refined.Kind is ProviderFailureKind.RateLimitExceeded)
            retryDeadline = ProviderFailureParsing.ParseRetryAfterFromMessage(detail) is { } delay
                ? ProviderRetryDeadline.FromDelay(delay) : null;
        return refined with { ServerRetryAfter = retryDeadline?.Delay, RetryDeadline = retryDeadline };
    }

    private ProviderFailureKind ResolveKind(ProviderFailure failure, string detail)
    {
        if (ProviderFailureParsing.Mentions(detail, "server_is_overloaded")
            || ProviderFailureParsing.Mentions(detail, "insufficient_system_resource"))
        {
            return ProviderFailureKind.ServerOverloaded;
        }

        if (ProviderFailureParsing.Mentions(detail, "slow_down"))
            return ProviderFailureKind.RateLimitExceeded;

        if (failure.Kind is not ProviderFailureKind.RateLimitExceeded)
            return failure.Kind;

        if (ProviderFailureParsing.LooksLikeQuotaExhausted(detail))
            return ProviderFailureKind.UsageLimitExceeded;

        // The subscription backend answers ordinary plan throttling with a plain 429 that no
        // amount of waiting inside one turn clears, so it is surfaced rather than retried.
        return isSubscriptionBackend
            ? ProviderFailureKind.ResponseTooManyFailedAttempts
            : ProviderFailureKind.RateLimitExceeded;
    }

    private static ClientResultException? FindClientResultException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is ClientResultException result)
                return result;
        }

        return null;
    }

    private static System.ClientModel.Primitives.PipelineResponse? TryGetRawResponse(
        ClientResultException exception)
    {
        try
        {
            return exception.GetRawResponse();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryGetHeader(
        System.ClientModel.Primitives.PipelineResponse? response,
        string name) =>
        response is not null && response.Headers.TryGetValue(name, out var value) ? value : null;

    private static string TryReadBody(System.ClientModel.Primitives.PipelineResponse? response)
    {
        if (response is null)
            return string.Empty;

        try
        {
            return response.Content?.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Detail(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
            parts.Add(current.Message);
        return string.Join(' ', parts);
    }
}
