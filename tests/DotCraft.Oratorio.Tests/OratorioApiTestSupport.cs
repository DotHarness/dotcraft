using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.Sources;
using DynamicToolCall = DotCraft.Protocol.AppServer.DynamicToolCallParams;
using DynamicToolResult = DotCraft.Protocol.AppServer.DynamicToolCallResult;
using RuntimeDynamicToolDeclaration = DotCraft.Protocol.AppServer.RuntimeDynamicToolDeclaration;
using RuntimeDynamicToolFunction = DotCraft.Protocol.AppServer.RuntimeDynamicToolFunction;
using RuntimeDynamicToolNamespace = DotCraft.Protocol.AppServer.RuntimeDynamicToolNamespace;

namespace DotCraft.Oratorio.Tests;

internal static class OratorioApiTestSupport
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static GitHubApiClient CreateGitHubApiClient(HttpMessageHandler handler) =>
        new(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions { Endpoint = "https://api.github.test" }),
            new StaticGitHubTokenProvider());

    internal static async Task CreateItemAsync(HttpClient client, string externalId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/items",
            new CreateItemRequest(
                "local",
                externalId,
                ItemKind.PullRequest,
                "Test item",
                "A test item.",
                "example-owner/oratorio",
                "operator",
                "test/oratorio"),
            JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Create item failed with {(int)response.StatusCode}: {body}");
        }
    }

    internal static Task<ItemDetailResponse> CreateLocalTaskAsync(
        HttpClient client,
        string title = "Write local task docs",
        string? repository = "example-owner/oratorio") =>
        PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(
                title,
                "Document the M1 local task workflow.",
                repository,
                "operator",
                "local/m1",
                ["m1", "local"]));

    internal static async Task AssertCancelRejectedAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync(path, new CancelRunRequest("Stop."), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("runNotCancellable", error?.Error.Code);
    }

    internal static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for predicate.");
    }

    internal static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for async predicate.");
    }

    internal static async Task<GitHubSyncJobDto> WaitForGitHubSyncJobAsync(HttpClient client, string jobId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        GitHubSyncJobDto? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{jobId}", JsonOptions);
            if (latest is not null && latest.Status is not (GitHubSyncStatus.Queued or GitHubSyncStatus.Running))
            {
                return latest;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for GitHub sync job {jobId}. Latest state: {latest?.Status}");
    }

    internal static async Task<ItemDetailResponse> CreateGitHubPullRequestInReviewAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub PR review.", MockOutcome.Success, 1));
        return await WaitForItemByIdAsync(client, pr.ItemId, x => x.Item.State == ItemState.AwaitingReview);
    }

    internal static async Task<ItemDetailResponse> CreateGitHubIssueInReviewAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub issue review.", MockOutcome.Success, 1));
        return await WaitForItemByIdAsync(client, issue.ItemId, x => x.Item.State == ItemState.AwaitingReview);
    }

    internal static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    internal static async Task<T> PutAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PutAsJsonAsync(path, request, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    internal static async Task<T> PatchAsync<T>(HttpClient client, string path, object request)
    {
        var response = await PatchRawAsync(client, path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name} response.");
    }

    internal static Task<HttpResponseMessage> PatchRawAsync(HttpClient client, string path, object request) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        });

    internal static async Task<ItemDetailResponse> WaitForItemAsync(
        HttpClient client,
        string externalId,
        Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/local/{externalId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {externalId}. Latest state: {latest?.Item.State}");
    }

    internal static async Task<ItemDetailResponse> WaitForItemByIdAsync(
        HttpClient client,
        string itemId,
        Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {itemId}. Latest state: {latest?.Item.State}");
    }

    internal static async Task DispatchAutoReviewAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AutoReviewDispatchService>();
        await dispatcher.DispatchEligibleAsync(CancellationToken.None);
    }

    internal static GitHubIssue TestGitHubIssue(long id, int number, string title, string repository, bool isPullRequest = false) =>
        new(
            id,
            number,
            title,
            $"Body for {title}.",
            "open",
            $"https://github.example.test/{repository}/{(isPullRequest ? "pull" : "issues")}/{number}",
            new GitHubUser("tester"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            isPullRequest ? new GitHubPullMarker($"https://github.example.test/{repository}/pull/{number}") : null);

    internal static GitHubPullRequest TestGitHubPullRequest(long id, int number, string title, string repository) =>
        new(
            id,
            number,
            title,
            $"Body for {title}.",
            "open",
            $"https://github.example.test/{repository}/pull/{number}",
            new GitHubUser("tester"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"),
            null,
            null,
            false,
            new GitHubBranchRef("feature/test", "head-sha"),
            new GitHubBranchRef("main", "base-sha"));

    internal static void MoveDefaultPullRequestHead(FakeGitHubApiClient fakeGitHub, string headSha, DateTimeOffset? updatedAt = null)
    {
        var updated = updatedAt ?? DateTimeOffset.Parse("2026-05-04T12:00:00Z");
        fakeGitHub.Issues[1] = fakeGitHub.Issues[1] with { UpdatedAt = updated };
        fakeGitHub.PullRequests[0] = fakeGitHub.PullRequests[0] with
        {
            UpdatedAt = updated,
            Head = fakeGitHub.PullRequests[0].Head with { Sha = headSha }
        };
    }

    internal static async Task MarkRunAsIncompatibleThreadContextAsync(TestOratorioApp app, string runId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.Include(x => x.Round).FirstAsync(x => x.RunId == runId);
        var context = JsonNode.Parse(run.PromptContextJson!)!.AsObject();
        context.Remove("threadStateWorkspaceMode");
        run.PromptContextJson = context.ToJsonString(JsonOptions);
        if (run.Round is not null)
        {
            run.Round.PromptContextJson = run.PromptContextJson;
        }

        await db.SaveChangesAsync();
    }
}

internal sealed class TestOratorioApp : WebApplicationFactory<Program>
{
    private readonly string _databasePath;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public TestOratorioApp(
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
        _configureServices = configureServices;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var defaultWorkspace = Path.ChangeExtension(_databasePath, ".workspace");
        Directory.CreateDirectory(defaultWorkspace);
        builder.UseSetting("Oratorio:DatabasePath", _databasePath);
        builder.UseSetting("Oratorio:Settings:SecretKeyPath", Path.ChangeExtension(_databasePath, ".key"));
        builder.UseSetting("Oratorio:DotCraft:MaxRunAttempts", "1");
        builder.UseSetting("Oratorio:DotCraft:RetryBackoffSeconds", "1");
        builder.UseSetting("Oratorio:DotCraft:MaxRetryBackoffSeconds", "1");
        builder.UseSetting("Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project", "github:github.com/example-owner/oratorio");
        builder.UseSetting("Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath", defaultWorkspace);
        builder.UseSetting("Oratorio:GitHub:Token", "test-token");
        builder.UseSetting("Oratorio:GitHub:WritesEnabled", "true");
        builder.UseSetting("Oratorio:GitHub:AppId", "12345");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Instance", "github.com");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Owner", "example-owner");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:InstallationId", "98765");
        builder.UseSetting("Oratorio:GitHub:InstallationProfiles:0:Source", "manual");
        builder.UseSetting("Oratorio:GitHub:PrivateKey", "test-private-key");
        builder.UseSetting("Oratorio:GitHub:WebhookSecret", "test-secret");
        builder.UseSetting("Oratorio:GitHub:Repositories:0", "example-owner/oratorio");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager, FakeWorktreeManager>();
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient, FakeGitDeliveryClient>();
            _configureServices?.Invoke(services);
        });
    }
}

