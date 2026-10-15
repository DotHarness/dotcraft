using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceForkTests
{
    private static readonly ThreadForkOptions SideChatOptions = new()
    {
        Ephemeral = true,
        Purpose = ThreadForkPurpose.SideChat
    };

    [Fact]
    public async Task ForkThreadAsync_SideChatReusesSourcePrefixWhileSourceTurnRuns()
    {
        var chatClient = new RecordingChatClient("Fixed the timeout");
        await using var agentFactory = CreateAgentFactory(chatClient, [new EchoToolSource()]);
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        await DrainAsync(service.SubmitInputAsync(source.Id, [new TextContent("Fix the timeout")]));
        var parentMessages = chatClient.LastMessages.Select(MessageText).ToArray();
        var parentTools = chatClient.LastOptions?.Tools?.Select(tool => tool.Name).ToArray();
        var parentCacheKey = chatClient.LastPromptCacheKey;
        AddRunningTurn(await service.GetThreadAsync(source.Id), "turn_running", "Now add a regression test");

        var sideChat = await service.ForkThreadAsync(source.Id, SideChatOptions);
        chatClient.ResponseText = "Retry is called from Send.";
        await DrainAsync(service.SubmitInputAsync(sideChat.Id, [new TextContent("Where is Retry called?")]));
        var firstRequest = chatClient.LastMessages.ToArray();
        chatClient.ResponseText = "Twice.";
        await DrainAsync(service.SubmitInputAsync(sideChat.Id, [new TextContent("How often?")]));

        Assert.True(ThreadVisibility.IsHidden(sideChat));
        Assert.Equal(TurnStatus.Running, (await service.GetThreadAsync(source.Id)).Turns[^1].Status);
        Assert.Equal(parentMessages, firstRequest.Take(parentMessages.Length).Select(MessageText));
        Assert.Equal(parentTools, chatClient.LastOptions?.Tools?.Select(tool => tool.Name));
        Assert.Equal(parentCacheKey, chatClient.LastPromptCacheKey);
        var runningInput = Array.FindIndex(firstRequest, message => MessageText(message).Contains("Now add a regression test"));
        var boundary = Array.FindIndex(firstRequest, message => ThreadContextItems.IsKind(message, SideChatBoundary.Kind));
        var question = Array.FindIndex(firstRequest, message => MessageText(message).Contains("Where is Retry called?"));
        Assert.InRange(runningInput, parentMessages.Length, boundary - 1);
        Assert.InRange(boundary, runningInput + 1, question - 1);
        Assert.Equal(firstRequest.Select(MessageText), chatClient.LastMessages.Take(firstRequest.Length).Select(MessageText));
        Assert.Single(chatClient.LastMessages, message => ThreadContextItems.IsKind(message, SideChatBoundary.Kind));
    }

    [Fact]
    public async Task ForkThreadAsync_SideChatRejectsPartialCopiesAndNesting()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "first", "answer one");
        var sideChat = await service.ForkThreadAsync(source.Id, SideChatOptions);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ForkThreadAsync(source.Id, SideChatOptions with { Ephemeral = false }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ForkThreadAsync(source.Id, SideChatOptions with
        {
            ForkPoint = new ThreadForkPoint { TurnId = "turn_001" }
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ForkThreadAsync(sideChat.Id, SideChatOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceArchiveOrDeletion_DeletesItsSideChats(bool archive)
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "first", "answer one");
        await _store.SaveThreadAsync(source);
        var sideChat = await service.ForkThreadAsync(source.Id, SideChatOptions);
        var branch = await service.ForkThreadAsync(source.Id, new ThreadForkOptions { Ephemeral = true });
        var deleted = new List<string>();
        service.ThreadDeletedForBroadcast = deleted.Add;

        if (archive)
            await service.ArchiveThreadAsync(source.Id);
        else
            await service.DeleteThreadPermanentlyAsync(source.Id);

        Assert.Contains(sideChat.Id, deleted);
        Assert.DoesNotContain(branch.Id, deleted);
    }

    [Fact]
    public async Task SpawnAgent_FailsInSideChat()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        AddCompletedTurn(source, "turn_001", "first", "answer one");
        var sideChat = await service.ForkThreadAsync(source.Id, SideChatOptions);
        var created = 0;
        service.ThreadCreatedForBroadcast += _ => created++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SubAgentSessionControl.SpawnAgentAsync(
            new SubAgentSessionContext
            {
                SessionService = service,
                ParentThread = sideChat,
                ParentTurnId = "turn_001",
                RootThreadId = sideChat.Id
            },
            new SubAgentSpawnOptions { AgentPrompt = "inspect the code", TaskName = "inspect" },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None));
        Assert.Equal(0, created);
    }

    private static void AddRunningTurn(SessionThread thread, string turnId, string userText)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var user = new SessionItem
        {
            Id = $"{turnId}_user",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = userText }
        };
        thread.Turns.Add(new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            Input = user,
            Items = [user],
            StartedAt = now
        });
    }
}
