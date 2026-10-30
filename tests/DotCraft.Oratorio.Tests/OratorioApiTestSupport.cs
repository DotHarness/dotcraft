using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.Sources;
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
            new StaticGitHubTokenProvider(),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()),
            new FixedClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z")));

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
    HoldWakeTurnAfterBackgroundWork,
    HoldBackgroundWork,
    SubmitImplementationDraft,
    SubmitImplementationDraftAfterSteer,
    SubmitFollowUpDraft,
    SubmitDiscussionReply,
    DismissFindingThenReply,
    SubmitDiscussionReplyTwice,
    ReplyAfterBackgroundWork,
    ReplyOnSteer,
    SubmitReviewDraftAfterSteer,
    SubmitReviewDraftOnSteer,
    ReplyOnResumeAfterBackgroundWork,
    HoldBackgroundWorkOnResume,
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
    public List<FakeAppServerSteer> SteerRequests { get; } = [];
    public bool UseMismatchedToolThreadId { get; set; }
    public Func<string, Task>? WakeTurnStarted { get; set; }
    public Task SteeredRunRelease { get; set; } = Task.CompletedTask;
    public bool FailThreadResume { get; set; }
    public int RejectTurnStarts { get; set; }
    public bool SupportsRuntimeAdditionalContext { get; set; } = true;
    public DynamicToolResult? LastToolResult { get; set; }
    public List<DynamicToolResult> ToolResults { get; } = [];
    private int _turnCount;
    private readonly TaskCompletionSource _firstTurnStartedProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task FirstTurnStartedProcessed => _firstTurnStartedProcessed.Task;

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
                if (RejectTurnStarts > 0)
                {
                    RejectTurnStarts--;
                    throw new InvalidOperationException("Synthetic turn start rejection.");
                }

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
            turnId => WakeTurnStarted?.Invoke(turnId) ?? Task.CompletedTask,
            steer => SteerRequests.Add(steer))
        {
            LastSteeredPrompt = () => SteerRequests.LastOrDefault()?.Prompt,
            TurnStartedProcessed = () => _firstTurnStartedProcessed.TrySetResult(),
            SteeredRunRelease = () => SteeredRunRelease
        });
    }
}

internal sealed record FakeAppServerSteer(string ThreadId, string ExpectedTurnId, string Prompt);

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