internal sealed class FakeGitHubApiClient : IGitHubApiClient
{
    public List<GitHubIssue> Issues { get; } =
    [
        new(
            1001,
            42,
            "Design comment sync cursor",
            "Bring imported source comments into Oratorio.",
            "open",
            "https://github.example.test/example-owner/oratorio/issues/42",
            new GitHubUser("example-author"),
            [new GitHubLabel("source-sync")],
            [new GitHubUser("example-assignee")],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            null),
        new(
            1002,
            184,
            "This is actually a PR-shaped issue",
            null,
            "open",
            "https://github.example.test/example-owner/oratorio/pull/184",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T10:00:00Z"),
            null,
            new GitHubPullMarker("https://github.example.test/example-owner/oratorio/pull/184"))
    ];

    public List<GitHubPullRequest> PullRequests { get; } =
    [
        new(
            2001,
            184,
            "Add JWT middleware and refresh-token flow",
            "Implements auth refresh flow.",
            "open",
            "https://github.example.test/example-owner/oratorio/pull/184",
            new GitHubUser("example-author"),
            [new GitHubLabel("security"), new GitHubLabel("backend")],
            [new GitHubUser("example-assignee")],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"),
            null,
            null,
            false,
            new GitHubBranchRef("feature/auth-refresh", "abc123"),
            new GitHubBranchRef("main", "base123"))
    ];

