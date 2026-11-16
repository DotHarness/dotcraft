using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using DotCraft.Context.Compaction;

namespace DotCraft.Agents;

internal sealed class AnthropicProviderFailureClassifier : IProviderFailureClassifier
{
    internal static readonly AnthropicProviderFailureClassifier Instance = new();

    public ProviderFailure Classify(Exception exception)
    {
        if (exception is ProviderFailureException carried)
            return carried.Failure;

        var fallback = DefaultProviderFailureClassifier.Instance.Classify(exception);
        var service = FindServiceException(exception);
        if (service is null)
            return fallback;

        var api = service as AnthropicApiException;
        var message = service.ApiMessage;
        var type = service.Type is { Json.ValueKind: JsonValueKind.String } errorType
            ? errorType.Json.GetString() : null;
        var code = service.ErrorCode;
        var status = api is not null ? (int)api.StatusCode : fallback.HttpStatus;
        var kind = ResolveKind(type, code, message) ?? status switch
        {
            529 => ProviderFailureKind.ServerOverloaded,
            { } value => DefaultProviderFailureClassifier.FromStatusCode((HttpStatusCode)value),
            _ => ProviderFailureKind.Other
        };

        return fallback with
        {
            Kind = kind,
            HttpStatus = status,
            ProviderErrorCode = code ?? type,
            RequestId = api?.RequestID ?? ReadRequestId(service.Body),
            Message = message,
            ServerRetryAfter = kind is ProviderFailureKind.RateLimitExceeded
                ? ProviderFailureParsing.ParseRetryAfterFromMessage(message)
                : null
        };
    }

    private static ProviderFailureKind? ResolveKind(string? type, string? code, string? message)
    {
        if (code == "context_length_exceeded")
            return ProviderFailureKind.ContextWindowExceeded;
        if (ProviderFailureParsing.LooksLikeQuotaExhausted(code))
            return ProviderFailureKind.UsageLimitExceeded;
        if (code == "insufficient_system_resource")
            return ProviderFailureKind.ServerOverloaded;

        return type switch
        {
            "overloaded_error" => ProviderFailureKind.ServerOverloaded,
            "authentication_error" or "permission_error" => ProviderFailureKind.Unauthorized,
            "billing_error" => ProviderFailureKind.UsageLimitExceeded,
            "rate_limit_error" => ProviderFailureKind.RateLimitExceeded,
            "invalid_request_error" when CompactionErrors.IsPromptTooLongMessage(message)
                => ProviderFailureKind.ContextWindowExceeded,
            "invalid_request_error" or "not_found_error" => ProviderFailureKind.BadRequest,
            "api_error" when ProviderFailureParsing.Mentions(message, "ValidationException")
                => ProviderFailureKind.BadRequest,
            "api_error" => ProviderFailureKind.InternalServerError,
            "timeout_error" => ProviderFailureKind.ResponseStreamDisconnected,
            _ => null
        };
    }

    private static AnthropicServiceException? FindServiceException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is AnthropicServiceException service)
                return service;
        }
        return null;
    }

    private static string? ReadRequestId(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("request_id", out var requestId)
        && requestId.ValueKind == JsonValueKind.String ? requestId.GetString() : null;
}
