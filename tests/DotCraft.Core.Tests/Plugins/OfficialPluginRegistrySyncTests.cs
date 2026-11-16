using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Text;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Utilities;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class OfficialPluginRegistrySyncTests : IDisposable
{
    private const string MarketplacePath = ".craft/plugins/marketplace.json";
    private const string OfficialUrl = OfficialPluginRegistrySync.ArchiveUrl;
    private const string FirstRevision = "1111111111111111111111111111111111111111";
    private const string SecondRevision = "2222222222222222222222222222222222222222";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "official-sync-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_root, "home");

    [Fact]
    public async Task SyncAsync_UsesShallowGitThenSkipsUnchangedAndReusesObjectsForNewRevision()
    {
        var repository = await CreateRepositoryAsync();
        var runner = new LocalGitRunner(repository);
        var http = new HttpFixture();
        var sync = CreateSync(http, runner.RunAsync);
        var cache = new PluginRegistryArchiveCache(Home);

        var first = await RefreshAsync(sync);
        var firstRevision = cache.RevisionFor(OfficialUrl, MarketplacePath);
        Assert.Equal(PluginRegistrySyncResult.Activated, first.Result);
        var snapshot = cache.SnapshotRootFor(OfficialUrl, MarketplacePath);
        Assert.Equal("second", File.ReadAllText(Path.Combine(snapshot, "content.txt")));
        Assert.Equal("true", (await GitAsync(snapshot, ["rev-parse", "--is-shallow-repository"])).StdOut.Trim());

        cache.RecordFailedAttempt(OfficialUrl, MarketplacePath);
        var marker = Path.Combine(cache.CacheRootFor(OfficialUrl, MarketplacePath), "updatedAt.txt");
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddDays(-1));
        var beforeUnchanged = runner.Calls.Count;
        var unchanged = await RefreshAsync(sync);

        Assert.Equal(PluginRegistrySyncResult.Unchanged, unchanged.Result);
        Assert.DoesNotContain(runner.Calls.Skip(beforeUnchanged), call => call.Args.Contains("fetch"));
        Assert.False(cache.ShouldRefresh(OfficialUrl, MarketplacePath, TimeSpan.FromHours(6)));
        Assert.False(File.Exists(Path.Combine(cache.CacheRootFor(OfficialUrl, MarketplacePath), "attempt.json")));

        await CommitAsync(repository, "third");
        var beforeChange = runner.Calls.Count;
        var changed = await RefreshAsync(sync);

        Assert.Equal(PluginRegistrySyncResult.Activated, changed.Result);
        Assert.NotEqual(firstRevision, cache.RevisionFor(OfficialUrl, MarketplacePath));
        Assert.Equal("third", File.ReadAllText(Path.Combine(snapshot, "content.txt")));
        Assert.Contains(runner.Calls.Skip(beforeChange), call => call.Directory == snapshot && call.Args.Contains("fetch"));
        Assert.Contains(runner.Calls.Skip(beforeChange), call =>
            call.Directory != snapshot && call.Args.Contains("fetch") && call.Args.Contains(snapshot));
        Assert.Empty(http.Requests);
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncAsync_FallsBackToPinnedHttpWhenGitIsUnavailableOrFails(bool unavailable)
    {
        var http = new HttpFixture(FirstRevision, CreateArchive("first"));
        var sync = CreateSync(http, (_, _, _, _) => unavailable
            ? throw new Win32Exception("git is unavailable")
            : Task.FromResult(new GitProcessRunner.GitResult(128, "", "git transport failed")));

        var result = await RefreshAsync(sync);

        Assert.Equal(PluginRegistrySyncResult.Activated, result.Result);
        var cache = new PluginRegistryArchiveCache(Home);
        Assert.Equal(FirstRevision, cache.RevisionFor(OfficialUrl, MarketplacePath));
        Assert.Equal("first", ReadSnapshotContent(cache));
        Assert.Equal(2, http.Requests.Count);
        Assert.EndsWith("/git/ref/heads/main", http.Requests[0]);
        Assert.EndsWith("/zipball/" + FirstRevision, http.Requests[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAsync_ReusesUnchangedHttpSnapshotWithOrWithoutGit(bool gitAvailable)
    {
        var cache = Seed(FirstRevision, "first");
        cache.RecordFailedAttempt(OfficialUrl, MarketplacePath);
        var sentinel = Path.Combine(cache.SnapshotRootFor(OfficialUrl, MarketplacePath), "kept.txt");
        File.WriteAllText(sentinel, "held");
        var http = new HttpFixture(FirstRevision);
        var sync = CreateSync(http, gitAvailable
            ? (_, args, _, _) =>
            {
                Assert.Contains("ls-remote", args);
                return Task.FromResult(new GitProcessRunner.GitResult(0, FirstRevision + "\trefs/heads/main", ""));
            }
            : UnavailableGit);

        var result = await RefreshAsync(sync);

        Assert.Equal(PluginRegistrySyncResult.Unchanged, result.Result);
        Assert.Equal(gitAvailable ? 0 : 1, http.Requests.Count);
        Assert.True(File.Exists(sentinel));
        Assert.False(File.Exists(Path.Combine(cache.CacheRootFor(OfficialUrl, MarketplacePath), "attempt.json")));
        Assert.False(cache.ShouldRefresh(OfficialUrl, MarketplacePath, TimeSpan.FromHours(6)));
    }

    [Fact]
    public async Task SyncAsync_HttpChangedRevisionReplacesContentAndRevisionTogether()
    {
        var cache = Seed(FirstRevision, "first");
        var http = new HttpFixture(SecondRevision, CreateArchive("second"));

        var result = await RefreshAsync(CreateSync(http, UnavailableGit));

        Assert.Equal(PluginRegistrySyncResult.Activated, result.Result);
        Assert.Equal(SecondRevision, cache.RevisionFor(OfficialUrl, MarketplacePath));
        Assert.Equal("second", ReadSnapshotContent(cache));
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncAsync_BothTransportsFailPreservesSuccessfulSnapshotAndReportsStages(bool invalidArchive)
    {
        var cache = Seed(FirstRevision, "first");
        var http = new HttpFixture(SecondRevision,
            invalidArchive ? Encoding.UTF8.GetBytes("not a zip") : null,
            invalidArchive ? HttpStatusCode.OK : HttpStatusCode.BadGateway);

        var result = await RefreshAsync(CreateSync(http, UnavailableGit));

        Assert.Equal(PluginRegistrySyncResult.Failed, result.Result);
        Assert.Equal(MarketplaceErrorCodes.FetchFailed, result.Code);
        Assert.Contains("Git synchronization failed", result.Message);
        Assert.Contains("GitHub HTTP synchronization failed", result.Message);
        if (!invalidArchive)
            Assert.Contains("HTTP 502", result.Message);
        Assert.Equal(FirstRevision, cache.RevisionFor(OfficialUrl, MarketplacePath));
        Assert.Equal("first", ReadSnapshotContent(cache));
        Assert.True(File.Exists(Path.Combine(cache.CacheRootFor(OfficialUrl, MarketplacePath), "attempt.json")));
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Fact]
    public async Task SyncAsync_FailedUpdateKeepsGitCheckoutAndSuccessfulRevision()
    {
        var repository = await CreateRepositoryAsync();
        var http = new HttpFixture(status: HttpStatusCode.BadGateway);
        var first = await RefreshAsync(CreateSync(http, new LocalGitRunner(repository).RunAsync));
        Assert.Equal(PluginRegistrySyncResult.Activated, first.Result);
        var cache = new PluginRegistryArchiveCache(Home);
        var revision = cache.RevisionFor(OfficialUrl, MarketplacePath);
        await CommitAsync(repository, "third");

        var result = await RefreshAsync(CreateSync(http,
            new LocalGitRunner(repository, corruptCheckout: true).RunAsync));

        Assert.Equal(PluginRegistrySyncResult.Failed, result.Result);
        Assert.Equal(revision, cache.RevisionFor(OfficialUrl, MarketplacePath));
        var snapshot = cache.SnapshotRootFor(OfficialUrl, MarketplacePath);
        Assert.Equal("second", File.ReadAllText(Path.Combine(snapshot, "content.txt")));
        Assert.Equal(revision, (await GitAsync(snapshot, ["rev-parse", "HEAD"])).StdOut.Trim());
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Fact]
    public async Task SyncAsync_CancellationDuringGitDoesNotStartHttpOrRecordFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var http = new HttpFixture(FirstRevision, CreateArchive("first"));
        var sync = CreateSync(http, (_, _, _, ct) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<GitProcessRunner.GitResult>(ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sync.SyncAsync(OfficialUrl, MarketplacePath, "Official", true, cancellation.Token, isDefault: true));

        Assert.Empty(http.Requests);
        var cache = new PluginRegistryArchiveCache(Home);
        Assert.False(File.Exists(Path.Combine(cache.CacheRootFor(OfficialUrl, MarketplacePath), "attempt.json")));
    }

    [Theory]
    [InlineData(OfficialUrl, false)]
    [InlineData("https://custom.test/marketplace.zip", true)]
    public async Task SyncAsync_CustomArchiveOrDefaultOverrideDoesNotUseOfficialTransports(string url, bool isDefault)
    {
        var http = new HttpFixture(archive: CreateArchive("custom"));
        var sync = CreateSync(http, (_, _, _, _) => throw new Xunit.Sdk.XunitException("Git must not be used."));

        var result = await sync.SyncAsync(url, MarketplacePath, "Custom", true, CancellationToken.None, isDefault);

        Assert.Equal(PluginRegistrySyncResult.Activated, result.Result);
        Assert.Equal([url], http.Requests);
    }

    private PluginRegistrySyncService CreateSync(
        HttpMessageHandler http, OfficialPluginRegistrySync.GitCommandRunner runner) =>
        new(Home, diagnostic: null, handler: http, gitRunner: runner);

    private static Task<PluginRegistrySyncOutcome> RefreshAsync(PluginRegistrySyncService sync) =>
        sync.SyncAsync(OfficialUrl, MarketplacePath, "Official", true, CancellationToken.None, isDefault: true);

    private static Task<GitProcessRunner.GitResult> UnavailableGit(
        string directory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct) =>
        throw new Win32Exception("git is unavailable");

    private PluginRegistryArchiveCache Seed(string revision, string content)
    {
        var cache = new PluginRegistryArchiveCache(Home);
        cache.Activate(OfficialUrl, MarketplacePath, CreateArchive(content), revision: revision);
        return cache;
    }

    private static string ReadSnapshotContent(PluginRegistryArchiveCache cache) =>
        File.ReadAllText(Path.Combine(cache.SnapshotRootFor(OfficialUrl, MarketplacePath), "root", "content.txt"));

    private static byte[] CreateArchive(string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("root/" + MarketplacePath).Open()))
                writer.Write(MarketplaceJson);
            using var contentWriter = new StreamWriter(archive.CreateEntry("root/content.txt").Open());
            contentWriter.Write(content);
        }
        return stream.ToArray();
    }

    private const string MarketplaceJson = """{"name":"official","plugins":[]}""";

    private async Task<string> CreateRepositoryAsync()
    {
        var repository = Path.Combine(_root, "repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, ["init", "--initial-branch=main"]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repository, MarketplacePath))!);
        File.WriteAllText(Path.Combine(repository, MarketplacePath), MarketplaceJson);
        await CommitAsync(repository, "first");
        await CommitAsync(repository, "second");
        return repository;
    }

    private static async Task CommitAsync(string repository, string content)
    {
        File.WriteAllText(Path.Combine(repository, "content.txt"), content);
        await GitAsync(repository, ["add", "--force", "."]);
        await GitAsync(repository, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test",
            "-c", "commit.gpgsign=false", "commit", "-m", content]);
    }

    private static async Task<GitProcessRunner.GitResult> GitAsync(string directory, IReadOnlyList<string> args)
    {
        var result = await GitProcessRunner.RunAsync(directory, args, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StdErr);
        return result;
    }

    public void Dispose() => new PluginRegistryArchiveCache(Home).TryDeleteDirectory(_root);

    private sealed class LocalGitRunner(string repository, bool corruptCheckout = false)
    {
        public List<(string Directory, string[] Args)> Calls { get; } = [];

        public async Task<GitProcessRunner.GitResult> RunAsync(
            string directory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add((directory, args.ToArray()));
            var localArgs = args.Select(arg => arg.StartsWith("https://", StringComparison.Ordinal)
                ? repository : arg).ToArray();
            var result = await GitProcessRunner.RunAsync(directory, localArgs, timeout, ct);
            if (corruptCheckout && args.Contains("checkout"))
                File.Delete(Path.Combine(directory, MarketplacePath));
            return result;
        }
    }

    private sealed class HttpFixture(
        string revision = FirstRevision,
        byte[]? archive = null,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            var bytes = url.EndsWith("/git/ref/heads/main", StringComparison.Ordinal)
                ? System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { @object = new { sha = revision } })
                : archive ?? throw new Xunit.Sdk.XunitException("Unexpected archive download.");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
        }
    }
}