    public List<GitHubIssueCommentWrite> IssueComments { get; } = [];
    public List<GitHubPullRequestReviewWrite> PullRequestReviews { get; } = [];
    public List<GitHubCheckRunWrite> CheckRuns { get; } = [];
    public List<GitHubPullRequestCreateWrite> PullRequestCreates { get; } = [];
    public List<string> WriteOperations { get; } = [];
    public Dictionary<string, List<GitHubIssue>> IssuesByRepository { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubPullRequest>> PullRequestsByRepository { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TimeSpan> ListIssueDelays { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CancelingIssueRepositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<DateTimeOffset?>> IssueSinceArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubListState>> IssueStateArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<GitHubListState>> PullRequestStateArguments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GitHubComment> PullRequestIssueComments { get; } =
    [
        new(
            9001,
            "Please use a constant-time comparison helper.",
            "https://github.example.test/example-owner/oratorio/pull/184#issuecomment-9001",
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T13:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T13:00:00Z"))
    ];
    public List<GitHubComment> IssueSourceComments { get; } =
    [
        new(
            9002,
            "Issue comment imported from GitHub.",
            "https://github.example.test/example-owner/oratorio/issues/42#issuecomment-9002",
            new GitHubUser("operator"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z"))
    ];
    public List<GitHubReview> SourceReviews { get; } =
    [
        new(
            9101,
            "Two suggestions remain.",
            "https://github.example.test/example-owner/oratorio/pull/184#pullrequestreview-9101",
            "CHANGES_REQUESTED",
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T15:00:00Z"))
    ];
    public List<GitHubReviewComment> SourceReviewComments { get; } =
    [
        new(
            9201,
            "Consider extracting the token validation branch.",
            "https://github.example.test/example-owner/oratorio/pull/184#discussion_r9201",
            "src/Auth/RefreshTokenStore.cs",
            88,
            88,
            new GitHubUser("reviewer"),
            DateTimeOffset.Parse("2026-05-02T15:05:00Z"),
            DateTimeOffset.Parse("2026-05-02T15:05:00Z"))
    ];
    public List<GitHubChangedFile> PullRequestFiles { get; } =
    [
        new("src/Auth/RefreshTokenStore.cs", "modified", 4, 1, 5, RefreshTokenStorePatch),
        new("src/Auth/JwtMiddleware.cs", "modified", 2, 0, 2, JwtMiddlewarePatch)
    ];
    public int FailNextWriteCount { get; set; }
    public int FailNextPullRequestReviewCount { get; set; }
    public int IssueCommentCallCount { get; private set; }
    public int PullRequestReviewCallCount { get; private set; }
    public int PullRequestReviewCommentCallCount { get; private set; }
    public int FullDiffCallCount { get; private set; }
    public int GetPullRequestCallCount { get; private set; }
    public int FailNextGetPullRequestCount { get; set; }
    public bool FailSourceDetailReads { get; set; }
    public bool FailPullRequestFileReads { get; set; }

    public static readonly string RefreshTokenStorePatch = """
@@ -84,8 +84,9 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
-        return token;
+        return token;
+        return refreshed;
     }
 }
""";

    public static readonly string JwtMiddlewarePatch = """
@@ -20,4 +20,5 @@ public sealed class JwtMiddleware
     public void Invoke()
     {
+        Validate();
     }
 }
""";

    public static readonly string SyntheticFileSearcherPatch = """
@@ -120,23 +255,23 @@ public sealed class SyntheticFileSearcher
     context line 01
     context line 02
     context line 03
     context line 04
     context line 05
     context line 06
     context line 07
     context line 08
     context line 09
     context line 10
     context line 11
     context line 12
     context line 13
     context line 14
     context line 15
     context line 16
     context line 17
     context line 18
     context line 19
     context line 20
     context line 21
     context line 22
     context line 23
""";

    public async Task<IReadOnlyList<GitHubIssue>> ListIssuesAsync(GitHubRepositoryRef repository, GitHubListState state, DateTimeOffset? since, CancellationToken ct)
    {
        if (ListIssueDelays.TryGetValue(repository.FullName, out var delay))
        {
            await Task.Delay(delay, ct);
        }

        if (CancelingIssueRepositories.Contains(repository.FullName))
        {
            throw new TaskCanceledException($"Synthetic GitHub list timeout for {repository.FullName}.");
        }

        if (!IssueSinceArguments.TryGetValue(repository.FullName, out var sinceArguments))
        {
            sinceArguments = [];
            IssueSinceArguments[repository.FullName] = sinceArguments;
        }

        sinceArguments.Add(since);
        if (!IssueStateArguments.TryGetValue(repository.FullName, out var stateArguments))
        {
            stateArguments = [];
            IssueStateArguments[repository.FullName] = stateArguments;
        }

        stateArguments.Add(state);
        var source = IssuesByRepository.TryGetValue(repository.FullName, out var issues) ? issues : Issues;
        source = FilterIssuesByState(source, state).ToList();
        if (since is not null)
        {
            source = source.Where(x => x.UpdatedAt >= since.Value).ToList();
        }

        return source;
    }

    public Task<IReadOnlyList<GitHubPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, GitHubListState state, CancellationToken ct)
    {
        if (!PullRequestStateArguments.TryGetValue(repository.FullName, out var stateArguments))
        {
            stateArguments = [];
            PullRequestStateArguments[repository.FullName] = stateArguments;
        }

        stateArguments.Add(state);
        var source = PullRequestsByRepository.TryGetValue(repository.FullName, out var pullRequests) ? pullRequests : PullRequests;
        return Task.FromResult<IReadOnlyList<GitHubPullRequest>>(FilterPullRequestsByState(source, state).ToArray());
    }

    public Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        GetPullRequestCallCount++;
        if (FailNextGetPullRequestCount > 0)
        {
            FailNextGetPullRequestCount--;
            throw new HttpRequestException("Synthetic GitHub pull request read failure.");
        }

        var source = PullRequestsByRepository.TryGetValue(repository.FullName, out var pullRequests) ? pullRequests : PullRequests;
        return Task.FromResult(source.First(x => x.Number == number));
    }

    public Task<IReadOnlyList<GitHubComment>> ListIssueCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        IssueCommentCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        return Task.FromResult<IReadOnlyList<GitHubComment>>(number == 184 ? PullRequestIssueComments : IssueSourceComments);
    }

    public Task<IReadOnlyList<GitHubReview>> ListPullRequestReviewsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        PullRequestReviewCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        return Task.FromResult<IReadOnlyList<GitHubReview>>(SourceReviews);
    }

    public Task<IReadOnlyList<GitHubReviewComment>> ListPullRequestReviewCommentsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        PullRequestReviewCommentCallCount++;
        if (FailSourceDetailReads)
        {
            throw new HttpRequestException("Synthetic GitHub detail failure.");
        }

        return Task.FromResult<IReadOnlyList<GitHubReviewComment>>(SourceReviewComments);
    }

    public Task<IReadOnlyList<GitHubChangedFile>> ListPullRequestFilesAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        if (FailPullRequestFileReads)
        {
            throw new HttpRequestException("Synthetic GitHub pull request file read failure.", null, HttpStatusCode.InternalServerError);
        }

        return Task.FromResult<IReadOnlyList<GitHubChangedFile>>(PullRequestFiles);
    }

    private static IEnumerable<GitHubIssue> FilterIssuesByState(IEnumerable<GitHubIssue> issues, GitHubListState state) =>
        state switch
        {
            GitHubListState.Open => issues.Where(x => x.State.Equals("open", StringComparison.OrdinalIgnoreCase)),
            GitHubListState.Closed => issues.Where(x => x.State.Equals("closed", StringComparison.OrdinalIgnoreCase)),
            _ => issues
        };

    private static IEnumerable<GitHubPullRequest> FilterPullRequestsByState(IEnumerable<GitHubPullRequest> pullRequests, GitHubListState state) =>
        state switch
        {
            GitHubListState.Open => pullRequests.Where(x => x.State.Equals("open", StringComparison.OrdinalIgnoreCase)),
            GitHubListState.Closed => pullRequests.Where(x => x.State.Equals("closed", StringComparison.OrdinalIgnoreCase)),
            _ => pullRequests
        };

    public Task<string> GetPullRequestDiffAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        FullDiffCallCount++;
        return Task.FromResult("""
diff --git a/src/Auth/RefreshTokenStore.cs b/src/Auth/RefreshTokenStore.cs
index 1111111..2222222 100644
--- a/src/Auth/RefreshTokenStore.cs
+++ b/src/Auth/RefreshTokenStore.cs
@@ -84,8 +84,9 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
-        return token;
+        return token;
+        return refreshed;
     }
 }
diff --git a/src/Auth/JwtMiddleware.cs b/src/Auth/JwtMiddleware.cs
index 3333333..4444444 100644
--- a/src/Auth/JwtMiddleware.cs
+++ b/src/Auth/JwtMiddleware.cs
@@ -20,4 +20,5 @@ public sealed class JwtMiddleware
     public void Invoke()
     {
+        Validate();
     }
 }
""");
    }

