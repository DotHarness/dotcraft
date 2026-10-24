using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class DiscussionTurnDeliveryTests
{
    [Fact]
    public async Task DesktopQuestionDuringRun_IsSteeredAndAnsweredInternally()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(fakeAppServer, fakeGitHub);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/request-changes",
            new DecisionRequest("Please look again."));

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitReviewDraftAfterSteer;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Second pass.", null, null));
        var running = await WaitForItemByIdAsync(client, itemId, x => x.Runs.Any(run => run.Status == RunStatus.Running && run.TurnId != null));
        var run = Assert.Single(running.Runs, x => x.Status == RunStatus.Running);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Is the second pass covering the refresh path?", null, null));
        var answered = await WaitForItemByIdAsync(
            client,
            itemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded) && x.Item.State == ItemState.AwaitingReview);

        var steer = Assert.Single(fakeAppServer.SteerRequests);
        Assert.Equal(run.TurnId, steer.ExpectedTurnId);
        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(run.RunId, turn.RunId);
        Assert.Null(turn.Origin);
        Assert.Null(turn.ReplySourceWriteId);
        Assert.Empty(fakeGitHub.IssueComments);
        Assert.DoesNotContain(answered.SourceWrites, x => x.Intent == GitHubWriteService.DiscussionReplyIntent);
        Assert.Equal(RunStatus.Succeeded, answered.Runs.Single(x => x.RunId == run.RunId).Status);
    }

    [Fact]
    public async Task SecondReplyForSameQuestion_IsRejected()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(fakeAppServer);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);

        var resultsBefore = fakeAppServer.ToolResults.Count;
        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReplyTwice;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Answer twice?", null, null));
        await WaitUntilAsync(() => fakeAppServer.ToolResults.Count == resultsBefore + 2);
        var answered = await WaitForItemByIdAsync(client, itemId, x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded));

        Assert.True(fakeAppServer.ToolResults[resultsBefore].Success);
        Assert.False(fakeAppServer.ToolResults[resultsBefore + 1].Success);
        Assert.Equal("DiscussionTurnAlreadyAnswered", fakeAppServer.ToolResults[resultsBefore + 1].ErrorCode);
        Assert.Single(answered.Comments, x => x.Purpose == CommentPurpose.DiscussionReply);
    }

    [Fact]
    public async Task QuestionAnsweredAfterBackgroundWork_IsNotFailedAtFirstTurnEnd()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(fakeAppServer);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);

        fakeAppServer.Outcome = FakeAppServerOutcome.ReplyAfterBackgroundWork;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Can a sub-agent check this?", null, null));
        var answered = await WaitForItemByIdAsync(client, itemId, x => x.DiscussionTurns.Any(turn => turn.Status != DiscussionTurnStatus.Pending && turn.Status != DiscussionTurnStatus.Running));

        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        Assert.Null(turn.ErrorCode);
        Assert.True(fakeAppServer.LastToolResult!.Success);
    }

    [Fact]
    public async Task DesktopQuestionDuringFirstRun_IsSteeredIntoThatRun()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraftAfterSteer);
        await using var app = CreateApp(fakeAppServer);
        var client = app.CreateClient();
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var itemId = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest).ItemId!;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "First pass.", null, null));
        var running = await WaitForItemByIdAsync(client, itemId, x => x.Runs.Any(run => run.Status == RunStatus.Running && run.TurnId != null));
        var run = Assert.Single(running.Runs);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Which files are in scope?", null, null));
        var answered = await WaitForItemByIdAsync(
            client,
            itemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded) && x.Item.State == ItemState.AwaitingReview);

        Assert.Equal(run.TurnId, Assert.Single(fakeAppServer.SteerRequests).ExpectedTurnId);
        Assert.Equal(run.RunId, Assert.Single(answered.DiscussionTurns).RunId);
    }

    [Fact]
    public async Task QuestionSteeredIntoReview_IsAnsweredAfterTheRunEndsWhileBackgroundWorkRuns()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.ReplyOnResumeAfterBackgroundWork);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitReviewDraftOnSteer);
        await using var app = CreateApp(fakeAppServer);
        var client = app.CreateClient();
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var itemId = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest).ItemId!;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "First pass.", null, null));
        await WaitForItemByIdAsync(client, itemId, x => x.Runs.Any(run => run.Status == RunStatus.Running && run.TurnId != null));

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Can a sub-agent check the migration?", null, null));
        var answered = await WaitForItemByIdAsync(
            client,
            itemId,
            x => x.DiscussionTurns.Any(turn => turn.Status is DiscussionTurnStatus.Succeeded or DiscussionTurnStatus.Failed));

        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        Assert.Null(turn.ErrorCode);
        Assert.Equal(RunStatus.Succeeded, Assert.Single(answered.Runs).Status);
        Assert.Contains(fakeAppServer.ThreadResumeRequests, request => request.ThreadId == turn.ThreadId);
    }

    [Fact]
    public async Task QuestionOnArchivedItem_IsRejected()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = CreateApp(fakeAppServer);
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, "Archived discussion", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{task.Item.ItemId}/archive", new { });

        var response = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Still there?", null, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("discussionTurnNotAllowed", error?.Error.Code);
    }

    private static TestOratorioApp CreateApp(FakeAppServerClientFactory fakeAppServer, FakeGitHubApiClient? fakeGitHub = null) =>
        new(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub ?? new FakeGitHubApiClient());
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });

    private static async Task<string> ReviewPullRequestAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        return pr.ItemId!;
    }
}
