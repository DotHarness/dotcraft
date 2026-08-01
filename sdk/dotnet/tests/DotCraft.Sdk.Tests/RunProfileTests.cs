using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.Tests;

public sealed class RunProfileTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunAsync_MergesTextFromTurnCompletedItems()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var runTask = thread.RunAsync("hello");
        await RespondAsync(transport, "thread/subscribe", new { ok = true });
        await RespondAsync(transport, "turn/start", new { turnId = "turn_1" });

        await PushAgentMessageRunAsync(transport, deltas: ["Hello, ", "world."], finalText: "Hello, world.");

        var result = await runTask.WaitAsync(Timeout);
        Assert.Equal("thread_1", result.ThreadId);
        Assert.Equal("turn_1", result.TurnId);
        Assert.Equal("Hello, world.", result.Text);
    }

    [Fact]
    public async Task RunAsync_FallsBackToStreamedSnapshot_WhenTurnHasNoItems()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var runTask = thread.RunAsync("hello");
        await RespondAsync(transport, "thread/subscribe", new { ok = true });
        await RespondAsync(transport, "turn/start", new { turnId = "turn_1" });

        await PushNotificationAsync(transport, "item/agentMessage/delta", new { threadId = "thread_1", turnId = "turn_1", itemId = "item_1", delta = "Partial " });
        await PushNotificationAsync(transport, "item/completed", new { threadId = "thread_1", turnId = "turn_1", item = new { id = "item_1", type = "agentMessage", status = "completed", payload = new { text = "Partial snapshot." } } });
        // turn/completed without items array -> reducer falls back to the item snapshot.
        await PushNotificationAsync(transport, "turn/completed", new { turn = new { id = "turn_1", threadId = "thread_1", status = "completed" } });

        var result = await runTask.WaitAsync(Timeout);
        Assert.Equal("Partial snapshot.", result.Text);
    }

    [Fact]
    public async Task RunStreamedAsync_YieldsNormalizedEventsInOrder()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var events = new List<DotCraftRunEvent>();
        var runTask = Task.Run(async () =>
        {
            await foreach (var runEvent in thread.RunStreamedAsync("hi"))
            {
                events.Add(runEvent);
            }
        });

        await RespondAsync(transport, "thread/subscribe", new { ok = true });
        await RespondAsync(transport, "turn/start", new { turnId = "turn_1" });

        await PushNotificationAsync(transport, "turn/started", new { threadId = "thread_1", turnId = "turn_1" });
        await PushNotificationAsync(transport, "item/started", new { threadId = "thread_1", turnId = "turn_1", item = new { id = "item_1", type = "agentMessage" } });
        await PushNotificationAsync(transport, "item/agentMessage/delta", new { threadId = "thread_1", turnId = "turn_1", itemId = "item_1", delta = "Hi." });
        await PushNotificationAsync(transport, "item/completed", new { threadId = "thread_1", turnId = "turn_1", item = new { id = "item_1", type = "agentMessage", payload = new { text = "Hi." } } });
        await PushNotificationAsync(transport, "turn/completed", new { turn = new { id = "turn_1", threadId = "thread_1", status = "completed" } });

        await runTask.WaitAsync(Timeout);

        Assert.Equal(
            [
                DotCraftRunEventTypes.TurnStarted,
                DotCraftRunEventTypes.ItemStarted,
                DotCraftRunEventTypes.AgentMessageDelta,
                DotCraftRunEventTypes.ItemCompleted,
                DotCraftRunEventTypes.Completed
            ],
            events.Select(e => e.Type).ToArray());
        Assert.All(events, e => Assert.Equal("thread_1", e.ThreadId));
    }

    [Fact]
    public async Task RunAsync_ThrowsTurnFailedError_OnFailure()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var runTask = thread.RunAsync("hi");
        await RespondAsync(transport, "thread/subscribe", new { ok = true });
        await RespondAsync(transport, "turn/start", new { turnId = "turn_1" });
        await PushNotificationAsync(transport, "turn/failed", new { turn = new { id = "turn_1", threadId = "thread_1", status = "failed" }, error = "model overloaded" });

        var error = await Assert.ThrowsAsync<TurnFailedError>(async () => await runTask.WaitAsync(Timeout));
        Assert.Equal("thread_1", error.ThreadId);
        Assert.Equal("turn_1", error.TurnId);
        Assert.Contains("model overloaded", error.Message);
    }

    [Fact]
    public async Task RunAsync_ThrowsTurnInProgressError_WhenBusy()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var runTask = thread.RunAsync("hi");
        await RespondAsync(transport, "thread/subscribe", new { ok = true });

        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("turn/start", outbound.RootElement.GetProperty("method").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new { jsonrpc = "2.0", id, error = new { code = -32012, message = "Turn in progress" } });
        }

        await Assert.ThrowsAsync<TurnInProgressError>(async () => await runTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ApprovalHandler_RespondsWithDecision()
    {
        ApprovalRequest? captured = null;
        var options = new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1",
            ApprovalHandler = (request, _) =>
            {
                captured = request;
                return Task.FromResult(ApprovalDecision.Decline);
            }
        };
        var (client, transport) = await ConnectAsync(options);
        await using var _ = client;

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 42,
            method = "item/approval/request",
            @params = new
            {
                requestId = "approval_1",
                threadId = "thread_1",
                turnId = "turn_1",
                itemId = "item_1",
                approvalType = "shell",
                operation = "deploy",
                target = "production",
                reason = "Protected environment",
                expiresAt = "2026-07-28T12:30:00Z"
            }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(42, outbound.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("decline", outbound.RootElement.GetProperty("result").GetProperty("decision").GetString());
        Assert.NotNull(captured);
        Assert.Equal("thread_1", captured!.ThreadId);
        Assert.Equal("approval_1", captured.RequestId);
        Assert.Equal("item_1", captured.ItemId);
        Assert.Equal("shell", captured.ApprovalType);
        Assert.Equal("deploy", captured.Operation);
        Assert.Equal("production", captured.Target);
        Assert.Equal(DateTimeOffset.Parse("2026-07-28T12:30:00Z"), captured.ExpiresAt);
    }

    [Fact]
    public async Task ApprovalRequest_ReturnsMethodNotFound_WhenNoHandler()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "item/approval/request",
            @params = new { threadId = "thread_1" }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(-32601, outbound.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task UserInputHandler_RespondsWithAnswers()
    {
        var options = new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1",
            UserInputHandler = (_, _) =>
                Task.FromResult(new UserInputResponse(new Dictionary<string, object?> { ["q1"] = "a1" }))
        };
        var (client, transport) = await ConnectAsync(options);
        await using var _ = client;

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "item/tool/requestUserInput",
            @params = new { threadId = "thread_1" }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal("a1", outbound.RootElement.GetProperty("result").GetProperty("answers").GetProperty("q1").GetString());
    }

    [Fact]
    public async Task Initialize_AdvertisesUserInputSupport_WhenHandlerProvided()
    {
        var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1",
            UserInputHandler = (_, _) => Task.FromResult(UserInputResponse.Empty)
        });

        using (var init = await transport.ReadOutboundAsync())
        {
            Assert.True(init.RootElement.GetProperty("params").GetProperty("capabilities").GetProperty("requestUserInputSupport").GetBoolean());
            var initId = init.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = initId,
                result = new { serverInfo = new { name = "d", version = "1", protocolVersion = "1" }, capabilities = new { } }
            });
        }

        using (await transport.ReadOutboundAsync())
        {
        }

        await using var client = await connectTask;
    }

    [Fact]
    public async Task ListAsync_ParsesThreadSummaries()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var listTask = client.Threads.ListAsync();
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("thread/list", outbound.RootElement.GetProperty("method").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    threads = new object[]
                    {
                        new { id = "thread_1", status = "active", displayName = "First" },
                        new { id = "thread_2", status = "paused" }
                    }
                }
            });
        }

        var threads = await listTask.WaitAsync(Timeout);
        Assert.Equal(2, threads.Count);
        Assert.Equal("thread_1", threads[0].Id);
        Assert.Equal("active", threads[0].Status);
        Assert.Equal("First", threads[0].DisplayName);
        Assert.Equal("thread_2", threads[1].Id);
    }

    [Fact]
    public async Task ListAsync_WorkspaceScope_SendsTypedScope()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var listTask = client.Threads.ListAsync(new DotCraftThreadListOptions(
            IncludeArchived: true,
            Scope: DotCraftThreadListScope.Workspace));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("thread/list", outbound.RootElement.GetProperty("method").GetString());
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.True(parameters.GetProperty("includeArchived").GetBoolean());
            Assert.Equal("workspace", parameters.GetProperty("scope").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new { threads = Array.Empty<object>() }
            });
        }

        Assert.Empty(await listTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ThreadHandle_SetMode_SendsTypedRequest()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var thread = await StartThreadAsync(client, transport);

        var modeTask = thread.SetModeAsync("plan");
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("thread/mode/set", outbound.RootElement.GetProperty("method").GetString());
            var @params = outbound.RootElement.GetProperty("params");
            Assert.Equal("thread_1", @params.GetProperty("threadId").GetString());
            Assert.Equal("plan", @params.GetProperty("mode").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result = new { ok = true } });
        }

        await modeTask.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ProviderAndModelCatalogs_ParseCapabilityMetadata()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var providersTask = client.Providers.ListAsync();
        await RespondAsync(transport, "provider/list", new
        {
            providers = new[]
            {
                new { id = "openai", displayName = "OpenAI (ChatGPT)", protocol = "openai-responses", isImplicit = true }
            }
        });
        var providers = await providersTask.WaitAsync(Timeout);
        Assert.Single(providers.Providers);
        Assert.True(providers.Providers[0].IsImplicit);

        var modelsTask = client.Models.GetCatalogAsync("openai");
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("model/list", outbound.RootElement.GetProperty("method").GetString());
            Assert.Equal("openai", outbound.RootElement.GetProperty("params").GetProperty("providerId").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    success = true,
                    providerId = "openai",
                    protocol = "openai-responses",
                    models = new[]
                    {
                        new
                        {
                            id = "gpt-5.6-sol",
                            ownedBy = "openai",
                            createdAt = "2026-07-01T00:00:00Z",
                            reasoning = new
                            {
                                supportsDisable = true,
                                supportedEfforts = new[] { new { effort = "medium", label = "Medium" }, new { effort = "high", label = "High" } },
                                defaultEffort = "medium",
                                supportedOutputs = new[] { "none", "full" },
                                defaultOutput = "full"
                            },
                            speed = new { supportedModes = new[] { "standard", "fast" }, defaultMode = "standard" },
                            contextWindow = new { catalogWindow = 1000000, configuredWindow = 256000, supportsMax = true, maxWindow = 1000000 }
                        }
                    }
                }
            });
        }

        var catalog = await modelsTask.WaitAsync(Timeout);
        Assert.True(catalog.Success);
        Assert.Equal("openai", catalog.ProviderId);
        var model = Assert.Single(catalog.Models);
        Assert.Equal("gpt-5.6-sol", model.Id);
        Assert.Equal(["standard", "fast"], model.Speed!.SupportedModes);
        Assert.Equal("high", model.Reasoning!.SupportedEfforts[1].Effort);
        Assert.True(model.ContextWindow!.SupportsMax);
        Assert.Equal(1000000, model.ContextWindow.MaxWindow);
    }

    [Fact]
    public async Task UpdateModelConfiguration_PreservesUnrelatedThreadFields()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var update = new DotCraftModelConfiguration(
            "anthropic",
            "claude-sonnet-4-5",
            new DotCraftReasoningConfiguration(true, "high", "full"),
            "fast",
            new DotCraftContextWindowConfiguration("max"));

        var task = client.Threads.UpdateModelConfigurationAsync("thread_1", update);
        await RespondAsync(transport, "thread/read", ThreadSnapshot("openai", "gpt-5.5"));

        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("thread/config/update", outbound.RootElement.GetProperty("method").GetString());
            var parameters = outbound.RootElement.GetProperty("params");
            var config = parameters.GetProperty("config");
            Assert.Equal("agent-profile", config.GetProperty("agentProfileId").GetString());
            Assert.Equal("autoApprove", config.GetProperty("approvalPolicy").GetString());
            Assert.Equal("anthropic", config.GetProperty("providerId").GetString());
            Assert.Equal("claude-sonnet-4-5", config.GetProperty("model").GetString());
            Assert.Equal("fast", config.GetProperty("speed").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result = new { } });
        }

        await RespondAsync(
            transport,
            "thread/read",
            ThreadSnapshot(
                update.ProviderId,
                update.Model,
                update.Reasoning,
                update.Speed,
                update.ContextWindow));
        Assert.Equal(update, await task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task StartAsync_SerializesTypedModelConfiguration()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var configuration = new DotCraftModelConfiguration(
            "openai",
            "gpt-5.6-sol",
            new DotCraftReasoningConfiguration(true, "medium", "full"),
            "standard",
            new DotCraftContextWindowConfiguration("default"));

        var task = client.Threads.StartAsync(new DotCraftThreadStartRequest(
            new SessionIdentity("universe", "member"),
            Config: new
            {
                agentProfileId = "release-operator",
                providerId = configuration.ProviderId,
                model = configuration.Model,
                reasoning = configuration.Reasoning,
                speed = configuration.Speed,
                contextWindow = configuration.ContextWindow
            }));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var config = outbound.RootElement.GetProperty("params").GetProperty("config");
            Assert.Equal("release-operator", config.GetProperty("agentProfileId").GetString());
            Assert.Equal("gpt-5.6-sol", config.GetProperty("model").GetString());
            Assert.Equal("medium", config.GetProperty("reasoning").GetProperty("effort").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = ThreadSnapshot(
                    configuration.ProviderId,
                    configuration.Model,
                    configuration.Reasoning,
                    configuration.Speed,
                    configuration.ContextWindow)
            });
        }

        var thread = await task.WaitAsync(Timeout);
        Assert.Equal("thread_1", thread.Id);
    }

    private static async Task<(DotCraftClient client, TestJsonRpcTransport transport)> ConnectAsync(DotCraftClientOptions? options = null)
    {
        var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, options ?? new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1"
        });

        using (var init = await transport.ReadOutboundAsync())
        {
            var initId = init.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = initId,
                result = new
                {
                    serverInfo = new { name = "dotcraft", version = "test", protocolVersion = "1" },
                    capabilities = new { threadManagement = true, threadSubscriptions = true }
                }
            });
        }

        using (await transport.ReadOutboundAsync())
        {
        }

        var client = await connectTask;
        return (client, transport);
    }

    private static async Task<DotCraftThread> StartThreadAsync(DotCraftClient client, TestJsonRpcTransport transport)
    {
        var startTask = client.Threads.StartAsync(new DotCraftThreadStartRequest(new SessionIdentity("sdk", "user")));
        await RespondAsync(transport, "thread/start", new { thread = new { id = "thread_1", status = "active" } });
        return await startTask.WaitAsync(Timeout);
    }

    private static async Task RespondAsync(TestJsonRpcTransport transport, string method, object result)
    {
        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(method, outbound.RootElement.GetProperty("method").GetString());
        var id = outbound.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result });
    }

    private static object ThreadSnapshot(
        string providerId,
        string model,
        DotCraftReasoningConfiguration? reasoning = null,
        string speed = "standard",
        DotCraftContextWindowConfiguration? contextWindow = null) =>
        new
        {
            thread = new
            {
                id = "thread_1",
                status = "active",
                configuration = new
                {
                    agentProfileId = "agent-profile",
                    approvalPolicy = "autoApprove",
                    providerId,
                    model,
                    reasoning = reasoning ?? new DotCraftReasoningConfiguration(true, "medium", "full"),
                    speed,
                    contextWindow = contextWindow ?? new DotCraftContextWindowConfiguration("default")
                }
            }
        };

    private static Task PushNotificationAsync(TestJsonRpcTransport transport, string method, object parameters) =>
        transport.PushInboundAsync(new { jsonrpc = "2.0", method, @params = parameters });

    private static async Task PushAgentMessageRunAsync(TestJsonRpcTransport transport, string[] deltas, string finalText)
    {
        await PushNotificationAsync(transport, "item/started", new { threadId = "thread_1", turnId = "turn_1", item = new { id = "item_1", type = "agentMessage", status = "started" } });
        foreach (var delta in deltas)
        {
            await PushNotificationAsync(transport, "item/agentMessage/delta", new { threadId = "thread_1", turnId = "turn_1", itemId = "item_1", deltaKind = "agentMessage", delta });
        }

        await PushNotificationAsync(transport, "item/completed", new { threadId = "thread_1", turnId = "turn_1", item = new { id = "item_1", type = "agentMessage", status = "completed", payload = new { text = finalText } } });
        await PushNotificationAsync(transport, "turn/completed", new
        {
            turn = new
            {
                id = "turn_1",
                threadId = "thread_1",
                status = "completed",
                items = new object[] { new { id = "item_1", type = "agentMessage", payload = new { text = finalText } } }
            }
        });
    }
}