    public Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, int number, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        IssueComments.Add(new GitHubIssueCommentWrite(repository, number, body));
        return Task.FromResult(new GitHubWriteResponse("issue-comment:100", $"https://github.example.test/{repository.FullName}/issues/{number}#issuecomment-100", """{"id":100}"""));
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, CancellationToken ct)
    {
        MaybeFailWrite();
        PullRequestReviews.Add(new GitHubPullRequestReviewWrite(repository, number, @event, body, commitId, []));
        return Task.FromResult(new GitHubWriteResponse("review:200", $"https://github.example.test/{repository.FullName}/pull/{number}#pullrequestreview-200", """{"id":200}"""));
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewAsync(GitHubRepositoryRef repository, int number, string @event, string body, string? commitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> comments, CancellationToken ct)
    {
        MaybeFailWrite();
        if (FailNextPullRequestReviewCount > 0)
        {
            FailNextPullRequestReviewCount--;
            throw new HttpRequestException("Synthetic GitHub review failure.");
        }

        PullRequestReviews.Add(new GitHubPullRequestReviewWrite(repository, number, @event, body, commitId, comments));
        return Task.FromResult(new GitHubWriteResponse("review:200", $"https://github.example.test/{repository.FullName}/pull/{number}#pullrequestreview-200", """{"id":200}"""));
    }

    public Task<GitHubWriteResponse> CreateCheckRunAsync(
        GitHubRepositoryRef repository,
        string name,
        string headSha,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct)
    {
        MaybeFailWrite();
        var id = $"check-run:{300 + CheckRuns.Count}";
        CheckRuns.Add(new GitHubCheckRunWrite(repository, name, headSha, status, conclusion, title, summary, id));
        return Task.FromResult(new GitHubWriteResponse(id, $"https://github.example.test/{repository.FullName}/runs/{300 + CheckRuns.Count - 1}", $$"""{"id":"{{id}}"}"""));
    }

    public Task<GitHubWriteResponse> UpdateCheckRunAsync(
        GitHubRepositoryRef repository,
        string checkRunId,
        string status,
        string? conclusion,
        string title,
        string summary,
        CancellationToken ct)
    {
        MaybeFailWrite();
        var check = CheckRuns.FirstOrDefault(x => x.ExternalId == checkRunId)
            ?? throw new InvalidOperationException($"Unknown check run {checkRunId}.");
        check.Status = status;
        check.Conclusion = conclusion;
        check.Title = title;
        check.Summary = summary;
        check.UpdateCount++;
        return Task.FromResult(new GitHubWriteResponse(checkRunId, $"https://github.example.test/{repository.FullName}/runs/{checkRunId}", $$"""{"id":"{{checkRunId}}"}"""));
    }

    public List<GitHubReviewThreadReplyWrite> ReviewThreadReplies { get; } = [];
    public List<(string ThreadId, bool Resolved)> ReviewThreadResolutions { get; } = [];

    public Task<IReadOnlyList<GitHubReviewThread>> ListPullRequestReviewThreadsAsync(GitHubRepositoryRef repository, int number, CancellationToken ct)
    {
        var threads = new List<GitHubReviewThread>();
        var index = 0;
        foreach (var review in PullRequestReviews.Where(r => r.Number == number))
        {
            foreach (var comment in review.Comments)
            {
                var threadId = $"thread-{number}-{index++}";
                var bodies = new List<string> { comment.Body };
                bodies.AddRange(ReviewThreadReplies.Where(reply => reply.ThreadId == threadId).Select(reply => reply.Body));
                threads.Add(new GitHubReviewThread(threadId, false, bodies));
            }
        }

        return Task.FromResult<IReadOnlyList<GitHubReviewThread>>(threads);
    }

    public Task<GitHubWriteResponse> CreatePullRequestReviewThreadReplyAsync(GitHubRepositoryRef repository, string threadId, string body, CancellationToken ct)
    {
        MaybeFailWrite();
        WriteOperations.Add($"reply:{threadId}");
        ReviewThreadReplies.Add(new GitHubReviewThreadReplyWrite(repository, threadId, body));
        var id = $"review-thread-reply:{400 + ReviewThreadReplies.Count - 1}";
        return Task.FromResult(new GitHubWriteResponse(id, $"https://github.example.test/{repository.FullName}/pull/184#discussion_r{400 + ReviewThreadReplies.Count - 1}", $$"""{"id":"{{id}}"}"""));
    }

    public Task<GitHubWriteResponse> ResolveReviewThreadAsync(GitHubRepositoryRef repository, string threadId, bool resolved, CancellationToken ct)
    {
        MaybeFailWrite();
        WriteOperations.Add($"resolve:{threadId}:{resolved}");
        ReviewThreadResolutions.Add((threadId, resolved));
        return Task.FromResult(new GitHubWriteResponse(threadId, null, $$"""{"threadId":"{{threadId}}","resolved":{{(resolved ? "true" : "false")}}}"""));
    }

    public Task<GitHubPullRequestCreateResponse> CreatePullRequestAsync(GitHubRepositoryRef repository, string title, string head, string @base, string body, bool draft, CancellationToken ct)
    {
        MaybeFailWrite();
        PullRequestCreates.Add(new GitHubPullRequestCreateWrite(repository, title, head, @base, body, draft));
        return Task.FromResult(new GitHubPullRequestCreateResponse(
            5001,
            501,
            $"https://github.example.test/{repository.FullName}/pull/501",
            title,
            new GitHubBranchRef(head, "commit-sha-123"),
            new GitHubBranchRef(@base, "base-sha-123")));
    }

    private void MaybeFailWrite()
    {
        if (FailNextWriteCount <= 0)
        {
            return;
        }

        FailNextWriteCount--;
        throw new HttpRequestException("Synthetic GitHub write failure.");
    }
}

internal sealed record GitHubIssueCommentWrite(GitHubRepositoryRef Repository, int Number, string Body);

internal sealed record GitHubPullRequestReviewWrite(GitHubRepositoryRef Repository, int Number, string Event, string Body, string? CommitId, IReadOnlyList<GitHubPullRequestReviewCommentWrite> Comments);

internal sealed record GitHubReviewThreadReplyWrite(GitHubRepositoryRef Repository, string ThreadId, string Body);

internal sealed class GitHubCheckRunWrite(
    GitHubRepositoryRef repository,
    string name,
    string headSha,
    string status,
    string? conclusion,
    string title,
    string summary,
    string externalId)
{
    public GitHubRepositoryRef Repository { get; } = repository;
    public string Name { get; } = name;
    public string HeadSha { get; } = headSha;
    public string Status { get; set; } = status;
    public string? Conclusion { get; set; } = conclusion;
    public string Title { get; set; } = title;
    public string Summary { get; set; } = summary;
    public string ExternalId { get; } = externalId;
    public int UpdateCount { get; set; }
}

internal sealed record GitHubPullRequestCreateWrite(GitHubRepositoryRef Repository, string Title, string Head, string Base, string Body, bool Draft);

internal sealed class FakeReviewLocalDiffProvider : IReviewLocalDiffProvider
{
    private readonly IReadOnlyDictionary<string, string> _patches;

    public FakeReviewLocalDiffProvider(IReadOnlyDictionary<string, string>? patches = null)
    {
        _patches = patches ?? new Dictionary<string, string>();
    }

    public List<string> RequestedPaths { get; } = [];

    public Task<string?> GetFilePatchAsync(OratorioRun run, string path, CancellationToken ct)
    {
        RequestedPaths.Add(path);
        return Task.FromResult(_patches.TryGetValue(path, out var patch) ? patch : null);
    }
}

internal sealed class CountingHandler : HttpMessageHandler
{
    public int Count { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization;
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new
            {
                token = "installation-token",
                expires_at = DateTimeOffset.UtcNow.AddMinutes(50)
            })
        };
        return Task.FromResult(response);
    }
}

internal sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}

internal sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class StaticGitHubTokenProvider : IGitHubTokenProvider
{
    public Task<string?> GetBearerTokenAsync(GitHubRepositoryRef repository, CancellationToken ct) => Task.FromResult<string?>("test-token");
}

internal sealed class PassthroughConfigurationSecretProtector : IConfigurationSecretProtector
{
    public bool IsProtected(string? value) => false;

    public string Protect(string value) => value;

    public string? Unprotect(string? value) => value;
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private T _value = value;

    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
    public void Set(T value) => _value = value;
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal sealed class MutableClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}

