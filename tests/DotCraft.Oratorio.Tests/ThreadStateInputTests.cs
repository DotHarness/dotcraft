using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class ThreadStateInputTests
{
    [Fact]
    public async Task RejectedInputRetainsThreadState_AcceptedRetryAdvancesIt()
    {
        const string threadId = "thread-test-1";
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitDiscussionReply)
        {
            RejectTurnStarts = 1
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var item = new OratorioItem
        {
            Source = "github",
            ExternalId = "pull-request-1",
            Kind = ItemKind.PullRequest,
            Title = "Review changes",
            Repository = "example-owner/oratorio",
            State = ItemState.AwaitingReview,
            HeadSha = "def456",
            SourceState = SourceState.Open
        };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            db.Runs.Add(new OratorioRun
            {
                Item = item,
                Round = new OratorioRound { Item = item, RoundNumber = 1 },
                RunnerKind = "appServer",
                Status = RunStatus.Succeeded,
                ThreadId = threadId,
                AppServerEndpoint = "http://appserver.test",
                PromptContextJson = ConversationThreadContext.Create(
                    RunPurpose.ReviewAnalysis, Path.GetTempPath(), null,
                    [OratorioDynamicToolCatalog.SubmitDiscussionReplyId])
            });
            db.ThreadStates.Add(new OratorioThreadState
            {
                ItemId = item.ItemId,
                ThreadId = threadId,
                WorkMode = RunPurpose.ReviewAnalysis,
                HeadSha = "abc123",
                SourceState = SourceState.Open,
                IsDraft = false
            });
            await db.SaveChangesAsync();
        }

        var rejectedTurnId = await DeliverQuestionAsync();
        await AssertStateAsync(rejectedTurnId, DiscussionTurnStatus.Failed, "abc123");

        var acceptedTurnId = await DeliverQuestionAsync();
        await AssertStateAsync(acceptedTurnId, DiscussionTurnStatus.Succeeded, "def456");

        async Task<string> DeliverQuestionAsync()
        {
            using var scope = app.Services.CreateScope();
            var detail = await scope.ServiceProvider.GetRequiredService<DiscussionTurnService>()
                .CreateByItemIdAsync(item.ItemId, new DiscussionTurnRequest("Review the latest head.", null, null),
                    CancellationToken.None);
            var turnId = Assert.Single(detail.DiscussionTurns, turn => turn.Status == DiscussionTurnStatus.Pending)
                .DiscussionTurnId;
            await scope.ServiceProvider.GetRequiredService<DiscussionTurnDelivery>()
                .DeliverAsync(turnId, CancellationToken.None);
            return turnId;
        }

        async Task AssertStateAsync(string turnId, DiscussionTurnStatus expectedStatus, string expectedHead)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var turn = await db.DiscussionTurns.AsNoTracking().SingleAsync(x => x.DiscussionTurnId == turnId);
            Assert.Equal(expectedStatus, turn.Status);
            Assert.Equal(threadId, turn.ThreadId);
            var record = await db.ThreadStates.AsNoTracking().SingleAsync(x => x.ThreadId == threadId);
            Assert.Equal(expectedHead, record.HeadSha);
        }
    }
}
