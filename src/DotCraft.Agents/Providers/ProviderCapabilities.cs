using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public interface IModelCatalogProvider
{
    Task<ModelCatalogResult> FetchModelsAsync(
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken);
}

public interface IProviderCachedModelCatalog
{
    ModelCatalogResult ReadCatalog();
}

public interface IProviderAccessTokenReader
{
    Task<string?> TryGetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}

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

public interface IProviderUsageReader
{
    Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

public interface IProviderLifecycle : IAsyncDisposable
{
    void Start();
}

public interface IProviderRuntimeIdentityResolver
{
    string? ResolveAccountId(EffectiveModelRuntime runtime);
}

public sealed record ProviderContextBudget(
    int MaxContextWindow,
    int EffectiveContextWindowPercent = 95,
    int? AutoCompactTokenLimit = null);

public sealed record ProviderRuntimeMetadata(
    bool UseLightweightResponses = false,
    ProviderContextBudget? ContextBudget = null);

public interface IProviderRuntimeMetadataResolver
{
    ProviderRuntimeMetadata Resolve(EffectiveModelRuntime runtime);
}

public sealed record ProviderUsageSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string>? Labels = null,
    string? PlanType = null,
    ProviderRateLimitWindow? Primary = null,
    ProviderRateLimitWindow? Secondary = null,
    ProviderCreditStatus? Credits = null,
    string? LimitReachedKind = null,
    IReadOnlyList<ProviderBalance>? Balances = null,
    string? PluginId = null);

public sealed record ProviderRateLimitWindow(
    int UsedPercent,
    TimeSpan WindowDuration,
    DateTimeOffset ResetAt);

public sealed record ProviderCreditStatus(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record ProviderBalance(
    string Currency,
    string Total,
    string Granted,
    string ToppedUp);

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

public interface IProviderWebSearch
{
    Task<ProviderWebSearchResult> SearchAsync(
        EffectiveModelRuntime runtime,
        ProviderWebSearchRequest request,
        CancellationToken cancellationToken);
}

public sealed record ProviderWebSearchRequest(
    string Id,
    JsonArray Input,
    JsonObject Commands);

public sealed record ProviderWebSearchResult(
    string Output,
    JsonArray Results);

public interface IProviderNativeCompactor
{
    Task<ProviderNativeCompactionReplacement> CompactAsync(
        ProviderNativeCompactionInput input,
        IReadOnlyList<ChatMessage> neutralHistory,
        ChatOptions? options,
        CancellationToken cancellationToken);
}

public interface IProviderNativeCompactorFactory
{
    IProviderNativeCompactor CreateCompactor(
        EffectiveModelRuntime runtime,
        IChatClient? rawRepresentationClient = null);
}

public sealed record ProviderPipelineOptions(
    EffectiveModelRuntime Runtime,
    string? ReasoningEffort,
    string? ReasoningOutput,
    bool ReasoningEnabled,
    string InferenceSpeed,
    bool PromptCachingEnabled,
    string? PromptCacheTtl);

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