public enum FakeAppServerOutcome
{
    Success,
    DrawerItemPublishFailure,
    Fail,
    Hold,
    HoldInterruptWithoutTerminal,
    TimeoutAfterTurnStarted,
    TimeoutAfterTurnStartedWithoutTerminal,
    SubmitReviewDraft,
    SubmitSyntheticBadAnchorReviewDraft,
    SubmitRetryAnchorReviewDraft,
    SubmitCleanReviewDraft,
    SubmitSummaryOnlyReviewDraft,
    SubmitSummaryOnlyReviewDraftThenDisconnect,
    SubmitSuggestionTextNotFoundReviewDraft,
    SubmitAmbiguousSuggestionTextReviewDraft,
    SubmitMultiLineReviewDraft,
    SubmitContextLineReviewDraft,
    SubmitNoOpReviewDraft,
    SubmitSummaryOnlyReviewDraftTwice,
    SubmitReviewDraftAfterBackgroundWork,
    CompleteTurnWithoutDraftStreamOpen,
    SubmitImplementationDraft,
    SubmitFollowUpDraft,
    SubmitDiscussionReply,
    Disconnect
}

internal sealed class FakeDotCraftProcessManager : IDotCraftAppServerProcessManager
{
    private static readonly DotCraftAppServerEndpoint Endpoint = new("ws://127.0.0.1:9100/ws", "test");
    private readonly bool _connected;
    private readonly DotCraftAppServerEndpoint? _endpoint;
    private readonly string _message;
    public List<string> EnsureWorkspacePaths { get; } = [];
    public List<string> ProbeWorkspacePaths { get; } = [];

    public FakeDotCraftProcessManager()
        : this(true, Endpoint, "DotCraft AppServer is reachable.")
    {
    }

    public FakeDotCraftProcessManager(bool connected, DotCraftAppServerEndpoint? endpoint, string? message = null)
    {
        _connected = connected;
        _endpoint = endpoint;
        _message = message ?? (connected ? "DotCraft AppServer is reachable." : "DotCraft AppServer is not reachable.");
    }

    public Task<DotCraftAppServerEndpoint> EnsureAvailableAsync(string workspacePath, CancellationToken ct)
    {
        EnsureWorkspacePaths.Add(workspacePath);
        return Task.FromResult(_endpoint ?? Endpoint);
    }

    public Task<DotCraftAppServerProbeResult> ProbeAsync(string workspacePath, CancellationToken ct)
    {
        ProbeWorkspacePaths.Add(workspacePath);
        return Task.FromResult(new DotCraftAppServerProbeResult(_endpoint, _connected, _connected ? "ok" : "unreachable", _message));
    }
}

internal sealed class FakeWorktreeManager : IWorktreeManager
{
    public List<WorktreePrepareRequest> PrepareRequests { get; } = [];
    public List<WorktreeCleanupRequest> CleanupRequests { get; } = [];
    public WorktreeException? Failure { get; set; }
    public string? BaseShaOverride { get; set; }

    public Task<WorktreePrepareResult> PrepareAsync(WorktreePrepareRequest request, CancellationToken ct)
    {
        PrepareRequests.Add(request);
        if (Failure is not null)
        {
            throw Failure;
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Source}:{request.ExternalId}:{request.ItemId}")))[..10].ToLowerInvariant();
        var worktreePath = Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees", key);
        var branch = $"oratorio/run/{key}";
        var baseRef = string.IsNullOrWhiteSpace(request.HeadSha)
            ? string.IsNullOrWhiteSpace(request.SourceBranch) ? "HEAD" : request.SourceBranch
            : request.HeadSha;
        return Task.FromResult(new WorktreePrepareResult(
            request.BaseWorkspacePath,
            worktreePath,
            branch,
            baseRef,
            BaseShaOverride ?? (string.IsNullOrWhiteSpace(request.HeadSha) ? "test-base-sha" : request.HeadSha),
            Path.Combine(request.BaseWorkspacePath, ".craft", "oratorio", "worktrees")));
    }

