using DotCraft.Agents;
using System.Text.Json;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningInput_ExistingCommunicationIsDurableBeforeContinuation(bool withGuidance)
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                if (withGuidance)
                    await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("human guidance")], ct: ct);
                await service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
                {
                    Id = "passive-one", RootThreadId = thread.Id, SenderAgentPath = "/root/worker",
                    TargetAgentPath = "/root", Message = "existing communication", Status = SubAgentMailboxStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow
                }, ct);
                return withGuidance ? new TextContent("first answer") : HistoryBoundaryClient.Tool();
            }
            await using var reader = new ThreadStore(_tempDir);
            var history = await reader.LoadModelHistoryAsync(thread.Id, ct);
            foreach (var input in withGuidance ? new[] { "human guidance", "existing communication" } : ["existing communication"])
            {
                Assert.Single(history, message => message.Text.Contains(input, StringComparison.Ordinal));
                Assert.Single(messages, message => message.Text.Contains(input, StringComparison.Ordinal));
            }
            Assert.Empty(await service.ListPendingSubAgentMailboxAsync(thread.Id, "/root", ct));
            Assert.Empty(thread.QueuedInputs);
            AssertResponsesUserMessageIds(messages);
            AssertResponsesUserMessageIds(history);
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        Assert.Equal(TurnStatus.Completed, thread.Turns[0].Status);
        Assert.Equal(withGuidance ? 1 : 0, thread.Turns[0].Items.Count(item => item.AsUserMessage?.DeliveryMode == "guidance"));
        Assert.Single(thread.Turns[0].Items, item => item.AsUserMessage?.DeliveryMode == SubAgentMailboxDelivery.DeliveryMode);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        Assert.Equal(3, model.Calls);
    }

    [Fact]
    public async Task RunningInput_ColdLoadingReconcilesExistingCommunicationWithoutSampling()
    {
        using var model = new HistoryBoundaryClient((_, _, _) => Task.FromResult<AIContent>(new TextContent("unused")));
        await using var factory = CreateAgentFactory(model);
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        thread.Turns.Add(new SessionTurn { Id = "interrupted", Status = TurnStatus.Running });
        await using var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        await persistence.SaveThreadAsync(thread);
        await service.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = "passive-one", RootThreadId = thread.Id, SenderAgentPath = "/root/worker", TargetAgentPath = "/root",
            Message = "existing communication", Status = SubAgentMailboxStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
        });
        await persistence.AppendModelHistoryAsync(thread.Id,
            [new ChatMessage(ChatRole.User, "existing communication")
            {
                AdditionalProperties = new()
                {
                    ["dotcraft.history.inputs"] = JsonSerializer.SerializeToElement(new[]
                    {
                        new { InputId = "mailbox:passive-one", ItemId = "admitted-item", TurnId = "interrupted" }
                    })
                }
            }], "interrupted");
        await using var coldFactory = CreateAgentFactory(model);
        var cold = CreateService(coldFactory, model, useStreamingFunctionInvoker: true);
        var restored = await cold.ResumeThreadAsync(thread.Id);
        Assert.Empty(await cold.ListPendingSubAgentMailboxAsync(thread.Id, "/root"));
        Assert.Single(restored.Turns[0].Items, item => item.Id == "admitted-item");
        await cold.ResumeThreadAsync(thread.Id);
        Assert.Single(await persistence.LoadModelHistoryAsync(thread.Id));
        Assert.Equal(0, model.Calls);
    }
}
