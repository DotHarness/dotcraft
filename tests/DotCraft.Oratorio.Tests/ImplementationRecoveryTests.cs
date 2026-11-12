using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ImplementationRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_ContinuesRecordedWorkspaceAndThreadWithImplementationTools(bool automatic)
    {
        var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Disconnect);
        var worktrees = new FakeWorktreeManager();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-11-12T00:00:00Z"));
        await using var app = CreateApp(appServer, worktrees, clock, automatic ? 2 : 1);
        var client = app.CreateClient();
        var created = await CreateLocalTaskAsync(client);
        var itemId = created.Item.ItemId;
        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/dispatch", ImplementationRequest());

        var interrupted = await WaitForItemByIdAsync(client, itemId,
            x => x.Runs.Any(run => run.Status == RunStatus.Failed && run.ErrorCode == "appServerDisconnected"));
        var original = Assert.Single(interrupted.Runs, run => run.Status == RunStatus.Failed);
        appServer.Outcome = FakeAppServerOutcome.SubmitImplementationDraft;
        worktrees.BaseShaOverride = "unrelated-checkout-head";
        if (automatic)
            clock.Advance(TimeSpan.FromSeconds(2));
        else
            await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/dispatch", ImplementationRequest());

        var completed = await WaitForItemByIdAsync(client, itemId,
            x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var retried = Assert.Single(completed.Runs, run => run.Status == RunStatus.Succeeded);

        Assert.Single(completed.Rounds);
        Assert.Equal(original.WorktreePath, retried.WorktreePath);
        Assert.Equal(original.WorktreeBranch, retried.WorktreeBranch);
        Assert.Equal(original.BaseRef, retried.BaseRef);
        Assert.Equal(original.BaseSha, retried.BaseSha);
        Assert.Equal(original.ThreadId, retried.ThreadId);
        Assert.Equal(1, appServer.StartThreadCount);
        Assert.True(Assert.Single(appServer.ThreadResumeRequests).DynamicTools!.ContainsTool("oratorio_run", "SubmitImplementationDraft"));
        Assert.True(appServer.LastToolResult!.Success);
    }

    [Fact]
    public async Task BackendRestart_ContinuesImplementationAfterInterruptingRecordedTurn()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "oratorio-tests", $"{Guid.NewGuid():n}.db");
        var clock = new MutableClock(DateTimeOffset.Parse("2026-11-12T00:00:00Z"));
        var originalAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        string itemId;
        RunDto original;
        await using (var app = CreateApp(originalAppServer, new FakeWorktreeManager(), clock, 2, databasePath))
        {
            var client = app.CreateClient();
            itemId = (await CreateLocalTaskAsync(client)).Item.ItemId;
            await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/dispatch", ImplementationRequest());
            var running = await WaitForItemByIdAsync(client, itemId,
                x => x.Runs.Any(run => run.Status == RunStatus.Running && run.TurnId != null));
            original = Assert.Single(running.Runs);
        }

        var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var worktrees = new FakeWorktreeManager { BaseShaOverride = "unrelated-checkout-head" };
        await using var recovered = CreateApp(appServer, worktrees, clock, 2, databasePath);
        var recoveredClient = recovered.CreateClient();
        await WaitForItemByIdAsync(recoveredClient, itemId,
            x => x.Runs.Count == 2 && x.Runs.Any(run => run.ErrorCode == "appServerRunnerInterrupted"));
        clock.Advance(TimeSpan.FromSeconds(2));
        var completed = await WaitForItemByIdAsync(recoveredClient, itemId,
            x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var retried = Assert.Single(completed.Runs, run => run.Status == RunStatus.Succeeded);

        Assert.Contains(appServer.InterruptedTurns, turn => turn.ThreadId == original.ThreadId && turn.TurnId == original.TurnId);
        Assert.Equal(original.ThreadId, retried.ThreadId);
        Assert.Equal(original.WorktreePath, retried.WorktreePath);
        Assert.Equal(original.BaseSha, retried.BaseSha);
        Assert.NotNull(Assert.Single(worktrees.PrepareRequests).ResumeFrom);
        Assert.Empty(appServer.ThreadStartRequests);
    }

    [Fact]
    public async Task DiscussionAfterInterruptedImplementation_KeepsWorkspaceAndThread()
    {
        var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Disconnect);
        var worktrees = new FakeWorktreeManager();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-11-12T00:00:00Z"));
        await using var app = CreateApp(appServer, worktrees, clock);
        var client = app.CreateClient();
        var created = await CreateLocalTaskAsync(client);
        var itemId = created.Item.ItemId;
        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/dispatch", ImplementationRequest());
        var interrupted = await WaitForItemByIdAsync(client, itemId, x => x.Item.State == ItemState.Failed);
        var original = Assert.Single(interrupted.Runs);
        worktrees.Failure = new WorktreeException("worktreeDirty", "Existing implementation changes.");
        appServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;

        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("What progress is already in the workspace?", null, null));
        var answered = await WaitForItemByIdAsync(client, itemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded));

        Assert.Equal(original.ThreadId, Assert.Single(answered.DiscussionTurns).ThreadId);
        Assert.Single(worktrees.PrepareRequests);
        Assert.Equal(1, appServer.StartThreadCount);
        Assert.Single(answered.Runs);
        Assert.Empty(answered.ImplementationDrafts);
        Assert.Equal(ItemState.Failed, answered.Item.State);
    }

    private static DispatchRequest ImplementationRequest() =>
        new("appServer", "Continue implementation.", null, null, "implementation", DeliveryPolicy.ManualDelivery);

    private static TestOratorioApp CreateApp(
        FakeAppServerClientFactory appServer,
        FakeWorktreeManager worktrees,
        MutableClock clock,
        int maxAttempts = 1,
        string? databasePath = null) =>
        new(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(appServer);
            services.AddSingleton<IWorktreeManager>(worktrees);
            services.AddSingleton<IClock>(clock);
        }, new Dictionary<string, string?>
        {
            ["Oratorio:DotCraft:MaxRunAttempts"] = maxAttempts.ToString()
        }, databasePath);
}