    public Task CleanupAsync(WorktreeCleanupRequest request, CancellationToken ct)
    {
        CleanupRequests.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class FakeGitDeliveryClient : IGitDeliveryClient
{
    public List<string> ChangedFiles { get; } = ["src/Implementation.cs"];
    public List<string> CommitMessages { get; } = [];
    public List<GitDeliveryPush> Pushes { get; } = [];
    public bool EmptyDiff { get; set; }
    public bool FailPush { get; set; }

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(EmptyDiff ? [] : ChangedFiles);

    public Task<string> CommitAllAsync(string worktreePath, string message, CancellationToken ct)
    {
        CommitMessages.Add(message);
        return Task.FromResult("commit-sha-123");
    }

    public Task PushBranchAsync(string worktreePath, SourceProjectKey project, string branchName, CancellationToken ct)
    {
        if (FailPush)
        {
            throw new InvalidOperationException("Synthetic push failure.");
        }

        Pushes.Add(new GitDeliveryPush(project, branchName));
        return Task.CompletedTask;
    }
}

internal sealed record GitDeliveryPush(SourceProjectKey Project, string BranchName)
{
    public GitHubRepositoryRef Repository
    {
        get
        {
            var parts = Project.ProjectPath.Split('/', 2);
            return new GitHubRepositoryRef(parts[0], parts.Length > 1 ? parts[1] : "");
        }
    }
}

internal sealed record FakeAppServerThreadResumeRequest(
    string ThreadId,
    IReadOnlyList<RuntimeDynamicToolDeclaration>? DynamicTools,
    IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? RuntimeAdditionalContext);

internal static class RuntimeDynamicToolTestExtensions
{
    public static bool ContainsTool(
        this IEnumerable<RuntimeDynamicToolDeclaration> declarations,
        string @namespace,
        string localName) =>
        declarations.OfType<RuntimeDynamicToolNamespace>()
            .Any(group =>
                group.Name == @namespace &&
                group.Tools.OfType<RuntimeDynamicToolFunction>().Any(tool => tool.Name == localName));
}

internal sealed class FakeAppServerClientFactory(FakeAppServerOutcome outcome) : IDotCraftAppServerClientFactory
{
    public FakeAppServerOutcome Outcome { get; set; } = outcome;
    public Queue<FakeAppServerOutcome> ScriptedOutcomes { get; } = new();
    public string? FollowUpDraftRepository { get; set; } = "example-owner/oratorio";
    public string? FollowUpDraftBranch { get; set; } = "main";
    public AppServerThreadStartRequest? LastThreadStartRequest { get; private set; }
    public List<AppServerThreadStartRequest> ThreadStartRequests { get; } = [];
    public List<FakeAppServerThreadResumeRequest> ThreadResumeRequests { get; } = [];
    public int ThreadResumeAttemptCount { get; private set; }
    public int StartThreadCount { get; private set; }
    public int ConnectCount { get; private set; }
    public List<string> StartedThreadIds { get; } = [];
    public List<string> TurnThreadIds { get; } = [];
    public List<string> TurnPrompts { get; } = [];
    public List<(string ThreadId, string TurnId)> InterruptedTurns { get; } = [];
    public bool UseMismatchedToolThreadId { get; set; }
    public Func<string, Task>? WakeTurnStarted { get; set; }
    public bool FailThreadResume { get; set; }
    public bool SupportsRuntimeAdditionalContext { get; set; } = true;
    public DynamicToolResult? LastToolResult { get; set; }
    public List<DynamicToolResult> ToolResults { get; } = [];
    private int _turnCount;

    public Task<IDotCraftAppServerClient> ConnectAsync(string appServerUrl, CancellationToken ct, string? token = null)
    {
        ConnectCount++;
        var connectionOutcome = ScriptedOutcomes.Count > 0 ? ScriptedOutcomes.Dequeue() : Outcome;
        return Task.FromResult<IDotCraftAppServerClient>(new FakeAppServerClient(
            connectionOutcome,
            request =>
            {
                LastThreadStartRequest = request;
                ThreadStartRequests.Add(request);
                StartThreadCount++;
                var threadId = $"thread-test-{StartThreadCount}";
                StartedThreadIds.Add(threadId);
                return threadId;
            },
            resumeRequest =>
            {
                ThreadResumeAttemptCount++;
                if (FailThreadResume)
                {
                    throw new InvalidOperationException("Synthetic thread resume rejection.");
                }

                ThreadResumeRequests.Add(resumeRequest);
            },
            threadId =>
            {
                TurnThreadIds.Add(threadId);
                _turnCount++;
                return $"turn-test-{_turnCount}";
            },
            prompt => TurnPrompts.Add(prompt),
            (threadId, turnId) => InterruptedTurns.Add((threadId, turnId)),
            () => SupportsRuntimeAdditionalContext,
            () => UseMismatchedToolThreadId,
            () => FollowUpDraftRepository,
            () => FollowUpDraftBranch,
            result =>
            {
                LastToolResult = result;
                ToolResults.Add(result);
            },
            turnId => WakeTurnStarted?.Invoke(turnId) ?? Task.CompletedTask));
    }
}

internal sealed class FakeAppServerClient(
    FakeAppServerOutcome outcome,
    Func<AppServerThreadStartRequest, string> startThread,
    Action<FakeAppServerThreadResumeRequest> resumeThread,
    Func<string, string> startTurn,
    Action<string> captureTurnPrompt,
    Action<string, string> interruptTurn,
    Func<bool> supportsRuntimeAdditionalContext,
    Func<bool> useMismatchedToolThreadId,
    Func<string?> getFollowUpDraftRepository,
    Func<string?> getFollowUpDraftBranch,
    Action<DynamicToolResult> captureToolResult,
    Func<string, Task> wakeTurnStarted) : IDotCraftAppServerClient
{
    private readonly Channel<DotCraftRunEvent> _notifications = Channel.CreateUnbounded<DotCraftRunEvent>();
    private Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>>? _dynamicToolHandler;
    private readonly TaskCompletionSource _backgroundWorkObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FakeAppServerThreadResumeRequest? _lastResumeRequest;
    private bool _interrupted;
    private volatile bool _backgroundWork;

    public bool SupportsDynamicToolRebind => true;
    public bool SupportsRuntimeAdditionalContext => supportsRuntimeAdditionalContext();
    public global::DotCraft.Sdk.AppBinding.DotCraftAppBindingClient AppBindings =>
        throw new NotSupportedException("The general AppServer test fake does not exercise App Binding.");

    public void SetDynamicToolHandler(Func<DynamicToolCall, CancellationToken, Task<DynamicToolResult>> handler) =>
        _dynamicToolHandler = handler;

    public Task<string> StartThreadAsync(AppServerThreadStartRequest request, CancellationToken ct)
    {
        var threadId = startThread(request);
        _notifications.Writer.TryWrite(Notification("thread/started", new { threadId }));
        return Task.FromResult(threadId);
    }

    public Task ResumeThreadAsync(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclaration>? dynamicTools,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext,
        CancellationToken ct)
    {
        _lastResumeRequest = new FakeAppServerThreadResumeRequest(threadId, dynamicTools, runtimeAdditionalContext);
        resumeThread(_lastResumeRequest);
        _notifications.Writer.TryWrite(Notification("thread/resumed", new { threadId }));
        return Task.CompletedTask;
    }

    public Task SubscribeThreadAsync(string threadId, CancellationToken ct) => Task.CompletedTask;

    public Task<string?> StartTurnAsync(string threadId, string prompt, CancellationToken ct)
    {
        captureTurnPrompt(prompt);
        var turnId = startTurn(threadId);
        _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId }));
        if (outcome == FakeAppServerOutcome.Success)
        {
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.DrawerItemPublishFailure)
        {
            _notifications.Writer.TryWrite(DisposedNotification("item/started"));
            _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { itemId = "item-after-drawer-failure", threadId, turnId, delta = "DotCraft analysis complete. " }));
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome is FakeAppServerOutcome.SubmitReviewDraft or FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft or FakeAppServerOutcome.SubmitRetryAnchorReviewDraft or FakeAppServerOutcome.SubmitCleanReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect or FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft or FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft or FakeAppServerOutcome.SubmitMultiLineReviewDraft or FakeAppServerOutcome.SubmitContextLineReviewDraft or FakeAppServerOutcome.SubmitNoOpReviewDraft)
        {
            _ = Task.Run(async () =>
            {
                var isSummaryOnly = outcome is FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect;
                if (_dynamicToolHandler is not null)
                {
                    if (outcome == FakeAppServerOutcome.SubmitRetryAnchorReviewDraft)
                    {
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var firstArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding is initially anchored to a full-file line that is not commentable in the diff.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 129,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var firstResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", firstArguments), CancellationToken.None);
                        captureToolResult(firstResult);

                        var retryArguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = 1,
                                minorCount = 0,
                                suggestionCount = 0,
                                body = "Verbose retry review summary from DotCraft."
                            },
                            comments = new object[]
                            {
                                new
                                {
                                    severity = "YELLOW",
                                    title = "Synthetic anchor drift",
                                    body = "This finding was re-anchored to a commentable diff line.",
                                    path = "src/Example/SyntheticFileSearcher.cs",
                                    kind = "commentOnly",
                                    line = 255,
                                    side = "right",
                                    reason = "requiresLargerChange"
                                }
                            }
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var retryResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-2", "oratorio_run", "SubmitReviewDraft", retryArguments), CancellationToken.None);
                        captureToolResult(retryResult);
                    }
                    else
                    {
                        object[] comments = outcome switch
                        {
                            FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => [],
                            FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing suggestion text",
                                body = "This suggestion intentionally references text that is not in the right-side diff.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return missing;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Ambiguous suggestion text",
                                body = "This suggestion intentionally references a repeated right-side snippet.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitMultiLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Refresh order is reversed",
                                body = "The refreshed value should be returned before falling back to the original token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return token;\n        return refreshed;",
                                newText = "        return refreshed;\n        return token;"
                            }
                            },
                            FakeAppServerOutcome.SubmitContextLineReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Context line needs explicit handling",
                                body = "The replacement targets an unchanged diff context line.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "line89",
                                newText = "line89 fixed"
                            }
                            },
                            FakeAppServerOutcome.SubmitNoOpReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "No-op replacement",
                                body = "This suggestion intentionally matches the current diff text.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return refreshed;"
                            }
                            },
                            FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "YELLOW",
                                title = "Synthetic anchor drift",
                                body = "This finding is anchored to a full-file line that is not commentable in the diff.",
                                path = "src/Example/SyntheticFileSearcher.cs",
                                kind = "commentOnly",
                                line = 129,
                                side = "right",
                                reason = "requiresLargerChange"
                            }
                            },
                            FakeAppServerOutcome.SubmitCleanReviewDraft => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            }
                            },
                            _ => new object[]
                            {
                            new
                            {
                                severity = "RED",
                                title = "Missing refresh guard",
                                body = "The refresh path can return the stale token.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "suggestion",
                                oldText = "        return refreshed;",
                                newText = "        return token;"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Validate middleware setup",
                                body = "The middleware should validate before invoking the next step.",
                                path = "src/Auth/JwtMiddleware.cs",
                                kind = "commentOnly",
                                line = 22,
                                side = "right",
                                reason = "needsHumanDecision"
                            },
                            new
                            {
                                severity = "YELLOW",
                                title = "Missing file",
                                body = "This path is not in the PR diff.",
                                path = "missing/File.cs",
                                kind = "commentOnly",
                                line = 5,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            },
                            new
                            {
                                severity = "RED",
                                title = "Bad comment anchor",
                                body = "This line is not commentable on the right side.",
                                path = "src/Auth/RefreshTokenStore.cs",
                                kind = "commentOnly",
                                line = 999,
                                side = "right",
                                reason = "cannotAnchorSafely"
                            }
                            }
                        };
                        var arguments = JsonSerializer.SerializeToElement(new
                        {
                            summary = new
                            {
                                majorCount = isSummaryOnly ? 0 : 1,
                                minorCount = isSummaryOnly ? 0 : 1,
                                suggestionCount = outcome switch
                                {
                                    FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft or FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect => 0,
                                    _ => 1
                                },
                                body = isSummaryOnly
                                    ? "Verbose clean review summary from DotCraft."
                                    : "Verbose review summary from DotCraft."
                            },
                            comments
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                        var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", arguments), CancellationToken.None);
                        captureToolResult(result);
                    }
                }

                var completion = isSummaryOnly
                    ? "DotCraft analysis complete."
                    : "DotCraft review draft submitted.";
                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = completion + " " }));
                if (outcome != FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect)
                {
                    _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = completion }));
                }
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftTwice)
        {
            _ = Task.Run(async () =>
            {
                await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-1", "First review summary from DotCraft.");
                await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-2", "Second review summary from DotCraft.");
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft review draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.CompleteTurnWithoutDraftStreamOpen)
        {
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft analysis complete." }));
        }
        else if (outcome == FakeAppServerOutcome.SubmitReviewDraftAfterBackgroundWork)
        {
            _backgroundWork = true;
            _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "Waiting for the background reviewer." }));
            _ = Task.Run(async () =>
            {
                await _backgroundWorkObserved.Task;
                _backgroundWork = false;
                const string wakeTurnId = "turn-wake-1";
                _notifications.Writer.TryWrite(Notification("turn/started", new { threadId, turnId = wakeTurnId }));
                await wakeTurnStarted(wakeTurnId);
                await SubmitSummaryOnlyDraftAsync(threadId, wakeTurnId, "call-review-1", "Review summary after background work.");
                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft review draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId = wakeTurnId, summary = "DotCraft review draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitImplementationDraft)
        {
            _ = Task.Run(async () =>
            {
                if (_dynamicToolHandler is not null)
                {
                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        summary = "Implemented the requested issue workflow.",
                        tests = new[] { "dotnet test passed" },
                        risks = Array.Empty<string>(),
                        changedFiles = new[] { "src/Implementation.cs" },
                        proposedCommitMessage = "Implement issue workflow",
                        proposedPrTitle = "Implement issue workflow",
                        proposedPrBody = "Implements the requested workflow."
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-implementation-1", "oratorio_run", "SubmitImplementationDraft", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft implementation draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft implementation draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitFollowUpDraft)
        {
            _ = Task.Run(async () =>
            {
                if (_dynamicToolHandler is not null)
                {
                    var reviewArguments = JsonSerializer.SerializeToElement(new
                    {
                        summary = new
                        {
                            majorCount = 0,
                            minorCount = 0,
                            suggestionCount = 0,
                            body = "Verbose clean review summary from DotCraft."
                        },
                        comments = Array.Empty<object>()
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
                    var reviewResult = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-review-1", "oratorio_run", "SubmitReviewDraft", reviewArguments), CancellationToken.None);
                    captureToolResult(reviewResult);

                    var arguments = JsonSerializer.SerializeToElement(new
                    {
                        proposals = new object[]
                        {
                            new
                            {
                                title = "Split out migration cleanup",
                                body = "The migration cleanup is useful but should be handled separately from this review.",
                                rationale = "It touches a different risk area.",
                                repository = getFollowUpDraftRepository(),
                                assignee = "maintainer",
                                branch = getFollowUpDraftBranch(),
                                labels = new[] { "follow-up", "backend" }
                            }
                        }
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-follow-up-1", "oratorio_run", "SubmitFollowUpDraft", arguments), CancellationToken.None);
                    captureToolResult(result);
                }

                _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft follow-up draft submitted. " }));
                _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft follow-up draft submitted." }));
                _notifications.Writer.TryComplete();
            });
        }
        else if (outcome == FakeAppServerOutcome.SubmitDiscussionReply)
        {
            return ReplyBeforeTurnStartReturnsAsync(threadId, turnId, prompt);
        }
        else if (outcome == FakeAppServerOutcome.Fail)
        {
            _notifications.Writer.TryWrite(Notification("turn/failed", new { threadId, turnId, error = new { message = "Synthetic DotCraft failure." } }));
            _notifications.Writer.TryComplete();
        }
        else if (outcome == FakeAppServerOutcome.Disconnect)
        {
            _notifications.Writer.TryComplete();
        }

        return Task.FromResult<string?>(turnId);
    }

    private async Task<string?> ReplyBeforeTurnStartReturnsAsync(string threadId, string turnId, string prompt)
    {
        if (_dynamicToolHandler is not null)
        {
            var arguments = JsonSerializer.SerializeToElement(new
            {
                discussionTurnId = ExtractDiscussionTurnId(prompt, _lastResumeRequest?.RuntimeAdditionalContext),
                body = "Agent answer from DotCraft."
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
            var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-discussion-1", "oratorio_run", "SubmitDiscussionReply", arguments), CancellationToken.None);
            captureToolResult(result);
        }

        _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft discussion reply submitted. " }));
        _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId, summary = "DotCraft discussion reply submitted." }));
        _notifications.Writer.TryComplete();
        return turnId;
    }

    public Task<string?> StartTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, string? modelId, CancellationToken ct) =>
        StartTurnAsync(threadId, input.FirstOrDefault()?.Text ?? "", ct);

    public Task<string?> EnqueueTurnAsync(string threadId, IReadOnlyList<TurnInputPartDto> input, CancellationToken ct) =>
        Task.FromResult<string?>("queued-test-1");

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct)
    {
        interruptTurn(threadId, turnId);
        _interrupted = true;
        if (outcome is not (FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal or FakeAppServerOutcome.HoldInterruptWithoutTerminal))
        {
            _notifications.Writer.TryWrite(Notification("turn/cancelled", new { threadId, turnId }));
        }

        return Task.CompletedTask;
    }

    public Task<AppServerThreadReadResult> ReadThreadAsync(string threadId, CancellationToken ct) =>
        Task.FromResult(new AppServerThreadReadResult(threadId, []));

    public Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModelInfoDto>>([new("fake-model", "Fake Model", "test")]);

    public Task<bool> HasBackgroundWorkAsync(string threadId, CancellationToken ct)
    {
        var running = _backgroundWork;
        if (running)
        {
            _backgroundWorkObserved.TrySetResult();
        }

        return Task.FromResult(running);
    }

    private async Task SubmitSummaryOnlyDraftAsync(string threadId, string turnId, string callId, string body)
    {
        var arguments = JsonSerializer.SerializeToElement(new
        {
            summary = new { majorCount = 0, minorCount = 0, suggestionCount = 0, body },
            comments = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        captureToolResult(await _dynamicToolHandler!(CreateCall(threadId, turnId, callId, "oratorio_run", "SubmitReviewDraft", arguments), CancellationToken.None));
    }

    public IAsyncEnumerable<DotCraftRunEvent> ReadEventsAsync(CancellationToken ct) =>
        outcome is FakeAppServerOutcome.TimeoutAfterTurnStarted or FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal ||
        outcome == FakeAppServerOutcome.HoldInterruptWithoutTerminal && _interrupted
            ? ReadEventsThenCancelAsync(ct)
            : _notifications.Reader.ReadAllAsync(ct);

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private static DotCraftRunEvent Notification(string method, object parameters)
    {
        var json = JsonSerializer.SerializeToElement(parameters, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var raw = new AppServerNotification(method, json);
        var threadId = String(json, "threadId") ?? "";
        var turnId = String(json, "turnId");
        if (method.StartsWith("turn/", StringComparison.Ordinal))
        {
            var turn = new SessionTurn
            {
                Id = turnId ?? "turn-test",
                ThreadId = threadId,
                Status = method["turn/".Length..],
                StartedAt = DateTimeOffset.UnixEpoch,
                Error = Error(json)
            };
            var value = new TurnNotification { Turn = turn, Error = Error(json) };
            return new DotCraftRunEvent<TurnNotification>(method, threadId, turn.Id, value, raw);
        }

        if (method.Contains("/delta", StringComparison.OrdinalIgnoreCase)
            || method.EndsWith("Delta", StringComparison.OrdinalIgnoreCase))
        {
            var value = new ItemDeltaNotification
            {
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = String(json, "itemId"),
                DeltaKind = String(json, "deltaKind"),
                Delta = String(json, "delta") ?? ""
            };
            return new DotCraftRunEvent<ItemDeltaNotification>(method, threadId, turnId, value, raw);
        }

        return new DotCraftRunEvent(method, threadId, turnId, raw);
    }

    private static DynamicToolCall CreateCall(
        string threadId,
        string turnId,
        string callId,
        string? toolNamespace,
        string tool,
        JsonElement arguments) =>
        new()
        {
            ThreadId = threadId,
            TurnId = turnId,
            CallId = callId,
            Namespace = toolNamespace,
            Tool = tool,
            Arguments = arguments
        };

    private async IAsyncEnumerable<DotCraftRunEvent> ReadEventsThenCancelAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (_notifications.Reader.TryRead(out var notification))
        {
            yield return notification;

            if (notification.Type is "turn/completed" or "turn/failed" or "turn/cancelled")
            {
                yield break;
            }
        }

        await Task.Yield();
        throw new OperationCanceledException(ct);
    }

    private static string ExtractDiscussionTurnId(
        string prompt,
        IReadOnlyDictionary<string, AppServerRuntimeAdditionalContextEntry>? runtimeAdditionalContext)
    {
        const string marker = "discussionTurnId `";
        var context = runtimeAdditionalContext is not null &&
            runtimeAdditionalContext.TryGetValue("oratorio.discussionTurn", out var entry)
            ? entry.Value
            : null;
        var sources = string.IsNullOrWhiteSpace(context)
            ? new[] { prompt }
            : new[] { context!, prompt };
        foreach (var source in sources)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = source.IndexOf('`', start);
            return end > start ? source[start..end] : "missing-discussion-turn";
        }

        return "missing-discussion-turn";
    }

    private static DotCraftRunEvent DisposedNotification(string method)
    {
        var document = JsonDocument.Parse("""{"text":"unavailable"}""");
        var payload = document.RootElement;
        document.Dispose();
        var raw = new AppServerNotification(method, JsonSerializer.SerializeToElement(new { }));
        var parameters = new ItemNotification
        {
            ThreadId = "thread-disposed",
            TurnId = "turn-disposed",
            Item = new SessionItem
            {
                Id = "item-disposed",
                TurnId = "turn-disposed",
                Type = "agentMessage",
                Status = "completed",
                CreatedAt = DateTimeOffset.UnixEpoch,
                PayloadKind = "agentMessage",
                Payload = payload
            }
        };
        return new DotCraftRunEvent<ItemNotification>(method, parameters.ThreadId, parameters.TurnId, parameters, raw);
    }

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? Error(JsonElement value)
    {
        if (String(value, "error") is { } direct)
            return direct;
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
            return String(error, "message");
        return null;
    }
}

internal static class TestHelpers
{
    public static string Signature(string body, string secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }
}
