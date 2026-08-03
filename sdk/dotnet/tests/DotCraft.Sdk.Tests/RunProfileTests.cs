using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class RunProfileTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_MergesTypedAgentPayloadWithoutDuplicatingDeltas()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var runTask = thread.RunAsync("hello");
        await RespondAsync(transport, "thread/subscribe", new RpcEmpty());
        await RespondAsync(transport, "turn/start", new TurnStartResult { Turn = Turn("inProgress") });
        await PushNotificationAsync(transport, "item/agentMessage/delta", new ItemDeltaNotification
        {
            ThreadId = "thread_1", TurnId = "turn_1", ItemId = "item_1", DeltaKind = "agentMessage", Delta = "Hello, "
        });
        await PushNotificationAsync(transport, "item/agentMessage/delta", new ItemDeltaNotification
        {
            ThreadId = "thread_1", TurnId = "turn_1", ItemId = "item_1", DeltaKind = "agentMessage", Delta = "world."
        });
        await PushNotificationAsync(transport, "item/completed", new ItemNotification
        {
            ThreadId = "thread_1", TurnId = "turn_1", Item = AgentItem("Hello, world.")
        });
        await PushNotificationAsync(transport, "turn/completed", new TurnNotification
        {
            Turn = Turn("completed", [AgentItem("Hello, world.")])
        });

        var result = await runTask.WaitAsync(Timeout);
        Assert.Equal("Hello, world.", result.Text);
        Assert.Equal("turn_1", result.Turn!.Id);
        Assert.Equal("completed", result.Turn.Status);
    }

    [Fact]
    public async Task RunStreamedAsync_UsesTypedEventsAndRawFallbackInOrder()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);
        var events = new List<DotCraftRunEvent>();
        var runTask = Task.Run(async () =>
        {
            await foreach (var runEvent in thread.RunStreamedAsync("hi"))
                events.Add(runEvent);
        });

        await RespondAsync(transport, "thread/subscribe", new RpcEmpty());
        await RespondAsync(transport, "turn/start", new TurnStartResult { Turn = Turn("inProgress") });
        await PushNotificationAsync(transport, "vendor/progress", new { threadId = "thread_1", turnId = "turn_1", progress = 0.5 });
        await PushNotificationAsync(transport, "item/agentMessage/delta", new ItemDeltaNotification
        {
            ThreadId = "thread_1", TurnId = "turn_1", ItemId = "item_1", DeltaKind = "agentMessage", Delta = "Hi."
        });
        await PushNotificationAsync(transport, "turn/completed", new TurnNotification { Turn = Turn("completed") });
        await runTask.WaitAsync(Timeout);

        Assert.Collection(
            events,
            value => Assert.IsType<DotCraftRawRunEvent>(value),
            value => Assert.IsType<DotCraftRunEvent<ItemDeltaNotification>>(value),
            value => Assert.IsType<DotCraftRunEvent<TurnNotification>>(value));
        Assert.Equal([DotCraftRunEventTypes.Raw, DotCraftRunEventTypes.AgentMessageDelta, DotCraftRunEventTypes.Completed], events.Select(value => value.Type));
    }

    [Fact]
    public async Task RunStreamedAsync_KnownMalformedNotificationFailsWithStableProtocolError()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);
        var runTask = Task.Run(async () =>
        {
            await foreach (var _ in thread.RunStreamedAsync("hi"))
            {
            }
        });

        await RespondAsync(transport, "thread/subscribe", new RpcEmpty());
        await RespondAsync(transport, "turn/start", new TurnStartResult { Turn = Turn("inProgress") });
        await PushNotificationAsync(transport, "item/agentMessage/delta", new { threadId = "thread_1", turnId = "turn_1", itemId = "item_1" });

        var error = await Assert.ThrowsAsync<ProtocolViolationException>(() => runTask.WaitAsync(Timeout));
        Assert.Equal("protocolViolation", error.Code);
    }

    [Fact]
    public async Task RunAsync_ThrowsTypedFailureAndCancellationErrors()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var failed = thread.RunAsync("fail");
        await RespondAsync(transport, "thread/subscribe", new RpcEmpty());
        await RespondAsync(transport, "turn/start", new TurnStartResult { Turn = Turn("inProgress") });
        await PushNotificationAsync(transport, "turn/failed", new TurnNotification { Turn = Turn("failed"), Error = "model overloaded" });
        var failedError = await Assert.ThrowsAsync<TurnFailedException>(() => failed.WaitAsync(Timeout));
        Assert.Contains("model overloaded", failedError.Message);

        var cancelled = thread.RunAsync("cancel");
        await RespondAsync(transport, "turn/start", new TurnStartResult { Turn = Turn("inProgress", id: "turn_2") });
        await PushNotificationAsync(transport, "turn/cancelled", new TurnNotification
        {
            Turn = Turn("cancelled", id: "turn_2"), Reason = "user"
        });
        var cancelledError = await Assert.ThrowsAsync<TurnCancelledException>(() => cancelled.WaitAsync(Timeout));
        Assert.Equal("turn_2", cancelledError.TurnId);
    }

    [Fact]
    public async Task RunAsync_BusyThreadCanReturnTypedEnqueueResult()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);
        var run = thread.RunAsync("later", new RunOptions { EnqueueIfBusy = true });
        await RespondAsync(transport, "thread/subscribe", new RpcEmpty());
        await RespondErrorAsync(transport, "turn/start", AppServerErrorCodes.TurnInProgress, "busy");
        await RespondAsync(transport, "turn/enqueue", new TurnEnqueueResult
        {
            QueuedInput = QueuedInput(), QueuedInputs = [QueuedInput()]
        });

        var result = await run.WaitAsync(Timeout);
        Assert.Null(result.Turn);
        Assert.Null(result.TurnId);
    }

    [Fact]
    public async Task TypedApprovalAndUserInputCallbacksRoundTrip()
    {
        ApprovalRequestParams? approval = null;
        UserInputRequestParams? input = null;
        var options = new DotCraftClientOptions
        {
            ApprovalHandler = (parameters, _) =>
            {
                approval = parameters;
                return Task.FromResult(ApprovalResponses.Decline);
            },
            UserInputHandler = (parameters, _) =>
            {
                input = parameters;
                return Task.FromResult(new UserInputResponseResult
                {
                    Answers = new Dictionary<string, UserInputAnswer>
                    {
                        ["q1"] = new() { Answers = ["a1"] }
                    }
                });
            }
        };
        var (client, transport) = await ConnectAsync(options);
        await using var _ = client;

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0", id = 42, method = "item/approval/request", @params = new ApprovalRequestParams
            {
                ThreadId = "thread_1", TurnId = "turn_1", ItemId = "item_1", RequestId = "approval_1",
                ApprovalType = "shell", Operation = "deploy", Target = "production", ScopeKey = "workspace", ExpiresAt = Now
            }
        });
        using (var outbound = await transport.ReadOutboundAsync())
            Assert.Equal("decline", outbound.RootElement.GetProperty("result").GetProperty("decision").GetString());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0", id = 43, method = "item/tool/requestUserInput", @params = new UserInputRequestParams
            {
                ThreadId = "thread_1", TurnId = "turn_1", ItemId = "item_2", RequestId = "input_1", Questions = []
            }
        });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var answers = outbound.RootElement.GetProperty("result").GetProperty("answers").GetProperty("q1").GetProperty("answers");
            Assert.Equal("a1", answers[0].GetString());
        }

        Assert.Equal("approval_1", approval!.RequestId);
        Assert.Equal("input_1", input!.RequestId);
    }

    [Fact]
    public async Task ThreadList_SendsRequiredIdentityAndReturnsContractsResult()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var identity = new SessionIdentity { ChannelName = "sdk", UserId = "user", WorkspacePath = "C:/workspace" };
        var task = client.Threads.ListAsync(new ThreadListParams { Identity = identity, Scope = "workspace", IncludeArchived = true });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.Equal("sdk", parameters.GetProperty("identity").GetProperty("channelName").GetString());
            Assert.Equal("workspace", parameters.GetProperty("scope").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id, result = new ThreadListResult
                {
                    Data = [new ThreadSummary { Id = "thread_1", Status = "active", DisplayName = "First" }]
                }
            });
        }
        var result = await task.WaitAsync(Timeout);
        Assert.Equal("thread_1", Assert.Single(result.Data).Id);
    }

    [Fact]
    public async Task ProviderAndModelClientsReturnContractDtos()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var providersTask = client.Providers.ListAsync();
        await RespondAsync(transport, "provider/list", new ProviderListResult
        {
            Providers = new List<ProviderInfo> { new() { Id = "openai", DisplayName = "OpenAI" } }
        });
        var providers = await providersTask.WaitAsync(Timeout);
        Assert.Equal("openai", Assert.Single(providers.Providers.Value!).Id.Value);

        var modelsTask = client.Models.GetCatalogAsync("openai");
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("openai", outbound.RootElement.GetProperty("params").GetProperty("providerId").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id, result = new ModelListResult
                {
                    Success = true,
                    ProviderId = "openai",
                    Models = new List<ModelCatalogItem> { new() { Id = "gpt-5.6-sol" } }
                }
            });
        }
        var models = await modelsTask.WaitAsync(Timeout);
        Assert.Equal("gpt-5.6-sol", Assert.Single(models.Models.Value!).Id.Value);
    }

    [Fact]
    public async Task UpdateModelConfiguration_PreservesEveryUnrelatedOptionalAndUnknownField()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var current = Configuration("openai", "gpt-5.5");
        current.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["futureSetting"] = JsonSerializer.SerializeToElement("kept")
        };
        var task = client.Threads.UpdateModelConfigurationAsync(
            "thread_1",
            "anthropic",
            "claude-sonnet-4-5",
            new ReasoningConfig { Enabled = true, Effort = "high", Output = "full" },
            "fast",
            new ThreadContextWindowConfig { Mode = "max" });
        await RespondAsync(transport, "thread/read", new ThreadReadResult { Thread = Thread(configuration: current) });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var config = outbound.RootElement.GetProperty("params").GetProperty("config");
            Assert.Equal("agent-profile", config.GetProperty("agentProfileId").GetString());
            Assert.Equal("kept", config.GetProperty("futureSetting").GetString());
            Assert.Equal("anthropic", config.GetProperty("providerId").GetString());
            Assert.Equal("high", config.GetProperty("reasoning").GetProperty("effort").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result = new RpcEmpty() });
        }
        await RespondAsync(transport, "thread/read", new ThreadReadResult
        {
            Thread = Thread(configuration: Configuration("anthropic", "claude-sonnet-4-5"))
        });
        var authoritative = await task.WaitAsync(Timeout);
        Assert.Equal("anthropic", authoritative.ProviderId.Value);
    }

    private static async Task<(DotCraftClient client, TestJsonRpcTransport transport)> ConnectAsync(DotCraftClientOptions? options = null)
    {
        var transport = new TestJsonRpcTransport();
        var value = options ?? new DotCraftClientOptions { ClientName = "test", ClientVersion = "0.1" };
        var connectTask = DotCraftClient.ConnectAsync(transport, value);
        using (var init = await transport.ReadOutboundAsync())
        {
            var id = init.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id, result = new InitializeResult
                {
                    ServerInfo = new ServerInfo { Name = "dotcraft", Version = "test", ProtocolVersion = "1" },
                    Capabilities = new ServerCapabilities { ThreadManagement = true, ThreadSubscriptions = true }
                }
            });
        }
        using (await transport.ReadOutboundAsync())
        {
        }
        return (await connectTask, transport);
    }

    private static async Task<DotCraftThread> StartThreadAsync(DotCraftClient client, TestJsonRpcTransport transport)
    {
        var task = client.Threads.StartAsync(new ThreadStartParams
        {
            Identity = new SessionIdentity { ChannelName = "sdk", UserId = "user" }
        });
        await RespondAsync(transport, "thread/start", new ThreadStartResult { Thread = Thread() });
        return await task.WaitAsync(Timeout);
    }

    private static async Task RespondAsync(TestJsonRpcTransport transport, string method, object result)
    {
        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(method, outbound.RootElement.GetProperty("method").GetString());
        var id = outbound.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result });
    }

    private static async Task RespondErrorAsync(TestJsonRpcTransport transport, string method, int code, string message)
    {
        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(method, outbound.RootElement.GetProperty("method").GetString());
        var id = outbound.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new { jsonrpc = "2.0", id, error = new { code, message } });
    }

    private static Task PushNotificationAsync(TestJsonRpcTransport transport, string method, object parameters) =>
        transport.PushInboundAsync(new { jsonrpc = "2.0", method, @params = parameters });

    private static SessionThread Thread(string id = "thread_1", ThreadConfiguration? configuration = null) => new()
    {
        Id = id,
        SessionId = "session_1",
        WorkspacePath = "C:/workspace",
        Cwd = "C:/workspace",
        RuntimeWorkspaceRoots = ["C:/workspace"],
        EffectiveWorkspacePath = "C:/workspace",
        Ephemeral = false,
        Worktree = null,
        OriginChannel = "sdk",
        Status = "active",
        Source = new ThreadSource { Kind = "user" },
        CreatedAt = Now,
        LastActiveAt = Now,
        HistoryMode = "full",
        Configuration = configuration ?? Configuration("openai", "gpt-5.6-sol"),
        Metadata = new Dictionary<string, string>(),
        Runtime = new ThreadRuntimeState { Busy = false, Running = false },
        QueuedInputs = []
    };

    private static SessionTurn Turn(string status, IReadOnlyList<SessionItem>? items = null, string id = "turn_1") => new()
    {
        Id = id,
        ThreadId = "thread_1",
        Status = status,
        StartedAt = Now,
        CompletedAt = status == "inProgress" ? null : Now,
        Items = items
    };

    private static SessionItem AgentItem(string text) => new()
    {
        Id = "item_1",
        TurnId = "turn_1",
        Type = "agentMessage",
        Status = "completed",
        CreatedAt = Now,
        CompletedAt = Now,
        PayloadKind = "agentMessage",
        Payload = JsonSerializer.SerializeToElement(new AgentMessagePayload { Text = text }, AppServerContractJson.Options)
    };

    private static QueuedTurnInput QueuedInput() => new()
    {
        Id = "queue_1",
        ThreadId = "thread_1",
        NativeInputParts = [new InputPart { Type = "text", Text = "later" }],
        MaterializedInputParts = [new InputPart { Type = "text", Text = "later" }],
        DisplayText = "later",
        Status = "queued",
        CreatedAt = Now
    };

    private static ThreadConfiguration Configuration(string provider, string model) => new()
    {
        AgentProfileId = "agent-profile",
        ApprovalPolicy = "autoApprove",
        ProviderId = provider,
        Model = model,
        Reasoning = new ReasoningConfig { Enabled = true, Effort = "medium", Output = "full" },
        Speed = "standard",
        ContextWindow = new ThreadContextWindowConfig { Mode = "default" }
    };
}
