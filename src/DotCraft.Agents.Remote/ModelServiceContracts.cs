using DotCraft.Configuration;

namespace DotCraft.Agents.Remote;

public sealed record ModelServiceConnection(Uri Endpoint, string Token);

public sealed record RemoteModelProvider(
    string Id,
    string DisplayName,
    string Protocol,
    string Endpoint,
    string AuthMethod,
    bool SupportsHostedImageGeneration,
    ProviderAuthenticationStatus Authentication,
    int? MaxOutputTokens = null,
    int NetworkTimeoutSeconds = 300,
    IReadOnlyDictionary<string, ProviderRequestAdaptation>? Models = null);

public sealed record ModelServiceCatalog(string CallerId, IReadOnlyList<RemoteModelProvider> Providers);

public sealed record ModelServiceRequestContext(
    string Model,
    ProviderConversationIdentity? Conversation,
    string? Operation = null);

public sealed record ModelServiceError(string Code, string Message);

public sealed class ModelServiceException(string code, string message, int status)
    : HttpRequestException(message, null, (System.Net.HttpStatusCode)status)
{
    public string Code { get; } = code;
}

public static class ModelServiceProtocol
{
    public const string ContextHeader = "X-DotCraft-Model-Context";
    public const string ErrorHeader = "X-DotCraft-Model-Error";

    public static bool IsRequestHeader(string name) =>
        name.Equals("Accept", StringComparison.OrdinalIgnoreCase)
        || name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
        || name.Equals("OpenAI-Beta", StringComparison.OrdinalIgnoreCase)
        || name.Equals("anthropic-version", StringComparison.OrdinalIgnoreCase)
        || name.Equals("anthropic-beta", StringComparison.OrdinalIgnoreCase)
        || name.Equals("originator", StringComparison.OrdinalIgnoreCase)
        || name.Equals("session-id", StringComparison.OrdinalIgnoreCase)
        || name.Equals("thread-id", StringComparison.OrdinalIgnoreCase)
        || name.Equals("x-client-request-id", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("x-codex-", StringComparison.OrdinalIgnoreCase);

    public static bool IsResponseHeader(string name) =>
        name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ETag", StringComparison.OrdinalIgnoreCase)
        || name.Equals("request-id", StringComparison.OrdinalIgnoreCase)
        || name.Equals("x-request-id", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("x-codex-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("x-ratelimit-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("anthropic-ratelimit-", StringComparison.OrdinalIgnoreCase);
}
