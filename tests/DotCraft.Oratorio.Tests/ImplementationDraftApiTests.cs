using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ImplementationDraftApiTests
{
    [Fact]
    public async Task AppServerImplementationDraft_AutoPrCreatesGeneratedPullRequestItem()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the synced issue.", null, null, "implementation", DeliveryPolicy.AutoPr));

        var reviewed = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);

        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitImplementationDraft"));
        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.Equal(RunPurpose.Implementation, Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer").Purpose);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal(DeliveryPolicy.AutoPr, draft.DeliveryPolicy);
        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Equal("https://github.example.test/example-owner/oratorio/pull/501", draft.PullRequestUrl);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        var create = Assert.Single(fakeGitHub.PullRequestCreates);
        Assert.Contains("Refs #42", create.Body);
        Assert.NotNull(draft.PullRequestItemId);

        var generated = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{draft.PullRequestItemId}", JsonOptions);
        Assert.NotNull(generated);
        Assert.Equal(ItemKind.PullRequest, generated!.Item.Kind);
        Assert.Equal(issue.ItemId, generated.Item.ParentItemId);
        Assert.Equal(draft.DraftId, generated.Item.GeneratedFromDraftId);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.LocalCommit && write.Status == SourceWriteStatus.Succeeded);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.BranchPush && write.Status == SourceWriteStatus.Succeeded);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task AppServerImplementationDraft_SourceWriteRetryReusesPushedGitHubBranch()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailNextWriteCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the synced issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var failed = await WaitForItemByIdAsync(
            client,
            issue.ItemId!,
            x => x.ImplementationDrafts.Any(draft => draft.Status == ImplementationDraftStatus.DeliveryFailed) &&
                x.SourceWrites.Any(write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Failed));
        var failedWrite = Assert.Single(failed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation);
        Assert.Equal("deliveryFailed", failedWrite.ErrorCode);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Empty(fakeGitHub.PullRequestCreates);

        fakeGit.EmptyDiff = true;
        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var draft = Assert.Single(retried.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Single(fakeGitHub.PullRequestCreates);
        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Null(retriedWrite.ErrorCode);
        Assert.DoesNotContain(retried.SourceWrites, write => write.ErrorCode == "invalidGitHubTarget");
    }

    [Fact]
    public async Task LocalTaskImplementationDraft_BlocksApprovalUntilDeliveredToTaskRepository()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(fakeGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: "example-owner/oratorio");

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Draft, draft.Status);
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/approve",
            new DecisionRequest("Accept handoff."),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);
        var approveError = await approveResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("implementationDraftUndelivered", approveError?.Error.Code);

        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{draft.DraftId}/deliver", new { });
        draft = Assert.Single(delivered.ImplementationDrafts);

        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("example-owner/oratorio", delivered.Item.Repository);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGit.Pushes).Repository.FullName);
        Assert.Equal("example-owner/oratorio", Assert.Single(fakeGitHub.PullRequestCreates).Repository.FullName);
        Assert.Contains(delivered.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestCreation && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task LocalTaskDispatch_MissingRepositoryWorkspaceFails()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:0"] = null
            });
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, repository: null);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this local task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        var failed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.Failed);

        Assert.Empty(failed.ImplementationDrafts);
        Assert.Contains("No repository workspace is configured", failed.Item.LatestSummary);
    }

    [Fact]
    public async Task AppServerFollowUpDraft_InheritsPullRequestRouteWhenBranchOmitted()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitFollowUpDraft)
        {
            FollowUpDraftBranch = null
        };
        var fakeWorktree = new FakeWorktreeManager();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review and capture follow-up work.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.FollowUpDrafts.Count == 1);
        var draft = Assert.Single(reviewed.FollowUpDrafts);
        Assert.Null(draft.Branch);

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal(ItemKind.LocalTask, localTask!.Item.Kind);
        Assert.Equal("example-owner/oratorio", localTask.Item.Repository);
        Assert.Equal("feature/auth-refresh", localTask.Item.Branch);
        Assert.Equal("abc123", localTask.Item.HeadSha);
        Assert.Equal(pr.ItemId, localTask.Item.ParentItemId);

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitImplementationDraft;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{localTask.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement this inherited follow-up task.", null, null, "implementation", DeliveryPolicy.ManualDelivery));

        await WaitForItemByIdAsync(client, localTask.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var localPrepareRequest = Assert.Single(fakeWorktree.PrepareRequests, x => x.ItemId == localTask.Item.ItemId);
        Assert.Equal("local", localPrepareRequest.Source);
        Assert.Equal("feature/auth-refresh", localPrepareRequest.SourceBranch);
        Assert.Equal("abc123", localPrepareRequest.HeadSha);
    }

    [Fact]
    public async Task FollowUpDraft_CreateLocalTask_DoesNotInheritBranchFromNonPullRequestParent()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var parent = await CreateLocalTaskAsync(client, "Parent local follow-up", repository: "example-owner/oratorio");
        string draftId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var item = await db.Items.FirstAsync(x => x.ItemId == parent.Item.ItemId);
            var now = DateTimeOffset.UtcNow;
            item.State = ItemState.AwaitingReview;
            item.CurrentRound = 1;
            item.HeadSha = "local-parent-head";
            var round = new OratorioRound
            {
                ItemId = item.ItemId,
                RoundNumber = 1,
                Status = RoundStatus.AwaitingReview,
                CreatedAt = now
            };
            var run = new OratorioRun
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                Attempt = 1,
                Status = RunStatus.Succeeded,
                RunnerKind = "appServer",
                StartedAt = now,
                CompletedAt = now,
                Purpose = RunPurpose.ReviewAnalysis
            };
            var draft = new OratorioFollowUpDraft
            {
                ItemId = item.ItemId,
                RoundId = round.RoundId,
                RunId = run.RunId,
                Status = FollowUpDraftStatus.Draft,
                Title = "Separate local cleanup",
                Body = "This should become a local task without inheriting the parent local branch.",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Rounds.Add(round);
            db.Runs.Add(run);
            db.FollowUpDrafts.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.DraftId;
        }

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal("example-owner/oratorio", localTask!.Item.Repository);
        Assert.Null(localTask.Item.Branch);
        Assert.Null(localTask.Item.HeadSha);
    }

    [Fact]
    public async Task AppServerFollowUpDraft_CanBeEditedAndCreatedAsLocalTask()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitFollowUpDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review and capture follow-up work.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.FollowUpDrafts.Count == 1);
        var draft = Assert.Single(reviewed.FollowUpDrafts);

        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitFollowUpDraft"));
        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.Equal(FollowUpDraftStatus.Draft, draft.Status);
        Assert.Equal("Split out migration cleanup", draft.Title);
        Assert.Equal("example-owner/oratorio", draft.Repository);
        Assert.Contains("backend", draft.Labels);

        var edited = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/follow-up-drafts/{draft.DraftId}",
            new FollowUpDraftUpdateRequest(
                "Edited follow-up",
                "Edited local task body.",
                "Still separate scope.",
                "example-owner/oratorio",
                "operator",
                "feature/follow-up",
                ["follow-up", "edited"]));
        var editedDraft = Assert.Single(edited.FollowUpDrafts);
        Assert.Equal("Edited follow-up", editedDraft.Title);
        Assert.Equal("operator", editedDraft.Assignee);

        var created = await PostAsync<ItemDetailResponse>(client, $"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { });
        var createdDraft = Assert.Single(created.FollowUpDrafts);
        Assert.Equal(FollowUpDraftStatus.Created, createdDraft.Status);
        Assert.NotNull(createdDraft.CreatedItemId);

        var localTask = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{createdDraft.CreatedItemId}", JsonOptions);
        Assert.NotNull(localTask);
        Assert.Equal(ItemKind.LocalTask, localTask!.Item.Kind);
        Assert.Equal("Edited follow-up", localTask.Item.Title);
        Assert.Equal("Edited local task body.", localTask.Item.Description);
        Assert.Equal("feature/follow-up", localTask.Item.Branch);
        Assert.Null(localTask.Item.HeadSha);
        Assert.Equal(pr.ItemId, localTask.Item.ParentItemId);
        Assert.Equal(draft.DraftId, localTask.Item.GeneratedFromDraftId);

        var createAgain = await client.PostAsJsonAsync($"/api/v1/follow-up-drafts/{draft.DraftId}/create-local-task", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, createAgain.StatusCode);
    }
}
