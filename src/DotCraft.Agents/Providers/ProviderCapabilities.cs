using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Lists models for one provider runtime.</summary>
public interface IModelCatalogProvider
{
    Task<ModelCatalogResult> FetchModelsAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken);
}

/// <summary>Optional interactive authentication capability exposed by a provider.</summary>
public interface IProviderAuthentication
{
    ProviderAuthenticationStatus GetStatus();

    Task<ProviderAuthenticationStatus> LoginAsync(
        ProviderLoginRequest request,
        CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

/// <summary>Provider-neutral interactive login options.</summary>
public sealed record ProviderLoginRequest(
    bool OpenBrowser,
    Func<ProviderAuthorizationRequest, ValueTask>? AuthorizationRequestAvailable = null);

/// <summary>Provider-neutral authorization URL and its selected local callback port.</summary>
public sealed record ProviderAuthorizationRequest(
    Uri AuthorizationUrl,
    int CallbackPort);

/// <summary>Provider-neutral authentication state.</summary>
public sealed record ProviderAuthenticationStatus(
    bool IsAuthenticated,
    string? AccountId = null,
    string? DisplayName = null,
    string? FailureCode = null,
    string? FailureMessage = null,
    string? PlanType = null,
    string? Email = null,
    DateTimeOffset? LastRefresh = null,
    DateTimeOffset? AccessTokenExpiresAt = null);

/// <summary>Reads an optional provider account-usage snapshot.</summary>
public interface IProviderUsageReader
{
    Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Starts and stops provider-owned background services at host boundaries.</summary>
public interface IProviderLifecycle : IAsyncDisposable
{
    void Start();
}

/// <summary>Resolves provider-owned runtime identity such as an OAuth account binding.</summary>
public interface IProviderRuntimeIdentityResolver
{
    string? ResolveAccountId(EffectiveModelRuntime runtime);
}

/// <summary>Provider-owned model metadata that affects Core orchestration.</summary>
public sealed record ProviderRuntimeMetadata(bool UseLightweightResponses = false);

/// <summary>Resolves provider-owned runtime metadata without exposing provider catalog types.</summary>
public interface IProviderRuntimeMetadataResolver
{
    ProviderRuntimeMetadata Resolve(EffectiveModelRuntime runtime);
}

/// <summary>Provider-neutral account usage values for control-plane projection.</summary>
public sealed record ProviderUsageSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string>? Labels = null,
    string? PlanType = null,
    ProviderRateLimitWindow? Primary = null,
    ProviderRateLimitWindow? Secondary = null,
    ProviderCreditStatus? Credits = null,
    string? LimitReachedKind = null);

public sealed record ProviderRateLimitWindow(
    int UsedPercent,
    TimeSpan WindowDuration,
    DateTimeOffset ResetAt);

public sealed record ProviderCreditStatus(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

/// <summary>Generates or edits images through an optional provider capability.</summary>
public interface IProviderImageGeneration
{
    Task<ProviderImageResult> GenerateImageAsync(
        EffectiveModelRuntime runtime,
        ProviderImageRequest request,
        CancellationToken cancellationToken);
}

public sealed record ProviderImageRequest(
    string Model,
    string Prompt,
    bool TransparentBackground,
    IReadOnlyList<string> ReferenceImageUrls,
    string? TurnId);

public sealed record ProviderImageResult(
    byte[] Image,
    string? ImagegenRequestId = null,
    string? GenerationId = null);

public sealed class ProviderImageException(string message, string? imagegenRequestId, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string? ImagegenRequestId { get; } = imagegenRequestId;
}

/// <summary>Executes provider-native history compaction for one configured model runtime.</summary>
public interface IProviderNativeCompactor
{
    Task<ProviderNativeCompactionReplacement> CompactAsync(
        ProviderNativeCompactionInput input,
        IReadOnlyList<ChatMessage> neutralHistory,
        ChatOptions? options,
        CancellationToken cancellationToken);
}

/// <summary>Creates a provider-native compactor for an immutable runtime.</summary>
public interface IProviderNativeCompactorFactory
{
    IProviderNativeCompactor CreateCompactor(
        EffectiveModelRuntime runtime,
        IChatClient? rawRepresentationClient = null);
}

/// <summary>Provider-neutral request-shaping options selected by Core.</summary>
public sealed record ProviderPipelineOptions(
    EffectiveModelRuntime Runtime,
    string? ReasoningEffort,
    string? ReasoningOutput,
    bool ReasoningEnabled,
    string InferenceSpeed,
    bool PromptCachingEnabled,
    string? PromptCacheTtl);

/// <summary>Flows Core-selected provider request options into provider-owned middleware.</summary>
public static class ProviderPipelineOptionsScope
{
    private static readonly AsyncLocal<ProviderPipelineOptions?> CurrentOptions = new();
    public static ProviderPipelineOptions? Current => CurrentOptions.Value;

    public static IDisposable Push(ProviderPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = CurrentOptions.Value;
        CurrentOptions.Value = options;
        return new Scope(previous);
    }

    private sealed class Scope(ProviderPipelineOptions? previous) : IDisposable
    {
        public void Dispose() => CurrentOptions.Value = previous;
    }
}
