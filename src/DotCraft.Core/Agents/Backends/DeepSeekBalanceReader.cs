using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.Agents;

internal sealed class DeepSeekBalanceReader(HttpClient client, TimeProvider time)
{
    public const string InsufficientBalanceKind = "balance";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, CachedRead> _cache = new(StringComparer.Ordinal);

    public async Task<ProviderUsageSnapshot?> ReadAsync(string apiKey, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(apiKey, out var cached) && now - cached.At < CacheDuration)
            return cached.Snapshot;
        var snapshot = await FetchAsync(apiKey, ct).ConfigureAwait(false);
        _cache[apiKey] = new CachedRead(now, snapshot);
        return snapshot;
    }

    private async Task<ProviderUsageSnapshot?> FetchAsync(string apiKey, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProviderWebSearchBackend.DeepSeekBalanceEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            return Parse(body.RootElement);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private ProviderUsageSnapshot? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("is_available", out var available) || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return null;
        var balances = new List<ProviderBalance>();
        foreach (var info in infos.EnumerateArray())
        {
            if (Read(info, "currency") is not { } currency || Read(info, "total_balance") is not { } total)
                continue;
            balances.Add(new ProviderBalance(currency, total, Read(info, "granted_balance") ?? "0", Read(info, "topped_up_balance") ?? "0"));
        }
        return new ProviderUsageSnapshot(
            time.GetUtcNow(),
            new Dictionary<string, double>(),
            LimitReachedKind: available.GetBoolean() ? null : InsufficientBalanceKind,
            Balances: balances);
    }

    private static string? Read(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private sealed record CachedRead(DateTimeOffset At, ProviderUsageSnapshot? Snapshot);
}
