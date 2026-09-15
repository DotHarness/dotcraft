using System.Collections.Concurrent;
using System.Net.Http.Headers;
using DotCraft.Configuration;
using DotCraft.Plugins.Marketplaces;

namespace DotCraft.Plugins;

/// <summary>
/// Downloads archive-backed plugin registry snapshots. This is the only place an archive source
/// reaches the network; plugin discovery reads whatever this service has already activated.
/// </summary>
public sealed class PluginRegistrySyncService
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshInterval = PluginSourceRegistryCatalog.ArchiveRefreshInterval;
    private const long MaximumDownloadBytes = 64L * 1024 * 1024;

    private static readonly HttpClient SharedClient = CreateClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

    private readonly ConcurrentDictionary<string, Task<PluginRegistrySyncResult>> _inFlight = new(StringComparer.Ordinal);
    private readonly string _craftHome;
    private readonly Action<PluginDiagnostic>? _diagnostic;
    private readonly HttpClient _client;

    public PluginRegistrySyncService(
        string craftHome,
        Action<PluginDiagnostic>? diagnostic = null,
        HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftHome);
        _craftHome = Path.GetFullPath(craftHome);
        _diagnostic = diagnostic;
        _client = handler == null ? SharedClient : CreateClient(handler);
    }

    /// <summary>
    /// Syncs every archive-backed registry that is due, including the host-provided default.
    /// Returns true when any source activated a new snapshot.
    /// </summary>
    public async Task<bool> SyncDueAsync(
        AppConfig.PluginsConfig? pluginsConfig,
        bool force,
        CancellationToken ct)
    {
        var activated = false;
        foreach (var source in PluginSourceRegistryCatalog.ArchiveSourcesFor(pluginsConfig))
        {
            var result = await SyncAsync(source.Url, source.MarketplacePath, source.Name, force, ct)
                .ConfigureAwait(false);
            activated |= result == PluginRegistrySyncResult.Activated;
        }

        return activated;
    }

    public Task<PluginRegistrySyncResult> SyncAsync(
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct)
    {
        var key = url + "\n" + marketplacePath;
        var pending = _inFlight.GetOrAdd(key, _ => RunAsync(url, marketplacePath, displayName, force, ct));
        if (pending.IsCompleted)
            _inFlight.TryRemove(new KeyValuePair<string, Task<PluginRegistrySyncResult>>(key, pending));
        return pending;
    }

    private async Task<PluginRegistrySyncResult> RunAsync(
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct)
    {
        try
        {
            return await DownloadAndActivateAsync(url, marketplacePath, displayName, force, ct).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(url + "\n" + marketplacePath, out _);
        }
    }

    private async Task<PluginRegistrySyncResult> DownloadAndActivateAsync(
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            Warn("InvalidPluginRegistrySourceUrl",
                $"Plugin marketplace '{displayName}' must be an HTTPS archive URL or a local archive/directory path.",
                url);
            return PluginRegistrySyncResult.Skipped;
        }

        var cache = new PluginRegistryArchiveCache(_craftHome);
        cache.CleanStaleTemporaryDirectories();
        if (!force && !IsDue(cache, url, marketplacePath))
            return PluginRegistrySyncResult.Skipped;

        byte[] bytes;
        try
        {
            bytes = await DownloadAsync(uri, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or InvalidOperationException
                                       or IOException)
        {
            cache.RecordFailedAttempt(url, marketplacePath);
            Warn("PluginRegistryDownloadFailed",
                $"Plugin marketplace '{displayName}' download failed: {ex.Message}",
                url);
            return PluginRegistrySyncResult.Failed;
        }

        try
        {
            cache.Activate(url, marketplacePath, bytes);
            return PluginRegistrySyncResult.Activated;
        }
        catch (Exception ex) when (ex is IOException
                                       or InvalidDataException
                                       or UnauthorizedAccessException
                                       or MarketplaceException)
        {
            cache.RecordFailedAttempt(url, marketplacePath);
            Warn("PluginRegistryExtractFailed",
                $"Plugin marketplace '{displayName}' archive could not be extracted: {ex.Message}",
                url);
            return PluginRegistrySyncResult.Failed;
        }
    }

    private static bool IsDue(PluginRegistryArchiveCache cache, string url, string marketplacePath)
    {
        if (!cache.ShouldAttempt(url, marketplacePath, RefreshInterval))
            return false;

        return !Directory.Exists(cache.SnapshotRootFor(url, marketplacePath))
               || cache.ShouldRefresh(url, marketplacePath, RefreshInterval);
    }

    private async Task<byte[]> DownloadAsync(Uri uri, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);

        using var response = await _client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
            throw new InvalidOperationException("The archive is larger than the plugin registry download limit.");

        await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer.Length + read > MaximumDownloadBytes)
                throw new InvalidOperationException("The archive is larger than the plugin registry download limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            // Every request carries its own deadline, so the client itself must not impose one.
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DotCraft", null));
        return client;
    }

    private void Warn(string code, string message, string path) =>
        _diagnostic?.Invoke(PluginDiagnostic.Warning(code, message, path: path));
}

public enum PluginRegistrySyncResult
{
    /// <summary>Not due, or not an HTTPS archive source.</summary>
    Skipped,
    Activated,
    Failed
}
