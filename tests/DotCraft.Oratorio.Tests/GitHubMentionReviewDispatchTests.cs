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
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.GitHub;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubMentionReviewDispatchTests
{
    [Fact]
    public async Task QueuedCommand_SynchronizesExactPullRequest_AndDispatchesCurrentHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            DisableHostedServices(services);
            ReplaceGitHub(services, fakeGitHub);
        });
        var client = app.CreateClient();
        await PostReviewCommandAsync(client, 9101, "@dotcraft-ai review for security regressions");

        using var scope = app.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<GitHubCommentCommandProcessor>();
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        db.ChangeTracker.Clear();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        var run = await db.Runs.AsNoTracking().SingleAsync();
        var item = await db.Items.AsNoTracking().SingleAsync(x => x.ItemId == run.ItemId);
        var queuedEvent = await db.TimelineEvents.AsNoTracking()
            .SingleAsync(x => x.RunId == run.RunId && x.Kind == TimelineEventKind.RunQueued);

        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Equal(run.RunId, command.RunId);
        Assert.Equal(RunDispatchTrigger.GitHubMentionReview, run.DispatchTrigger);
        Assert.Equal(RunPurpose.ReviewAnalysis, run.Purpose);
        Assert.Equal("appServer", run.RunnerKind);
        Assert.Equal("abc123", run.TargetHeadSha);
        Assert.Equal("pr:example-owner/oratorio#184", item.ExternalId);
        Assert.Equal("abc123", item.HeadSha);
        Assert.Contains("Review focus: security regressions", queuedEvent.Body);
        Assert.Equal(1, fakeGitHub.GetPullRequestCallCount);
        Assert.Empty(fakeGitHub.IssueStateArguments);
        Assert.Empty(fakeGitHub.PullRequestStateArguments);
    }

    [Fact]
    public async Task CompatibleActiveReview_IsSharedByDistinctCommands()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            DisableHostedServices(services);
            ReplaceGitHub(services, fakeGitHub);
        });
        var client = app.CreateClient();

        await PostReviewCommandAsync(client, 9201, "@dotcraft-ai review");
        using (var firstScope = app.Services.CreateScope())
        {
            var processor = firstScope.ServiceProvider.GetRequiredService<GitHubCommentCommandProcessor>();
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        }

        await PostReviewCommandAsync(client, 9202, "@dotcraft-ai review for authentication");
        using (var secondScope = app.Services.CreateScope())
        {
            var processor = secondScope.ServiceProvider.GetRequiredService<GitHubCommentCommandProcessor>();
            Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        }

        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var commands = await db.GitHubCommentCommands.AsNoTracking()
            .OrderBy(x => x.SourceCommentId)
            .ToListAsync();
        var run = await db.Runs.AsNoTracking().SingleAsync();

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command =>
        {
            Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
            Assert.Equal(run.RunId, command.RunId);
        });
        Assert.Equal(2, fakeGitHub.GetPullRequestCallCount);
        Assert.Single(fakeGitHub.CheckRuns);
    }

    [Fact]
    public async Task TransientGitHubReadFailure_IsRetriedTwice_ThenFails()
    {
        var fakeGitHub = new FakeGitHubApiClient
        {
            FailNextGetPullRequestCount = 3
        };
        await using var app = new TestOratorioApp(services =>
        {
            DisableHostedServices(services);
            ReplaceGitHub(services, fakeGitHub);
        });
        var client = app.CreateClient();
        await PostReviewCommandAsync(client, 9301, "@dotcraft-ai review");

        using var scope = app.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<GitHubCommentCommandProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        db.ChangeTracker.Clear();
        var command = await db.GitHubCommentCommands.SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Queued, command.Status);
        Assert.Equal(1, command.AttemptCount);
        Assert.NotNull(command.NextAttemptAt);

        command.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        db.ChangeTracker.Clear();
        command = await db.GitHubCommentCommands.SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Queued, command.Status);
        Assert.Equal(2, command.AttemptCount);
        Assert.NotNull(command.NextAttemptAt);

        command.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));
        db.ChangeTracker.Clear();
        command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubCommentCommandStatus.Failed, command.Status);
        Assert.Equal(3, command.AttemptCount);
        Assert.Null(command.NextAttemptAt);
        Assert.Equal("githubCommentCommandGitHubReadFailed", command.ErrorCode);
        Assert.NotNull(command.CompletedAt);
        Assert.Equal(3, fakeGitHub.GetPullRequestCallCount);
        Assert.Empty(await db.Runs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MentionReview_PublishesCommentReview_WhenOrdinaryAutoPublishIsDisabled()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            ReplaceGitHub(services, fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "false"
        });
        var client = app.CreateClient();

        await PostReviewCommandAsync(client, 9401, "@dotcraft-ai review for security regressions");
        await WaitUntilAsync(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            return await db.SourceWriteLogs.AsNoTracking().AnyAsync(x =>
                x.Intent == "reviewDraftPublish" && x.Status == SourceWriteStatus.Succeeded);
        });

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var command = await db.GitHubCommentCommands.AsNoTracking().SingleAsync();
        var run = await db.Runs.AsNoTracking().SingleAsync(x => x.RunId == command.RunId);
        var draft = await db.ReviewDrafts.AsNoTracking().SingleAsync(x => x.RunId == run.RunId);
        var publishWrite = await db.SourceWriteLogs.AsNoTracking()
            .SingleAsync(x => x.ItemId == run.ItemId && x.Intent == "reviewDraftPublish");
        var review = Assert.Single(fakeGitHub.PullRequestReviews);

        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Equal(RunDispatchTrigger.GitHubMentionReview, run.DispatchTrigger);
        Assert.Equal("abc123", run.TargetHeadSha);
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        Assert.Equal(SourceWriteStatus.Succeeded, publishWrite.Status);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("abc123", review.CommitId);
        Assert.DoesNotContain(fakeGitHub.PullRequestReviews, x => x.Event is "APPROVE" or "REQUEST_CHANGES");
        Assert.Contains(
            fakeAppServer.TurnPrompts,
            prompt => prompt.Contains("Review focus: security regressions", StringComparison.Ordinal));
        Assert.Empty(fakeGitHub.IssueStateArguments);
        Assert.Empty(fakeGitHub.PullRequestStateArguments);
    }

    [Fact]
    public async Task MentionReview_AutoPublishStillBlocksDraftWarnings()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitNoOpReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            ReplaceGitHub(services, fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        }, settings: new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewPublishEnabled"] = "false"
        });
        var client = app.CreateClient();

        await PostReviewCommandAsync(client, 9501, "@dotcraft-ai review");
        await WaitUntilAsync(async () =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            return await db.SourceWriteLogs.AsNoTracking()
                .AnyAsync(x => x.Intent == "reviewDraftPublish" && x.ErrorCode == "reviewDraftWarnings");
        });

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var draft = await verifyDb.ReviewDrafts.AsNoTracking().SingleAsync();
        var write = await verifyDb.SourceWriteLogs.AsNoTracking()
            .SingleAsync(x => x.Intent == "reviewDraftPublish");
        Assert.Equal(ReviewDraftStatus.PublishFailed, draft.Status);
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("reviewDraftWarnings", write.ErrorCode);
        Assert.Empty(fakeGitHub.PullRequestReviews);
    }

    private static async Task PostReviewCommandAsync(
        HttpClient client,
        long commentId,
        string body)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action = "created",
            repository = new { full_name = "example-owner/oratorio" },
            issue = new
            {
                number = 184,
                pull_request = new
                {
                    url = "https://api.github.com/repos/example-owner/oratorio/pulls/184"
                }
            },
            comment = new
            {
                id = commentId,
                body,
                author_association = "MEMBER",
                user = new { login = "example-reviewer", type = "User" }
            },
            sender = new { login = "example-reviewer", type = "User" }
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-GitHub-Event", "issue_comment");
        request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", $"delivery-{commentId}");
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", Sign(payload, "test-secret"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        int timeoutSeconds = 20)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Condition was not met within {timeoutSeconds} seconds.");
    }

    private static void ReplaceGitHub(IServiceCollection services, FakeGitHubApiClient fakeGitHub)
    {
        services.RemoveAll<IGitHubApiClient>();
        services.AddSingleton<IGitHubApiClient>(fakeGitHub);
    }

    private static void DisableHostedServices(IServiceCollection services) =>
        services.RemoveAll<IHostedService>();

    private static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
}
