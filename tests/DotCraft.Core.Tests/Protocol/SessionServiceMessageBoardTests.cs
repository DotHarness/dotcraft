using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.GeneratedTools.Core;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMessageBoardTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData("agent", "default")]
    [InlineData("plan", "explorer")]
    public async Task MessageBoard_CollaborationWritesAreAllowedForNativeRolesAndPlan(string mode, string role)
    {
        using var client = new CoordinationToolCallChatClient("BoardPost",
            new Dictionary<string, object?> { ["text"] = "research result", ["newChannelName"] = "findings" });
        await using var factory = CreateAgentFactory(client, [new BoardTestToolSource()]);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        var root = await service.CreateThreadAsync(MakeIdentity());
        var child = await BoardChild(service, root, "researcher", role);
        await service.SetThreadModeAsync(child.Id, mode);
        await service.RefreshThreadAgentAsync(child.Id);
        await DrainAsync(service.SubmitInputAsync(child.Id, [new TextContent("publish finding")]));
        var page = await BoardCall(service, root, new("searchPosts"));
        Assert.Single(page.GetProperty("results").EnumerateArray());
        Assert.Equal("/root/researcher", page.GetProperty("results")[0].GetProperty("author").GetString());
    }

    [Fact]
    public async Task MessageBoard_UserToolDenyStillPreventsWrites()
    {
        using var client = new CapturingToolFailureChatClient("BoardPost");
        await using var factory = CreateAgentFactory(client, [new BoardTestToolSource()]);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        var root = await service.CreateThreadAsync(MakeIdentity(), config: new ThreadConfiguration { ToolDenyList = ["BoardPost"] });
        await service.RefreshThreadAgentAsync(root.Id);
        await DrainAsync(service.SubmitInputAsync(root.Id, [new TextContent("attempt publish")]));
        Assert.Equal(TurnStatus.Completed, Assert.Single(root.Turns).Status);
        Assert.NotNull(client.ToolResult);
        Assert.Equal("tool_not_found", StreamingFunctionInvokingChatClient.GetToolResultErrorCode(client.ToolResult));
        Assert.Empty((await BoardCall(service, root, new("getChannels"))).GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task MessageBoard_PostRetryRemainsIdempotentAfterRecipientCloses()
    {
        using var client = new RecordingChatClient("done");
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var root = await service.CreateThreadAsync(MakeIdentity());
        var worker = await BoardChild(service, root, "worker");
        var request = new BoardRequest("post", NewChannelName: "work", Text: "once", AgentsToNotify: ["worker"]);
        var first = await BoardCall(service, root, request, "same-invocation");
        await service.SetThreadSpawnEdgeStatusAsync(root.Id, worker.Id, ThreadSpawnEdgeStatus.Closed);
        var retry = await BoardCall(service, root, request, "same-invocation");
        Assert.Equal(first.GetProperty("messageId").GetString(), retry.GetProperty("messageId").GetString());
        Assert.Single((await BoardCall(service, root, new("searchPosts"))).GetProperty("results").EnumerateArray());
        var changed = await BoardCall(service, root, request with { Text = "changed" }, "same-invocation", allowError: true);
        Assert.Equal("board_invalid_argument", changed.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MessageBoard_ModelToolUsesRuntimeIdentityAndGenericToolEvents()
    {
        using var client = new CoordinationToolCallChatClient("BoardPost",
            new Dictionary<string, object?> { ["text"] = "shared finding", ["newChannelName"] = "research" });
        var recorder = new ToolInvocationRecorderRouter();
        await using var factory = CreateAgentFactory(client, [new BoardTestToolSource()], toolDispatcher: new ToolDispatcher(recorder: recorder));
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var root = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(root.Id);
        var events = await CollectAsync(service.SubmitInputAsync(root.Id, [new TextContent("share a finding")]));
        var turn = Assert.Single((await service.GetThreadAsync(root.Id)).Turns);
        Assert.True(turn.Status == TurnStatus.Completed, turn.Error);
        var call = Assert.IsType<ToolCallPayload>(Assert.Single(turn.Items, i => i.Type == ItemType.ToolCall).Payload);
        Assert.Equal("core.board", call.Presentation?.PresentationId);
        Assert.Equal("post", call.Presentation?.Options?["operation"]?.GetValue<string>());
        var result = Assert.IsType<ToolResultPayload>(Assert.Single(turn.Items, i => i.Type == ItemType.ToolResult).Payload);
        Assert.True(result.Success);
        Assert.Contains(events, e => e.EventType == SessionEventType.ItemCompleted && e.ItemPayload?.Type == ItemType.ToolResult);
        var page = await BoardCall(service, root, new("searchPosts"));
        Assert.Equal("/root", page.GetProperty("results")[0].GetProperty("author").GetString());
        Assert.Equal("shared finding", page.GetProperty("results")[0].GetProperty("textPreview").GetString());
    }

    [Fact]
    public async Task MessageBoard_TwoChildrenSharePersistedTopicAfterHostRestart_ForkIsEmpty()
    {
        using var client = new RecordingChatClient("done");
        await using var factory = CreateAgentFactory(client, [new BoardTestToolSource()]);
        var service = CreateService(factory, client);
        var root = await service.CreateThreadAsync(MakeIdentity());
        var worker = await BoardChild(service, root, "worker");
        var reviewer = await BoardChild(service, root, "reviewer");
        var initial = await BoardCall(service, root, new("post", NewChannelName: "design", Text: "Root proposal"));
        var topic = initial.GetProperty("messageId").GetString()!;
        await BoardCall(service, worker, new("post", ThreadId: topic, Text: "Worker evidence"));
        await BoardCall(service, reviewer, new("post", ThreadId: topic, Text: "Review conclusion"));
        await service.SetThreadSpawnEdgeStatusAsync(root.Id, worker.Id, ThreadSpawnEdgeStatus.Closed);
        await service.ArchiveThreadAsync(root.Id);

        using var resumedClient = new RecordingChatClient("resumed");
        await using var resumedFactory = CreateAgentFactory(resumedClient, [new BoardTestToolSource()]);
        var resumed = CreateService(resumedFactory, resumedClient);
        var loaded = await resumed.GetThreadAsync(root.Id);
        var read = await BoardCall(resumed, loaded, new("readThread", ThreadId: topic));
        Assert.Equal(2, read.GetProperty("nReturned").GetInt32());
        Assert.Contains(read.GetProperty("results").EnumerateArray(), post => post.GetProperty("textPreview").GetString() == "Worker evidence");
        var fork = await resumed.ForkThreadAsync(root.Id);
        Assert.Empty((await BoardCall(resumed, fork, new("getChannels"))).GetProperty("results").EnumerateArray());
        await resumed.DeleteThreadPermanentlyAsync(root.Id);
        using var db = new DotCraft.Persistence.WorkspaceStateDatabase(_tempDir);
        Assert.Equal("board_deleted", Assert.Throws<BoardException>(() => new AgentMessageBoardStore(db).Read(
            new(root.Id, root.Id, "/root"), new("readPost", MessageId: topic))).Code);
    }

    [Fact]
    public async Task MessageBoard_ColdResumeRecoversConsumedNoticeWithoutFutureDelivery()
    {
        using var client = new RecordingChatClient("done");
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var root = await service.CreateThreadAsync(MakeIdentity());
        root.Turns.Add(new SessionTurn { Id = "interrupted", Status = TurnStatus.Running });
        await using var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        await persistence.SaveThreadAsync(root);
        await persistence.AppendModelHistoryAsync(root.Id, [new ChatMessage(ChatRole.User, "consumed board preview")
        {
            AdditionalProperties = new()
            {
                ["dotcraft.history.inputs"] = JsonSerializer.SerializeToElement(new[]
                {
                    new { InputId = "mailbox:board:post-id", ItemId = "notice-item", TurnId = "interrupted" }
                })
            }
        }], "interrupted");

        await using var coldFactory = CreateAgentFactory(client);
        var cold = CreateService(coldFactory, client);
        var restored = await cold.ResumeThreadAsync(root.Id);
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(restored.Turns).Status);
        var payload = Assert.Single(restored.Turns[0].Items, item => item.Id == "notice-item").AsUserMessage!;
        Assert.Equal("subagentMailbox", payload.DeliveryMode);
        Assert.Equal("consumed board preview", Assert.Single(payload.MaterializedInputParts!).Text);
        Assert.Empty(await cold.ListPendingSubAgentMailboxAsync(root.Id, "/root"));
        await cold.ResumeThreadAsync(root.Id);
        Assert.Single(restored.Turns[0].Items, item => item.Id == "notice-item");
        Assert.Single(await persistence.LoadModelHistoryAsync(root.Id));
    }

    [Fact]
    public async Task MessageBoard_StorageFailureIsRecoverableAndPreservesExistingPosts()
    {
        using var client = new CoordinationToolCallChatClient("BoardPost",
            new Dictionary<string, object?> { ["text"] = "rejected", ["channelName"] = "work" });
        await using var factory = CreateAgentFactory(client, [new BoardTestToolSource()]);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        var root = await service.CreateThreadAsync(MakeIdentity());
        await BoardCall(service, root, new("post", NewChannelName: "work", Text: "preserved"));
        using var database = new DotCraft.Persistence.WorkspaceStateDatabase(_tempDir);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_board_post BEFORE INSERT ON agent_board_posts BEGIN SELECT RAISE(ABORT,'fixture_write_rejected'); END";
        command.ExecuteNonQuery();
        await service.RefreshThreadAgentAsync(root.Id);
        await DrainAsync(service.SubmitInputAsync(root.Id, [new TextContent("publish")]));
        Assert.Equal(TurnStatus.Completed, Assert.Single(root.Turns).Status);
        Assert.Contains(client.FollowUpMessages.SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => result.Result?.ToString()?.Contains("board_storage_error") == true);
        var posts = (await BoardCall(service, root, new("searchPosts"))).GetProperty("results");
        Assert.Equal("preserved", Assert.Single(posts.EnumerateArray()).GetProperty("textPreview").GetString());
        command.CommandText = "DROP TRIGGER reject_board_post";
        command.ExecuteNonQuery();
        await BoardCall(service, root, new("post", ChannelName: "work", Text: "recovered"));
    }

    [Fact]
    public async Task MessageBoard_ReadBudgetPreservesJsonAndUnicodeContinuation()
    {
        using var client = new RecordingChatClient("done");
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var root = await service.CreateThreadAsync(MakeIdentity());
        var body = string.Concat(Enumerable.Repeat("😀界\"", 6000));
        var post = await BoardCall(service, root, new("post", NewChannelName: "large", Text: body));
        var id = post.GetProperty("messageId").GetString();
        var offset = 0;
        var reconstructed = new StringBuilder();
        do
        {
            var page = await BoardCall(service, root, new("readPost", MessageId: id, OffsetChars: offset));
            Assert.True(Encoding.UTF8.GetByteCount(page.GetRawText()) <= 8000);
            reconstructed.Append(page.GetProperty("text").GetString());
            var next = page.GetProperty("nextOffsetChars").GetInt32();
            Assert.True(next > offset);
            offset = next;
        } while (offset < BoardText.Count(body));
        Assert.Equal(body, reconstructed.ToString());
    }

    [Fact]
    public async Task MessageBoard_RejectsTemporaryThreadsAndForeignPostIds()
    {
        using var client = new RecordingChatClient("done");
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var root = await service.CreateThreadAsync(MakeIdentity());
        var other = await service.CreateThreadAsync(MakeIdentity());
        var post = await BoardCall(service, root, new("post", NewChannelName: "private", Text: "one team"));
        var foreign = await BoardCall(service, other, new("readPost", MessageId: post.GetProperty("messageId").GetString()), allowError: true);
        Assert.Equal("board_not_found", foreign.GetProperty("error").GetProperty("code").GetString());
        var temporary = await service.CreateThreadAsync(MakeIdentity(), ephemeral: true);
        var unavailable = await BoardCall(service, temporary, new("getChannels"), allowError: true);
        Assert.Equal("board_unavailable", unavailable.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<SessionThread> BoardChild(SessionService service, SessionThread root, string name, string role = "default")
    {
        var path = "/root/" + name;
        var child = await service.CreateThreadAsync(MakeIdentity(), source: ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            ParentThreadId = root.Id, RootThreadId = root.Id, AgentPath = path, TaskName = name,
            RuntimeType = "native", Depth = 1, AgentRole = role
        }));
        await service.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = root.Id, ChildThreadId = child.Id, AgentPath = path,
            TaskName = name, RuntimeType = "native", Status = ThreadSpawnEdgeStatus.Open, Depth = 1, AgentRole = role
        });
        return child;
    }

    private static async Task<JsonElement> BoardCall(SessionService service, SessionThread thread, BoardRequest request,
        string? callId = null, bool allowError = false)
    {
        var json = await service.ExecuteBoardAsync(new SubAgentSessionContext
        {
            SessionService = service, ParentThread = thread, ParentTurnId = "test-turn",
            RootThreadId = thread.Source.SubAgent?.RootThreadId ?? thread.Id
        }, request, callId ?? Guid.NewGuid().ToString(), CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        if (!allowError) Assert.False(document.RootElement.TryGetProperty("error", out _), json);
        return document.RootElement.Clone();
    }

    private sealed class BoardTestToolSource : AIFunctionToolSource
    {
        public override string SourceId => "core-native";
        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            var tools = new AgentMessageBoardTools();
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardPost(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardCreateChannel(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardGetChannels(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardReadThread(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardReadPost(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardSearchPosts(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardSubscribe(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardUnsubscribe(tools);
            yield return GeneratedToolFunctions.AgentMessageBoardTools_BoardListThreads(tools);
        }
    }
}
