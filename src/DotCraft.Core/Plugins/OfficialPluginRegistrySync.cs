using System.ComponentModel;
using System.Text.Json;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Utilities;

namespace DotCraft.Plugins;

internal sealed class OfficialPluginRegistrySync(
    OfficialPluginRegistrySync.GitCommandRunner gitRunner,
    Func<string, CancellationToken, Task<byte[]>> download)
{
    private const string Repository = "DotHarness/dotcraft-plugins";
    internal const string ArchiveUrl = $"https://github.com/{Repository}/archive/refs/heads/main.zip";
    private const string GitUrl = $"https://github.com/{Repository}.git";
    private const string RepositoryApiUrl = $"https://api.github.com/repos/{Repository}";
    private const string BranchRef = "refs/heads/main";
    private const string FetchRef = "refs/dotcraft/marketplace-sync";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    internal delegate Task<GitProcessRunner.GitResult> GitCommandRunner(
        string directory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

    public async Task<PluginRegistrySyncOutcome> SyncAsync(
        PluginRegistryArchiveCache cache, string marketplacePath, CancellationToken ct)
    {
        Exception gitFailure;
        try
        {
            return await SyncGitAsync(cache, marketplacePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsSyncFailure(ex))
        {
            gitFailure = ex;
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            return await SyncHttpAsync(cache, marketplacePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsSyncFailure(ex))
        {
            throw new MarketplaceException(FailureCode(ex),
                $"Git synchronization failed: {gitFailure.Message}. GitHub HTTP synchronization failed: {ex.Message}", ex);
        }
    }

    private async Task<PluginRegistrySyncOutcome> SyncGitAsync(
        PluginRegistryArchiveCache cache, string marketplacePath, CancellationToken ct)
    {
        Directory.CreateDirectory(cache.CacheBaseRoot);
        var remote = await RunGitAsync(cache.CacheBaseRoot,
            ["ls-remote", GitUrl, BranchRef], ct).ConfigureAwait(false);
        var revision = remote.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(revision))
            throw new IOException("git ls-remote did not return the official marketplace main revision.");

        var snapshot = cache.SnapshotRootFor(ArchiveUrl, marketplacePath);
        var hasGit = Directory.Exists(Path.Combine(snapshot, ".git"));
        if (IsCurrent(cache, marketplacePath, revision)
            && (!hasGit || string.Equals(
                await RunGitAsync(snapshot, ["rev-parse", "HEAD"], ct).ConfigureAwait(false),
                revision, StringComparison.Ordinal)))
        {
            cache.MarkChecked(ArchiveUrl, marketplacePath);
            return new(PluginRegistrySyncResult.Unchanged);
        }

        var staged = Path.Combine(cache.CacheBaseRoot, $".official.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(staged);
            await RunGitAsync(staged, ["init"], ct).ConfigureAwait(false);
            if (hasGit)
            {
                await FetchAsync(snapshot, GitUrl, revision, ct).ConfigureAwait(false);
                await FetchAsync(staged, snapshot, FetchRef, ct).ConfigureAwait(false);
            }
            else
            {
                await FetchAsync(staged, GitUrl, revision, ct).ConfigureAwait(false);
            }

            await RunGitAsync(staged, ["checkout", "--detach", FetchRef], ct).ConfigureAwait(false);
            var fetchedRevision = await RunGitAsync(staged, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);
            if (!string.Equals(fetchedRevision, revision, StringComparison.Ordinal))
                throw new IOException($"Official marketplace revision mismatch: expected {revision}, got {fetchedRevision}.");

            ct.ThrowIfCancellationRequested();
            cache.ActivateDirectory(ArchiveUrl, marketplacePath, staged, revision);
            return new(PluginRegistrySyncResult.Activated);
        }
        finally
        {
            cache.TryDeleteDirectory(staged);
        }
    }

    private async Task<PluginRegistrySyncOutcome> SyncHttpAsync(
        PluginRegistryArchiveCache cache, string marketplacePath, CancellationToken ct)
    {
        var metadataBytes = await DownloadStageAsync(
            RepositoryApiUrl + "/git/ref/heads/main", "query official marketplace revision", ct).ConfigureAwait(false);
        string revision;
        try
        {
            using var metadata = JsonDocument.Parse(metadataBytes);
            revision = metadata.RootElement.GetProperty("object").GetProperty("sha").GetString()
                ?? throw new JsonException("The official marketplace revision is missing.");
            if (string.IsNullOrWhiteSpace(revision))
                throw new JsonException("The official marketplace revision is empty.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new MarketplaceException(MarketplaceErrorCodes.FetchFailed,
                $"Could not read the official marketplace revision: {ex.Message}", ex);
        }

        if (IsCurrent(cache, marketplacePath, revision))
        {
            cache.MarkChecked(ArchiveUrl, marketplacePath);
            return new(PluginRegistrySyncResult.Unchanged);
        }

        var bytes = await DownloadStageAsync(
            RepositoryApiUrl + "/zipball/" + revision, "download official marketplace archive", ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        cache.Activate(ArchiveUrl, marketplacePath, bytes, revision: revision);
        return new(PluginRegistrySyncResult.Activated);
    }

    private static bool IsCurrent(PluginRegistryArchiveCache cache, string marketplacePath, string revision)
    {
        if (!string.Equals(cache.RevisionFor(ArchiveUrl, marketplacePath), revision, StringComparison.Ordinal))
            return false;

        try
        {
            MarketplaceDocumentLoader.ValidateRoot(cache.SnapshotRootFor(ArchiveUrl, marketplacePath), marketplacePath);
            return true;
        }
        catch (MarketplaceException)
        {
            return false;
        }
    }

    private Task<string> FetchAsync(string directory, string source, string revision, CancellationToken ct) =>
        RunGitAsync(directory,
            ["fetch", "--depth", "1", "--no-tags", source, $"+{revision}:{FetchRef}"], ct);

    private async Task<string> RunGitAsync(string directory, IReadOnlyList<string> args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await gitRunner(directory,
            ["-c", "protocol.ext.allow=never", "-c", "credential.interactive=never", .. args],
            GitTimeout, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new MarketplaceException(MarketplaceErrorCodes.FetchFailed,
                $"git {args[0]} failed: {detail.Trim()}");
        }

        return result.StdOut.Trim();
    }

    private async Task<byte[]> DownloadStageAsync(string url, string stage, CancellationToken ct)
    {
        try
        {
            return await download(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsSyncFailure(ex))
        {
            throw new MarketplaceException(FailureCode(ex), $"Could not {stage}: {ex.Message}", ex);
        }
    }

    private static string FailureCode(Exception ex) => ex switch
    {
        MarketplaceException failure => failure.Code,
        GitProcessTimeoutException or OperationCanceledException => MarketplaceErrorCodes.FetchTimeout,
        _ => MarketplaceErrorCodes.FetchFailed
    };

    private static bool IsSyncFailure(Exception ex) =>
        ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or GitProcessTimeoutException or HttpRequestException or OperationCanceledException or MarketplaceException;
}
