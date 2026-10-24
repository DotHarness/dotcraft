using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubMentionScanTests
{
    private const string Repository = "example-owner/oratorio";

    [Fact]
    public async Task ScannedReviewCommand_WithoutWebhook_DispatchesOnce()
    {
        var harness = new ScanHarness(new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:WebhookSecret"] = ""
        });
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9601, "@dotcraft-ai /review token expiry");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        var run = await db.Runs.AsNoTracking().SingleAsync();
        Assert.Equal("issue-comment:9601", command.SourceCommentId);
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Equal("token expiry", command.Focus);
        Assert.Equal(run.RunId, command.RunId);
        Assert.Equal(RunDispatchTrigger.GitHubMentionReview, run.DispatchTrigger);
        var reaction = Assert.Single(harness.GitHub.Reactions);
        Assert.Equal(GitHubCommentKind.Conversation, reaction.Kind);
        Assert.Equal(9601, reaction.CommentId);
        Assert.Equal("eyes", reaction.Content);
        Assert.NotNull(command.AcknowledgedAt);
    }

    [Fact]
    public async Task ScannedInlineReviewCommand_ReactsOnReviewComment()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddReviewComment(9701, "@dotcraft-ai review");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal("review-comment:9701", command.SourceCommentId);
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Single(await db.Runs.AsNoTracking().ToListAsync());
        var reaction = Assert.Single(harness.GitHub.Reactions);
        Assert.Equal(GitHubCommentKind.ReviewComment, reaction.Kind);
        Assert.Equal(9701, reaction.CommentId);
    }

    [Fact]
    public async Task WebhookScanRescanAndEdit_ProduceOneCommandRunAndReaction()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var comment = harness.AddConversationComment(9801, "@dotcraft-ai review");
        var response = await harness.PostWebhookAsync(comment);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await harness.ScanAsync();
        await harness.ProcessAllAsync();
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        harness.EditConversationComment(9801, "@dotcraft-ai /review something else");
        await harness.ScanAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.EditConversationComment(9801, "@dotcraft-ai /review a third thing");
        await harness.ScanAsync();
        var redelivery = await harness.PostWebhookAsync(comment);
        Assert.Equal(HttpStatusCode.OK, redelivery.StatusCode);
        await harness.ProcessAllAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Null(command.Focus);
        Assert.Single(await db.Runs.AsNoTracking().ToListAsync());
        Assert.Single(harness.GitHub.Reactions);
    }

    [Fact]
    public async Task CommentCreatedBeforeBaseline_IsNeverACandidate()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        var createdAt = harness.Clock.UtcNow.AddMinutes(-5);
        harness.GitHub.RepositoryIssueComments[Repository] =
        [
            ConversationComment(9901, "@dotcraft-ai review", createdAt, createdAt)
        ];
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.EditConversationComment(9901, "@dotcraft-ai /review edited");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        await harness.AssertNoCommandWorkAsync(expectRecord: false);
    }

    [Fact]
    public async Task BotComment_IsNeverRecorded()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9902, "@dotcraft-ai review", userType: "Bot");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        await harness.AssertNoCommandWorkAsync(expectRecord: false);
    }

    [Fact]
    public async Task UnaffiliatedCommenter_IsRejectedWithoutReaction()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9903, "@dotcraft-ai review", association: "CONTRIBUTOR");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        var command = await harness.AssertNoCommandWorkAsync(expectRecord: true);
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command!.Status);
        Assert.Equal("githubCommentCommandActorUnauthorized", command.ErrorCode);
    }

    [Fact]
    public async Task ClosedPullRequest_IsRejectedWithoutRunOrReaction()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        harness.GitHub.PullRequests[0] = harness.GitHub.PullRequests[0] with
        {
            State = "closed",
            ClosedAt = harness.Clock.UtcNow
        };
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9904, "@dotcraft-ai review");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        var command = await harness.AssertNoCommandWorkAsync(expectRecord: true);
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command!.Status);
        Assert.Equal("githubCommentCommandPullRequestNotOpen", command.ErrorCode);
        Assert.NotNull(command.CompletedAt);
    }

    [Fact]
    public async Task RepositoryWithoutWorkspaceMapping_IsNotScanned()
    {
        var harness = new ScanHarness(new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:Repositories:1"] = "example-owner/no-workspace"
        });
        await using var app = harness.App;
        await harness.ScanAsync();

        Assert.All(harness.GitHub.CommentScanRequests, request => Assert.Equal(Repository, request.Repository));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var cursor = await db.GitHubCommentScanCursors.AsNoTracking().SingleAsync();
        Assert.Equal(Repository, cursor.Repository);
    }

    [Fact]
    public async Task UnchangedRepository_UsesConditionalRequestAndRecordsNoError()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        await harness.ScanAsync();
        harness.GitHub.CommentScanRequests.Clear();

        await harness.ScanAsync();

        Assert.Equal(2, harness.GitHub.CommentScanRequests.Count);
        Assert.All(harness.GitHub.CommentScanRequests, request =>
        {
            Assert.NotNull(request.ETag);
            Assert.True(request.NotModified);
        });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var cursor = await db.GitHubCommentScanCursors.AsNoTracking().SingleAsync();
        Assert.Null(cursor.LastErrorCode);
        Assert.Null(cursor.LastErrorAt);
        Assert.NotNull(cursor.LastScannedAt);
    }

    [Fact]
    public async Task OneRepositoryFailure_DoesNotBlockAnother()
    {
        var brokenWorkspace = Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.broken");
        Directory.CreateDirectory(brokenWorkspace);
        var harness = new ScanHarness(new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:Repositories:0"] = "example-owner/broken",
            ["Oratorio:GitHub:Repositories:1"] = Repository,
            ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:Project"] = "github:github.com/example-owner/broken",
            ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:WorkspacePath"] = brokenWorkspace
        });
        await using var app = harness.App;
        harness.GitHub.FailingCommentScanRepositories.Add("example-owner/broken");
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9905, "@dotcraft-ai review");
        await harness.ScanAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var cursors = await db.GitHubCommentScanCursors.AsNoTracking().ToDictionaryAsync(x => x.Repository);
        Assert.Equal("githubCommentScanFailed", cursors["example-owner/broken"].LastErrorCode);
        Assert.NotNull(cursors["example-owner/broken"].LastErrorAt);
        Assert.Null(cursors[Repository].LastErrorCode);
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(Repository, command.Repository);
        Assert.Equal(GitHubCommentCommandStatus.Queued, command.Status);
    }

    [Fact]
    public async Task ReactionFailure_DoesNotBlockDispatch()
    {
        var harness = new ScanHarness();
        await using var app = harness.App;
        harness.GitHub.FailNextReactionCount = 1;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9906, "@dotcraft-ai review");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        var run = await db.Runs.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Equal(run.RunId, command.RunId);
        Assert.Null(command.AcknowledgedAt);
        Assert.Equal("githubCommentCommandReactionFailed", command.AcknowledgementErrorCode);
        Assert.Empty(harness.GitHub.Reactions);
    }

    [Fact]
    public async Task WritesDisabled_DispatchesWithoutReaction()
    {
        var harness = new ScanHarness(new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:WritesEnabled"] = "false"
        });
        await using var app = harness.App;
        await harness.ScanAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.AddConversationComment(9907, "@dotcraft-ai review");
        await harness.ScanAsync();
        await harness.ProcessAllAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Empty(harness.GitHub.Reactions);
    }

    private static GitHubComment ConversationComment(
        long id,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string association = "MEMBER",
        string userType = "User") =>
        new(
            id,
            body,
            $"https://github.example.test/{Repository}/pull/184#issuecomment-{id}",
            new GitHubUser("example-reviewer", userType),
            createdAt,
            updatedAt,
            association,
            $"https://api.github.example.test/repos/{Repository}/issues/184");

    private sealed class ScanHarness
    {
        public ScanHarness(IReadOnlyDictionary<string, string?>? settings = null)
        {
            App = new TestOratorioApp(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(GitHub);
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(Clock);
            }, settings);
            Client = App.CreateClient();
        }

        public TestOratorioApp App { get; }
        public HttpClient Client { get; }
        public FakeGitHubApiClient GitHub { get; } = new();
        public MutableClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-24T08:00:00Z"));

        public GitHubComment AddConversationComment(
            long id,
            string body,
            string association = "MEMBER",
            string userType = "User")
        {
            var comment = ConversationComment(id, body, Clock.UtcNow, Clock.UtcNow, association, userType);
            Comments(GitHub.RepositoryIssueComments).Add(comment);
            return comment;
        }

        public void EditConversationComment(long id, string body)
        {
            var comments = Comments(GitHub.RepositoryIssueComments);
            var index = comments.FindIndex(x => x.Id == id);
            comments[index] = comments[index] with { Body = body, UpdatedAt = Clock.UtcNow };
        }

        public void AddReviewComment(long id, string body) =>
            Comments(GitHub.RepositoryReviewComments).Add(new GitHubReviewComment(
                id,
                body,
                $"https://github.example.test/{Repository}/pull/184#discussion_r{id}",
                "src/Auth/RefreshTokenStore.cs",
                88,
                88,
                new GitHubUser("example-reviewer", "User"),
                Clock.UtcNow,
                Clock.UtcNow,
                "COLLABORATOR",
                $"https://api.github.example.test/repos/{Repository}/pulls/184"));

        public async Task ScanAsync()
        {
            using var scope = App.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<GitHubCommentScanService>().ScanAsync(CancellationToken.None);
        }

        public async Task ProcessAllAsync()
        {
            while (true)
            {
                using var scope = App.Services.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<GitHubCommentCommandProcessor>();
                if (!await processor.ProcessNextAsync(CancellationToken.None))
                {
                    return;
                }
            }
        }

        public async Task<HttpResponseMessage> PostWebhookAsync(GitHubComment comment)
        {
            var payload = JsonSerializer.Serialize(new
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
                    id = comment.Id,
                    body = comment.Body,
                    html_url = comment.HtmlUrl,
                    created_at = comment.CreatedAt,
                    author_association = comment.AuthorAssociation,
                    user = new { login = comment.User!.Login, type = comment.User.Type }
                },
                sender = new { login = comment.User.Login, type = comment.User.Type }
            });
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-GitHub-Event", "issue_comment");
            request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", Guid.NewGuid().ToString("n"));
            request.Headers.TryAddWithoutValidation(
                "X-Hub-Signature-256",
                "sha256=" + Convert.ToHexString(
                    HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(payload)))
                    .ToLowerInvariant());
            return await Client.SendAsync(request);
        }

        public async Task<OratorioGitHubCommentCommand?> AssertNoCommandWorkAsync(bool expectRecord)
        {
            using var scope = App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var commands = await db.GitHubCommentCommands.AsNoTracking().ToListAsync();
            Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
            Assert.Empty(GitHub.Reactions);
            if (!expectRecord)
            {
                Assert.Empty(commands);
                return null;
            }

            return Assert.Single(commands);
        }

        private static List<T> Comments<T>(Dictionary<string, List<T>> source)
        {
            if (!source.TryGetValue(Repository, out var comments))
            {
                comments = [];
                source[Repository] = comments;
            }

            return comments;
        }
    }
}
