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
        turn.Items.Add(new SessionItem
        {
            Id = itemId,
            TurnId = turnId,
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
}
