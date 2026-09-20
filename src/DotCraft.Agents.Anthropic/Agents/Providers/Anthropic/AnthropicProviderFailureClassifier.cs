namespace DotCraft.Agents;

/// <summary>
/// The Anthropic SDK reports the provider's error type in the exception text rather than as
/// structured data, so the wire code is read from there.
/// </summary>
internal sealed class AnthropicProviderFailureClassifier : IProviderFailureClassifier
{
    internal static readonly AnthropicProviderFailureClassifier Instance = new();

    public ProviderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var fallback = DefaultProviderFailureClassifier.Instance.Classify(exception);
        var detail = Detail(exception);
        var status = fallback.HttpStatus;

        var kind = ResolveKind(detail, status);
        if (kind is null)
            return fallback;

        return fallback with
        {
            Kind = kind.Value,
            ProviderErrorCode = ProviderErrorCode(detail),
            ServerRetryAfter = kind is ProviderFailureKind.RateLimitExceeded
                ? ProviderFailureParsing.ParseRetryAfterFromMessage(detail)
                : null
        };
    }

    private static ProviderFailureKind? ResolveKind(string detail, int? status)
    {
        // The provider's own capacity signals outrank the status, which is 200 for an in-band
        // frame and 529 for a rejected request.
        if (ProviderFailureParsing.Mentions(detail, "overloaded_error")
            || ProviderFailureParsing.Mentions(detail, "insufficient_system_resource")
            || status == 529)
        {
            return ProviderFailureKind.ServerOverloaded;
        }

        if (ProviderFailureParsing.Mentions(detail, "authentication_error")
            || ProviderFailureParsing.Mentions(detail, "permission_error"))
        {
            return ProviderFailureKind.Unauthorized;
        }

        if (ProviderFailureParsing.Mentions(detail, "billing_error")
            || ProviderFailureParsing.LooksLikeQuotaExhausted(detail))
        {
            return ProviderFailureKind.UsageLimitExceeded;
        }

        if (ProviderFailureParsing.Mentions(detail, "rate_limit_error") || status == 429)
            return ProviderFailureKind.RateLimitExceeded;

        if (ProviderFailureParsing.Mentions(detail, "invalid_request_error"))
            return ProviderFailureKind.BadRequest;

        return null;
    }

    private static string? ProviderErrorCode(string detail)
    {
        foreach (var code in (string[])
                 [
                     "overloaded_error",
                     "rate_limit_error",
                     "authentication_error",
                     "permission_error",
                     "billing_error",
                     "invalid_request_error",
                     "insufficient_system_resource",
                     "api_error"
                 ])
        {
            if (ProviderFailureParsing.Mentions(detail, code))
                return code;
        }

        return null;
    }

    private static string Detail(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
            parts.Add(current.Message);
        return string.Join(' ', parts);
    }
}
