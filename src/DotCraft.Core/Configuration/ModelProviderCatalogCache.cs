using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;

namespace DotCraft.Configuration;

public enum ModelCatalogRefreshStrategy
{
    OnlineIfUncached,
    Online
}

/// <summary>
/// Caches catalogs per provider identity, and keeps serving the last catalog fetched under an identity
/// while refreshing it fails.
/// </summary>
internal sealed class ModelProviderCatalogCache(TimeProvider? timeProvider = null)
{
    internal static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public static string BuildIdentity(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return string.Join(
            '\n',
            runtime.ProviderId,
            runtime.Protocol,
            runtime.EndPoint.TrimEnd('/').ToLowerInvariant(),
            runtime.AuthMethod,
            runtime.ChatGptAccountId ?? string.Empty,
            Fingerprint(runtime.ApiKey));
    }

    public async Task<ModelCatalogResult> GetOrFetchAsync(
        string identity,
        ModelCatalogRefreshStrategy refreshStrategy,
        Func<CancellationToken, Task<ModelCatalogResult>> fetchAsync,
        CancellationToken cancellationToken)
    {
        var entry = _entries.GetOrAdd(identity, static _ => new CacheEntry());
        // The gate also collapses concurrent listings for one identity into a single fetch.
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (refreshStrategy == ModelCatalogRefreshStrategy.OnlineIfUncached && TryRead(entry, out var cached))
                return cached;

            var result = await fetchAsync(cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            if (result.Success)
            {
                entry.Catalog = Clone(result);
                entry.CatalogAt = now;
                entry.Failure = null;
                return result;
            }

            entry.Failure = Clone(result);
            entry.FailureAt = now;
            return entry.Catalog is null ? result : Clone(entry.Catalog);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private bool TryRead(CacheEntry entry, out ModelCatalogResult result)
    {
        var now = _timeProvider.GetUtcNow();
        var catalog = entry.Catalog;
        if (catalog != null && now - entry.CatalogAt <= CatalogTtl)
        {
            result = Clone(catalog);
            return true;
        }

        var failure = entry.Failure;
        if (failure != null && now - entry.FailureAt <= FailureRetryDelay)
        {
            result = Clone(catalog ?? failure);
            return true;
        }

        result = default!;
        return false;
    }

    private static string Fingerprint(string apiKey) =>
        apiKey.Length == 0
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static ModelCatalogResult Clone(ModelCatalogResult source) => new()
    {
        Success = source.Success,
        ErrorCode = source.ErrorCode,
        ErrorMessage = source.ErrorMessage,
        ProviderId = source.ProviderId,
        Protocol = source.Protocol,
        EndPoint = source.EndPoint,
        Models = source.Models
            .Select(static model => new ModelCatalogEntry
            {
                Id = model.Id,
                OwnedBy = model.OwnedBy,
                CreatedAt = model.CreatedAt
            })
            .ToList()
    };

    private sealed class CacheEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public ModelCatalogResult? Catalog { get; set; }

        public DateTimeOffset CatalogAt { get; set; }

        public ModelCatalogResult? Failure { get; set; }

        public DateTimeOffset FailureAt { get; set; }
    }
}
