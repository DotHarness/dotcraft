using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerPendingInteractiveReplayTests
{
    [Fact]
    public async Task ThreadSubscribe_WhenThreadWaitingInput_ReplaysRequestAndResolvesResponse()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(requestUserInputSupport: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        AddWaitingUserInputTurn(thread, "turn_001", "item_input_001", "req_input_001");

        harness.Transport.ApprovalHandler = (method, @params) =>
        {
            Assert.Equal(AppServerMethods.ItemRequestUserInput, method);
            var request = Assert.IsType<AppServerRequestUserInputParams>(@params);
            Assert.Equal(thread.Id, request.ThreadId);
            Assert.Equal("turn_001", request.TurnId);
            Assert.Equal("item_input_001", request.ItemId);
            Assert.Equal("req_input_001", request.RequestId);

            return InMemoryTransport.BuildClientResponse(1, new
            {
                answers = new Dictionary<string, object>
                {
                    ["choice"] = new { answers = new[] { "B" } }
                }
            });
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var replay = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(AppServerMethods.ItemRequestUserInput, replay.RootElement.GetProperty("method").GetString());

        await WaitForAsync(() => harness.Service.ResolvedUserInputs.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedUserInputs);
        Assert.Equal("req_input_001", resolved.requestId);
        Assert.Equal("B", Assert.Single(resolved.response.Answers["choice"].Answers));
    }

    [Fact]
    public async Task ThreadResume_WhenThreadWaitingApproval_ReplaysRequestAndResolvesResponse()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        AddWaitingApprovalTurn(thread, "turn_001", "item_approval_001", "req_approval_001");

        harness.Transport.ApprovalHandler = (method, @params) =>
        {
            Assert.Equal(AppServerMethods.ItemApprovalRequest, method);
            var request = Assert.IsType<AppServerApprovalRequestParams>(@params);
            Assert.Equal(thread.Id, request.ThreadId);
            Assert.Equal("turn_001", request.TurnId);
            Assert.Equal("item_approval_001", request.ItemId);
            Assert.Equal("req_approval_001", request.RequestId);

            return InMemoryTransport.BuildClientResponse(1, new { decision = "acceptForSession" });
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadResume,
            new { threadId = thread.Id }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var resumed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(resumed, AppServerMethods.ThreadResumed);
        using var replay = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(AppServerMethods.ItemApprovalRequest, replay.RootElement.GetProperty("method").GetString());

        await WaitForAsync(() => harness.Service.ResolvedApprovals.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal("req_approval_001", resolved.requestId);
        Assert.Equal(SessionApprovalDecision.AcceptForSession, resolved.decision);
    }

    [Fact]
    public async Task ThreadResume_WhenTurnHasMultipleWaitingApprovals_ReplaysAllAndResolvesResponses()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var turn = CreateWaitingTurn(thread, "turn_001", TurnStatus.WaitingApproval);
        AddWaitingApprovalRequest(thread, turn, "item_approval_001", "req_approval_001");
        AddWaitingApprovalRequest(thread, turn, "item_approval_002", "req_approval_002");
        AddWaitingApprovalRequest(thread, turn, "item_approval_003", "req_approval_003");

        var handledRequestIds = new List<string>();
        harness.Transport.ApprovalHandler = (method, @params) =>
        {
            Assert.Equal(AppServerMethods.ItemApprovalRequest, method);
            var request = Assert.IsType<AppServerApprovalRequestParams>(@params);
            handledRequestIds.Add(request.RequestId);
            return InMemoryTransport.BuildClientResponse(1, new { decision = "accept" });
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadResume,
            new { threadId = thread.Id }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var resumed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(resumed, AppServerMethods.ThreadResumed);

        var replayedRequestIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var replay = await harness.Transport.ReadNextSentAsync();
            Assert.Equal(AppServerMethods.ItemApprovalRequest, replay.RootElement.GetProperty("method").GetString());
            replayedRequestIds.Add(
                replay.RootElement.GetProperty("params").GetProperty("requestId").GetString()!);
        }

        Assert.Equal(
            ["req_approval_001", "req_approval_002", "req_approval_003"],
            replayedRequestIds);
        await WaitForAsync(() => harness.Service.ResolvedApprovals.Count == 3);
        Assert.Equal(
            ["req_approval_001", "req_approval_002", "req_approval_003"],
            handledRequestIds);
        Assert.Equal(
            ["req_approval_001", "req_approval_002", "req_approval_003"],
            harness.Service.ResolvedApprovals.Select(resolved => resolved.requestId).ToArray());
    }

    [Fact]
    public async Task ThreadResume_WhenMultipleApprovalsArePending_DoesNotReplayNextBeforeCurrentResponse()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var turn = CreateWaitingTurn(thread, "turn_001", TurnStatus.WaitingApproval);
        AddWaitingApprovalRequest(thread, turn, "item_approval_001", "req_approval_001");
        AddWaitingApprovalRequest(thread, turn, "item_approval_002", "req_approval_002");
        AddWaitingApprovalRequest(thread, turn, "item_approval_003", "req_approval_003");

        var observedRequestIds = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var responses = new Dictionary<string, TaskCompletionSource<AppServerIncomingMessage>>
        {
            ["req_approval_001"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["req_approval_002"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["req_approval_003"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        harness.Transport.ApprovalHandlerAsync = (method, @params) =>
        {
            Assert.Equal(AppServerMethods.ItemApprovalRequest, method);
            var request = Assert.IsType<AppServerApprovalRequestParams>(@params);
            observedRequestIds.Enqueue(request.RequestId);
            return responses[request.RequestId].Task;
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadResume,
            new { threadId = thread.Id }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var resumed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsNotification(resumed, AppServerMethods.ThreadResumed);

        using var firstReplay = await harness.Transport.ReadNextSentAsync();
        AssertApprovalReplay(firstReplay, "req_approval_001");
        await WaitForAsync(() => observedRequestIds.Count == 1);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));
        using var subscribeResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(subscribeResponse);

        await Task.Delay(100);
        Assert.Null(harness.Transport.TryReadSent());
        Assert.Equal(["req_approval_001"], observedRequestIds.ToArray());

        responses["req_approval_001"].SetResult(
            InMemoryTransport.BuildClientResponse(1, new { decision = "accept" }));
        using var secondReplay = await harness.Transport.ReadNextSentAsync();
        AssertApprovalReplay(secondReplay, "req_approval_002");
        await WaitForAsync(() => observedRequestIds.Count == 2);

        await Task.Delay(100);
        Assert.Null(harness.Transport.TryReadSent());
        Assert.Equal(["req_approval_001", "req_approval_002"], observedRequestIds.ToArray());

        responses["req_approval_002"].SetResult(
            InMemoryTransport.BuildClientResponse(2, new { decision = "accept" }));
        using var thirdReplay = await harness.Transport.ReadNextSentAsync();
        AssertApprovalReplay(thirdReplay, "req_approval_003");
        await WaitForAsync(() => observedRequestIds.Count == 3);

        responses["req_approval_003"].SetResult(
            InMemoryTransport.BuildClientResponse(3, new { decision = "accept" }));

        await WaitForAsync(() => harness.Service.ResolvedApprovals.Count == 3);
        Assert.Equal(
            ["req_approval_001", "req_approval_002", "req_approval_003"],
            harness.Service.ResolvedApprovals.Select(resolved => resolved.requestId).ToArray());
    }

    [Fact]
    public async Task ThreadSubscribe_RepeatedForSameWaitingInput_DoesNotReplayDuplicateRequest()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(requestUserInputSupport: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        AddWaitingUserInputTurn(thread, "turn_001", "item_input_001", "req_input_001");
        var requestCount = 0;

        harness.Transport.ApprovalHandler = (method, @params) =>
        {
            Assert.Equal(AppServerMethods.ItemRequestUserInput, method);
            requestCount++;
            return InMemoryTransport.BuildClientResponse(1, new
            {
                answers = new Dictionary<string, object>()
            });
        };

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));
        using var firstResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(firstResponse);
        using var firstReplay = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(AppServerMethods.ItemRequestUserInput, firstReplay.RootElement.GetProperty("method").GetString());
        await WaitForAsync(() => harness.Service.ResolvedUserInputs.Count == 1);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));
        using var secondResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(secondResponse);

        await Task.Delay(100);
        Assert.Null(harness.Transport.TryReadSent());
        Assert.Equal(1, requestCount);
    }

    private static void AddWaitingUserInputTurn(
        SessionThread thread,
        string turnId,
        string itemId,
        string requestId)
    {
        var turn = CreateWaitingTurn(thread, turnId, TurnStatus.WaitingInput);
        turn.Items.Add(new SessionItem
        {
            Id = itemId,
            TurnId = turnId,
            Type = ItemType.UserInputRequest,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserInputRequestPayload
            {
                RequestId = requestId,
                Questions =
                [
                    new RequestUserInputQuestion
                    {
                        Id = "choice",
                        Header = "Choice",
                        Question = "Pick one?",
                        Options =
                        [
                            new RequestUserInputQuestionOption
                            {
                                Label = "A",
                                Description = "Pick A."
                            },
                            new RequestUserInputQuestionOption
                            {
                                Label = "B",
                                Description = "Pick B."
                            }
                        ]
                    }
                ]
            }
        });
    }

    private static void AddWaitingApprovalTurn(
        SessionThread thread,
        string turnId,
        string itemId,
        string requestId)
    {
        var turn = CreateWaitingTurn(thread, turnId, TurnStatus.WaitingApproval);
        AddWaitingApprovalRequest(thread, turn, itemId, requestId);
    }

    private static void AddWaitingApprovalRequest(
        SessionThread thread,
        SessionTurn turn,
        string itemId,
        string requestId)
    {
        turn.Items.Add(new SessionItem
        {
            Id = itemId,
            TurnId = turn.Id,
            Type = ItemType.ApprovalRequest,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ApprovalRequestPayload
            {
                RequestId = requestId,
                ApprovalType = "shell",
                Operation = "npm test",
                Target = thread.WorkspacePath,
                ScopeKey = "shell:npm test",
                Reason = "Run tests."
            }
        });
    }

    private static SessionTurn CreateWaitingTurn(
        SessionThread thread,
        string turnId,
        TurnStatus status)
    {
        var turn = new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = thread.OriginChannel
        };
        thread.Turns.Add(turn);
        return turn;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, cts.Token);
    }

    private static void AssertApprovalReplay(JsonDocument replay, string requestId)
    {
        Assert.Equal(AppServerMethods.ItemApprovalRequest, replay.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            requestId,
            replay.RootElement.GetProperty("params").GetProperty("requestId").GetString());
    }
}
