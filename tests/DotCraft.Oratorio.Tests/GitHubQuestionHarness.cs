using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

internal sealed class GitHubQuestionHarness
{
    public const string Repository = "example-owner/oratorio";

    public GitHubQuestionHarness(FakeAppServerOutcome outcome, IReadOnlyDictionary<string, string?>? settings = null)
    {
        AppServer = new FakeAppServerClientFactory(outcome);
        App = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(GitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(AppServer);
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager>(Worktrees);
        }, settings);
        Client = App.CreateClient();
    }

    public TestOratorioApp App { get; }
    public HttpClient Client { get; }
    public FakeGitHubApiClient GitHub { get; } = new();
    public FakeAppServerClientFactory AppServer { get; }
    public FakeWorktreeManager Worktrees { get; } = new();

    public async Task<string> SyncPullRequestAsync()
    {
        await PostAsync<GitHubSyncResponse>(Client, "/api/v1/sources/github/sync", new { });
        var list = await Client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        return Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest).ItemId!;
    }

    public async Task<string> ReviewPullRequestAsync()
    {
        var itemId = await SyncPullRequestAsync();
        await PostAsync<ItemDetailResponse>(
            Client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(Client, itemId, x => x.Item.State == ItemState.AwaitingReview);
        return itemId;
    }

    public Task<ItemDetailResponse> DetailAsync(string itemId) =>
        Client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions)!;

    public Task<ItemDetailResponse> WaitForAnsweredAsync(string itemId) =>
        WaitForItemByIdAsync(
            Client,
            itemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded && turn.ReplySourceWriteId != null));

    public Task<ItemDetailResponse> WaitForReplyWriteAsync(string itemId, SourceWriteStatus status) =>
        WaitForItemByIdAsync(
            Client,
            itemId,
            x => x.DiscussionTurns.Any(turn =>
                turn.Status == DiscussionTurnStatus.Succeeded &&
                x.SourceWrites.Any(write => write.WriteId == turn.ReplySourceWriteId && write.Status == status)));

    public async Task<OratorioGitHubCommentCommand> LoadCommandAsync()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
    }

    public static string CommentUrl(long id) => $"https://github.example.test/{Repository}/pull/184#issuecomment-{id}";

    public static string ReviewCommentUrl(long id) => $"https://github.example.test/{Repository}/pull/184#discussion_r{id}";

    public Task<HttpResponseMessage> AskOnIssueAsync(long commentId, string body) =>
        PostWebhookAsync("issue_comment", new
        {
            action = "created",
            repository = new { full_name = Repository },
            issue = new { number = 42 },
            comment = new
            {
                id = commentId,
                body,
                html_url = $"https://github.example.test/{Repository}/issues/42#issuecomment-{commentId}",
                created_at = DateTimeOffset.UtcNow,
                author_association = "MEMBER",
                user = new { login = "example-reviewer", type = "User" }
            }
        });

    public Task AskOnGitHubAsync(long commentId, string body) =>
        SendWebhookAsync("issue_comment", new
        {
            action = "created",
            repository = new { full_name = Repository },
            issue = new
            {
                number = 184,
                pull_request = new { url = $"https://api.github.com/repos/{Repository}/pulls/184" }
            },
            comment = new
            {
                id = commentId,
                body,
                html_url = CommentUrl(commentId),
                created_at = DateTimeOffset.UtcNow,
                author_association = "MEMBER",
                user = new { login = "example-reviewer", type = "User" }
            },
            sender = new { login = "example-reviewer", type = "User" }
        });

    public Task AskInlineOnGitHubAsync(long commentId, long? inReplyToId, string path, int line, string body) =>
        SendWebhookAsync("pull_request_review_comment", new
        {
            action = "created",
            repository = new { full_name = Repository },
            pull_request = new { number = 184 },
            comment = new
            {
                id = commentId,
                in_reply_to_id = inReplyToId,
                body,
                path,
                line,
                html_url = ReviewCommentUrl(commentId),
                created_at = DateTimeOffset.UtcNow,
                author_association = "MEMBER",
                user = new { login = "example-reviewer", type = "User" }
            },
            sender = new { login = "example-reviewer", type = "User" }
        });

    private async Task SendWebhookAsync(string eventName, object body)
    {
        using var response = await PostWebhookAsync(eventName, body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string eventName, object body)
    {
        var payload = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-GitHub-Event", eventName);
        request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", Guid.NewGuid().ToString("n"));
        request.Headers.TryAddWithoutValidation(
            "X-Hub-Signature-256",
            "sha256=" + Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(payload)))
                .ToLowerInvariant());
        return await Client.SendAsync(request);
    }
}
