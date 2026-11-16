using System.Collections.Concurrent;
using System.Net.Http.Headers;
using DotCraft.Configuration;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Utilities;

namespace DotCraft.Plugins;

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

    private readonly ConcurrentDictionary<string, Lazy<Task<PluginRegistrySyncOutcome>>> _inFlight = new(StringComparer.Ordinal);
    private readonly string _craftHome;
    private readonly Action<PluginDiagnostic>? _diagnostic;
    private readonly HttpClient _client;
    private readonly OfficialPluginRegistrySync _officialSync;

    public PluginRegistrySyncService(
        string craftHome,
        Action<PluginDiagnostic>? diagnostic = null,
        HttpMessageHandler? handler = null)
        : this(craftHome, diagnostic, handler,
            (directory, args, timeout, ct) => GitProcessRunner.RunAsync(directory, args, timeout, ct))
    {
    }

    internal PluginRegistrySyncService(
        string craftHome,
        Action<PluginDiagnostic>? diagnostic,
        HttpMessageHandler? handler,
        OfficialPluginRegistrySync.GitCommandRunner gitRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(craftHome);
        _craftHome = Path.GetFullPath(craftHome);
        _diagnostic = diagnostic;
        _client = handler == null ? SharedClient : CreateClient(handler);
        _officialSync = new OfficialPluginRegistrySync(
            gitRunner, (url, ct) => DownloadAsync(new Uri(url), ct, github: true));
    }

    /// <summary>Returns true only when a source activates a new snapshot.</summary>
    public async Task<bool> SyncDueAsync(
        AppConfig.PluginsConfig? pluginsConfig,
        bool force,
        CancellationToken ct)
    {
        var activated = false;
        foreach (var source in PluginSourceRegistryCatalog.ArchiveSourcesFor(pluginsConfig))
        {
            var outcome = await SyncAsync(source.Url, source.MarketplacePath, source.Name, force, ct, source.IsDefault)
                .ConfigureAwait(false);
            activated |= outcome.Result == PluginRegistrySyncResult.Activated;
        }

        return activated;
    }

    public Task<PluginRegistrySyncOutcome> SyncAsync(
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct,
        bool isDefault = false)
    {
        var key = url + "\n" + marketplacePath + "\n" + isDefault;
        var pending = _inFlight.GetOrAdd(key, _ => new Lazy<Task<PluginRegistrySyncOutcome>>(
            () => RunAsync(key, url, marketplacePath, displayName, force, ct, isDefault)));
        var task = pending.Value;
        if (task.IsCompleted)
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<PluginRegistrySyncOutcome>>>(key, pending));
        return task;
    }

    private async Task<PluginRegistrySyncOutcome> RunAsync(
        string key,
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct,
        bool isDefault)
    {
        try
        {
            return await DownloadAndActivateAsync(url, marketplacePath, displayName, force, ct, isDefault)
                .ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<PluginRegistrySyncOutcome> DownloadAndActivateAsync(
        string url,
        string marketplacePath,
        string displayName,
        bool force,
        CancellationToken ct,
        bool isDefault)
    {
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            Warn("InvalidPluginRegistrySourceUrl",
                $"Plugin marketplace '{displayName}' must be an HTTPS archive URL or a local archive/directory path.",
                url);
            return new(PluginRegistrySyncResult.Skipped);
        }

        var cache = new PluginRegistryArchiveCache(_craftHome);
        cache.CleanStaleTemporaryDirectories();
        if (!force && !IsDue(cache, url, marketplacePath))
            return new(PluginRegistrySyncResult.Skipped);

        var stage = "download";
        try
        {
            if (isDefault && string.Equals(url, OfficialPluginRegistrySync.ArchiveUrl, StringComparison.Ordinal))
                return await _officialSync.SyncAsync(cache, marketplacePath, ct).ConfigureAwait(false);

            var bytes = await DownloadAsync(uri, ct).ConfigureAwait(false);
            stage = "extract";
            ct.ThrowIfCancellationRequested();
            cache.Activate(url, marketplacePath, bytes);
            return new(PluginRegistrySyncResult.Activated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or InvalidOperationException
                                       or IOException
                                       or InvalidDataException
                                       or UnauthorizedAccessException
                                       or MarketplaceException)
        {
            cache.RecordFailedAttempt(url, marketplacePath);
            var code = ex is MarketplaceException marketplaceError
                ? marketplaceError.Code
                : MarketplaceErrorCodes.FetchFailed;
            var message = $"Plugin marketplace '{displayName}' {stage} failed: {ex.Message}";
            Warn(stage == "extract" ? "PluginRegistryExtractFailed" : "PluginRegistryDownloadFailed", message, url);
            return new(PluginRegistrySyncResult.Failed, code, message);
        }
    }

    private static bool IsDue(PluginRegistryArchiveCache cache, string url, string marketplacePath)
    {
        if (!cache.ShouldAttempt(url, marketplacePath, RefreshInterval))
            return false;

        return !Directory.Exists(cache.SnapshotRootFor(url, marketplacePath))
               || cache.ShouldRefresh(url, marketplacePath, RefreshInterval);
    }

    private async Task<byte[]> DownloadAsync(Uri uri, CancellationToken ct, bool github = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (github)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            }

            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
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
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new MarketplaceException(MarketplaceErrorCodes.FetchTimeout,
                "HTTP download timed out after 30 seconds.", ex);
        }
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
    Skipped,
    Activated,
    Unchanged,
    Failed
}

public sealed record PluginRegistrySyncOutcome(
    PluginRegistrySyncResult Result,
    string? Code = null,
    string? Message = null);
