using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Sdk;
using DotCraft.Sdk.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class OratorioAppBindingSdkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ClientOptions_AdvertiseNoInteractiveApprovalSupport()
    {
        var transport = new TestJsonRpcTransport();
        await using var client = await ConnectSdkClientAsync(
            transport,
            DotCraftAppServerClientFactory.CreateClientOptions(),
            initialize =>
            {
                var parameters = initialize.GetProperty("params");
                Assert.Equal("oratorio", parameters.GetProperty("clientInfo").GetProperty("name").GetString());
                Assert.Equal("0.5.2", parameters.GetProperty("clientInfo").GetProperty("version").GetString());
                Assert.False(parameters.GetProperty("capabilities").GetProperty("approvalSupport").GetBoolean());
                Assert.Equal(false, DotCraftAppServerClientFactory.CreateClientOptions().AutoReconnect);
            });
    }

    [Fact]
    public async Task StartThread_UsesBaseWorkspaceForIdentityAndWorktreeForExecution()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        await using var client = new DotCraftAppServerClient(sdkClient);
        var baseWorkspace = Path.Combine(Path.GetTempPath(), "oratorio-base");
        var worktree = Path.Combine(baseWorkspace, ".craft", "oratorio", "worktrees", "review");

        var startTask = client.StartThreadAsync(new AppServerThreadStartRequest(
            DisplayName: "Review",
            BaseWorkspacePath: baseWorkspace,
            ExecutionWorkspacePath: worktree,
            ApprovalPolicy: "interrupt",
            AgentInstructions: "Review the change."), CancellationToken.None);

        using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            Assert.Equal("thread/start", outbound.RootElement.GetProperty("method").GetString());
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.Equal(
                baseWorkspace,
                parameters.GetProperty("identity").GetProperty("workspacePath").GetString());
            var config = parameters.GetProperty("config");
            Assert.Equal(worktree, config.GetProperty("executionWorkspaceOverride").GetString());
            Assert.False(config.TryGetProperty("workspaceOverride", out _));

            await transport.PushResultAsync(outbound, ThreadStartResult("thread-review", baseWorkspace, worktree));
        }

        Assert.Equal("thread-review", await startTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task StartThread_OmitsExecutionOverrideWhenWorkspacePathsMatch()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        await using var client = new DotCraftAppServerClient(sdkClient);
        var workspace = Path.Combine(Path.GetTempPath(), "oratorio-workspace");

        var startTask = client.StartThreadAsync(new AppServerThreadStartRequest(
            DisplayName: "Review",
            BaseWorkspacePath: workspace,
            ExecutionWorkspacePath: workspace,
            ApprovalPolicy: "interrupt",
            AgentInstructions: "Review the change."), CancellationToken.None);

        using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            var config = outbound.RootElement.GetProperty("params").GetProperty("config");
            Assert.False(config.TryGetProperty("executionWorkspaceOverride", out _));
            Assert.False(config.TryGetProperty("workspaceOverride", out _));
            await transport.PushResultAsync(outbound, ThreadStartResult("thread-review", workspace, workspace));
        }

        Assert.Equal("thread-review", await startTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ReadThread_RequestsOnlyNewestItemPageAndReturnsItChronologically()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        await using var client = new DotCraftAppServerClient(sdkClient);

        var readTask = client.ReadThreadAsync("thread-history", CancellationToken.None);
        using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            Assert.Equal("thread/items/list", outbound.RootElement.GetProperty("method").GetString());
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.Equal("thread-history", parameters.GetProperty("threadId").GetString());
            Assert.Equal(200, parameters.GetProperty("limit").GetInt32());
            Assert.Equal("descending", parameters.GetProperty("sortDirection").GetString());
            Assert.Equal(3, parameters.EnumerateObject().Count());

            await transport.PushResultAsync(outbound, new
            {
                data = new object[]
                {
                    new
                    {
                        turnId = "turn-new",
                        item = new
                        {
                            id = "item-new",
                            turnId = "turn-new",
                            type = "agentMessage",
                            status = "completed",
                            createdAt = "2026-08-05T12:01:00Z",
                            completedAt = "2026-08-05T12:01:01Z",
                            payload = new { text = "new" }
                        }
                    },
                    new
                    {
                        turnId = "turn-old",
                        item = new
                        {
                            id = "item-old",
                            turnId = "turn-old",
                            type = "userMessage",
                            status = "completed",
                            createdAt = "2026-08-05T12:00:00Z",
                            completedAt = "2026-08-05T12:00:01Z",
                            payload = new { text = "old" }
                        }
                    }
                },
                nextCursor = "older-page"
            });
        }

        var result = await readTask.WaitAsync(Timeout);
        Assert.Equal("thread-history", result.ThreadId);
        Assert.Collection(
            result.Items,
            item => Assert.Equal("item-old", item.Id),
            item => Assert.Equal("item-new", item.Id));
    }

    [Fact]
    public async Task ListModels_MapsSdkCatalogProjection()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        await using var client = new DotCraftAppServerClient(sdkClient);

        var listTask = client.ListModelsAsync(CancellationToken.None);
        using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            Assert.Equal("model/list", outbound.RootElement.GetProperty("method").GetString());
            await transport.PushResultAsync(outbound, new
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
                            supportedEfforts = new[] { new { effort = "medium", label = "Medium" } },
                            defaultEffort = "medium",
                            supportedOutputs = new[] { "none", "full" },
                            defaultOutput = "full"
                        },
                        speed = new
                        {
                            supportedModes = new[] { "standard", "fast" },
                            defaultMode = "standard"
                        },
                        contextWindow = new
                        {
                            catalogWindow = 1_000_000,
                            configuredWindow = 256_000,
                            supportsMax = true,
                            maxWindow = 1_000_000
                        }
                    }
                }
            });
        }

        var model = Assert.Single(await listTask.WaitAsync(Timeout));
        Assert.Equal("gpt-5.6-sol", model.Id);
        Assert.Equal("gpt-5.6-sol", model.DisplayName);
        Assert.Equal("openai", model.Provider);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "MissingApiKey")]
    public async Task ListModels_ReturnsEmptyForEmptyOrFailedCatalog(bool success, string? errorCode)
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        await using var client = new DotCraftAppServerClient(sdkClient);

        var listTask = client.ListModelsAsync(CancellationToken.None);
        using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            await transport.PushResultAsync(outbound, new
            {
                success,
                providerId = "openai",
                protocol = "openai-responses",
                models = Array.Empty<object>(),
                errorCode,
                errorMessage = errorCode is null ? null : "API key is not configured."
            });
        }

        Assert.Empty(await listTask.WaitAsync(Timeout));
    }

    [Fact]
    public async Task ApproveConnection_UsesSdkAppBindingClientAndPersistsTypedResult()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        var client = new DotCraftAppServerClient(sdkClient);
        var factory = new SingleClientFactory(client);
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"oratorio-app-binding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        try
        {
            var workspacePath = Path.GetFullPath(stateDirectory);
            var runtimeIdentity = $"local:{workspacePath}";
            var store = new OratorioDotCraftBindingStore(Path.Combine(stateDirectory, "binding.json"));
            using var services = new ServiceCollection().BuildServiceProvider();
            var boardSurfaceRuntime = new OratorioBoardSurfaceRuntime();
            var service = new OratorioAppBindingService(
                factory,
                null!,
                store,
                new PassthroughSecretProtector(),
                new OratorioBindingMcpRuntime(
                    services.GetRequiredService<IServiceScopeFactory>(),
                    new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance)),
                boardSurfaceRuntime,
                NullLogger<OratorioAppBindingService>.Instance);

            await Assert.ThrowsAsync<OratorioApiException>(() => service.ApproveAsync(
                BuildHandoff("connect", "connect_req_1", workspacePath, runtimeIdentity, endpoint: null),
                "http://127.0.0.1:5199",
                CancellationToken.None));

            var approveTask = service.ApproveAsync(
                BuildHandoff("connect", "connect_req_1", workspacePath, runtimeIdentity,
                    "ws://127.0.0.1:9100/ws?token=appserver-secret&transport=local"),
                "http://127.0.0.1:5199",
                CancellationToken.None);

            Assert.Equal("ws://127.0.0.1:9100/ws?transport=local", factory.LastAppServerUrl);
            Assert.Equal("appserver-secret", factory.LastToken);

            using (var outbound = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/connect", outbound.RootElement.GetProperty("method").GetString());
                var parameters = outbound.RootElement.GetProperty("params");
                Assert.Equal("connect_req_1", parameters.GetProperty("connectionRequestId").GetString());
                Assert.Equal("request-token", parameters.GetProperty("requestToken").GetString());
                Assert.Equal("Oratorio", parameters.GetProperty("accountLabel").GetString());

                await transport.PushInboundAsync(new
                {
                    jsonrpc = "2.0",
                    id = outbound.RootElement.GetProperty("id").GetInt64(),
                    result = new
                    {
                        principal = new
                        {
                            principalId = "principal-1",
                            appId = "com.dotharness.oratorio",
                            userId = "user-1",
                            expiresAt = "2026-08-15T00:00:00.0000000+00:00"
                        },
                        credential = "credential-1"
                    }
                });
            }

            using (var authenticate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/authenticate", authenticate.RootElement.GetProperty("method").GetString());
                var parameters = authenticate.RootElement.GetProperty("params");
                Assert.Equal("com.dotharness.oratorio", parameters.GetProperty("appId").GetString());
                Assert.Equal("credential-1", parameters.GetProperty("credential").GetString());
                await transport.PushResultAsync(authenticate, new { });
            }

            using (var publish = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/surface/publish", publish.RootElement.GetProperty("method").GetString());
                var parameters = publish.RootElement.GetProperty("params");
                Assert.Equal("board", parameters.GetProperty("surfaceId").GetString());
                Assert.Equal("http://127.0.0.1:5199/dotcraft/surfaces/board/api/v1", parameters.GetProperty("endpoint").GetString());
                Assert.Equal(boardSurfaceRuntime.Bearer, parameters.GetProperty("bearer").GetString());
                await transport.PushResultAsync(publish, new
                {
                    appId = "com.dotharness.oratorio",
                    surfaceId = "board",
                    endpoint = "http://127.0.0.1:5199/dotcraft/surfaces/board/api/v1",
                    bearer = boardSurfaceRuntime.Bearer,
                    expiresAt = "2026-07-16T12:02:00Z"
                });
            }

            var result = await approveTask.WaitAsync(Timeout);
            Assert.Equal("connect", result.Operation);
            Assert.Equal("connected", result.State);
            Assert.True(store.TryLoad(runtimeIdentity, out var persisted));
            Assert.Equal("ws://127.0.0.1:9100/ws?transport=local", persisted.AppServerUrl);
            Assert.DoesNotContain("appserver-secret", persisted.AppServerUrl, StringComparison.Ordinal);
            Assert.Equal("appserver-secret", persisted.ProtectedAppServerToken);
            Assert.Equal("principal-1", persisted.PrincipalId);
            Assert.Equal("credential-1", persisted.ProtectedCredential);
            Assert.Equal(DateTimeOffset.Parse("2026-08-15T00:00:00+00:00"), persisted.PrincipalExpiresAt);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ApproveBinding_UsesSdkAuthenticationInspectionAndActivation()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"oratorio-app-binding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        try
        {
            var workspacePath = Path.GetFullPath(stateDirectory);
            var runtimeIdentity = $"local:{workspacePath}";
            var store = new OratorioDotCraftBindingStore(Path.Combine(stateDirectory, "binding.json"));
            store.Save(new OratorioDotCraftBinding(
                runtimeIdentity,
                workspacePath,
                "ws://127.0.0.1:9100/ws",
                "com.dotharness.oratorio",
                "principal-1",
                "credential-1",
                DateTimeOffset.UtcNow.AddDays(20),
                "Oratorio",
                []));
            using var services = new ServiceCollection().BuildServiceProvider();
            var service = new OratorioAppBindingService(
                new SingleClientFactory(new DotCraftAppServerClient(sdkClient)),
                null!,
                store,
                new PassthroughSecretProtector(),
                new OratorioBindingMcpRuntime(
                    services.GetRequiredService<IServiceScopeFactory>(),
                    new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance)),
                new OratorioBoardSurfaceRuntime(),
                NullLogger<OratorioAppBindingService>.Instance);

            var approveTask = service.ApproveAsync(
                BuildHandoff("bind", "bind_req_1", workspacePath, runtimeIdentity),
                "http://127.0.0.1:5199",
                CancellationToken.None);

            using (var authenticate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/authenticate", authenticate.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(authenticate, new { });
            }

            using (var inspect = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/binding/request/get", inspect.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(inspect, new
                {
                    bindingRequestId = "bind_req_1",
                    bindingId = "binding-1",
                    threadId = "thread-1",
                    appId = "com.dotharness.oratorio",
                    state = "connecting",
                    expiresAt = "2026-07-16T12:00:00+00:00"
                });
            }

            using (var activate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/binding/activate", activate.RootElement.GetProperty("method").GetString());
                var parameters = activate.RootElement.GetProperty("params");
                Assert.Equal("bind_req_1", parameters.GetProperty("bindingRequestId").GetString());
                Assert.Equal("http://127.0.0.1:5199/dotcraft/bindings/binding-1/mcp", parameters.GetProperty("endpoint").GetString());
                Assert.False(string.IsNullOrWhiteSpace(parameters.GetProperty("bearer").GetString()));
                await transport.PushResultAsync(activate, new
                {
                    bindingId = "binding-1",
                    threadId = "thread-1",
                    appId = "com.dotharness.oratorio",
                    state = "active",
                    authorityRevision = 1
                });
            }

            var result = await approveTask.WaitAsync(Timeout);
            Assert.Equal("binding-1", result.BindingId);
            Assert.Equal("active", result.State);
            Assert.True(store.TryLoad(runtimeIdentity, out var persisted));
            var hint = Assert.Single(persisted.Bindings!);
            Assert.Equal("binding-1", hint.BindingId);
            Assert.Equal(1, hint.AuthorityRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RebindPersisted_UsesSdkRebindWithStoredAuthorityRevision()
    {
        var transport = new TestJsonRpcTransport();
        var sdkClient = await ConnectSdkClientAsync(transport);
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"oratorio-app-binding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateDirectory);

        try
        {
            var workspacePath = Path.GetFullPath(stateDirectory);
            var runtimeIdentity = $"local:{workspacePath}";
            var store = new OratorioDotCraftBindingStore(Path.Combine(stateDirectory, "binding.json"));
            store.Save(new OratorioDotCraftBinding(
                runtimeIdentity,
                workspacePath,
                "ws://127.0.0.1:9100/ws",
                "com.dotharness.oratorio",
                "principal-1",
                "credential-1",
                DateTimeOffset.UtcNow.AddDays(20),
                "Oratorio",
                [new OratorioBindingRebindHint("binding-1", "thread-1", 7)]));
            using var services = new ServiceCollection().BuildServiceProvider();
            var service = new OratorioAppBindingService(
                new SingleClientFactory(new DotCraftAppServerClient(sdkClient)),
                null!,
                store,
                new PassthroughSecretProtector(),
                new OratorioBindingMcpRuntime(
                    services.GetRequiredService<IServiceScopeFactory>(),
                    new OratorioDynamicToolCatalog(NullLogger<OratorioDynamicToolCatalog>.Instance)),
                new OratorioBoardSurfaceRuntime(),
                NullLogger<OratorioAppBindingService>.Instance);

            var rebindTask = service.RebindPersistedAsync("http://127.0.0.1:5199", CancellationToken.None);

            using (var authenticate = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/connection/authenticate", authenticate.RootElement.GetProperty("method").GetString());
                await transport.PushResultAsync(authenticate, new { });
            }

            using (var rebind = await transport.ReadOutboundAsync().WaitAsync(Timeout))
            {
                Assert.Equal("app/binding/rebind", rebind.RootElement.GetProperty("method").GetString());
                var parameters = rebind.RootElement.GetProperty("params");
                Assert.Equal("binding-1", parameters.GetProperty("bindingId").GetString());
                Assert.Equal(7, parameters.GetProperty("authorityRevision").GetInt64());
                Assert.Equal("http://127.0.0.1:5199/dotcraft/bindings/binding-1/mcp", parameters.GetProperty("endpoint").GetString());
                Assert.False(string.IsNullOrWhiteSpace(parameters.GetProperty("bearer").GetString()));
                await transport.PushResultAsync(rebind, new { state = "active", authorityRevision = 7 });
            }

            await rebindTask.WaitAsync(Timeout);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static object ThreadStartResult(string threadId, string workspacePath, string effectiveWorkspacePath) =>
        new
        {
            thread = new
            {
                id = threadId,
                sessionId = $"session-{threadId}",
                workspacePath,
                cwd = effectiveWorkspacePath,
                runtimeWorkspaceRoots = new[] { workspacePath },
                effectiveWorkspacePath,
                ephemeral = false,
                worktree = new { path = effectiveWorkspacePath },
                originChannel = "oratorio",
                status = "idle",
                source = new { kind = "user" },
                createdAt = "2026-01-01T00:00:00Z",
                lastActiveAt = "2026-01-01T00:00:00Z",
                historyMode = "none",
                metadata = new Dictionary<string, string>(),
                runtime = new { },
                queuedInputs = Array.Empty<object>()
            }
        };

    private static async Task<DotCraftClient> ConnectSdkClientAsync(
        TestJsonRpcTransport transport,
        DotCraftClientOptions? options = null,
        Action<JsonElement>? inspectInitialize = null)
    {
        var connectTask = DotCraftClient.ConnectAsync(
            transport,
            options ?? new DotCraftClientOptions { ClientName = "oratorio-test", ClientVersion = "1" });

        using (var initialize = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            inspectInitialize?.Invoke(initialize.RootElement);
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = initialize.RootElement.GetProperty("id").GetInt64(),
                result = new
                {
                    serverInfo = new { name = "dotcraft", version = "1", protocolVersion = "1" },
                    capabilities = new { appBinding = true, appBindingVersion = 2 }
                }
            });
        }

        using (var initialized = await transport.ReadOutboundAsync().WaitAsync(Timeout))
        {
            Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
        }

        return await connectTask.WaitAsync(Timeout);
    }

    private static string BuildHandoff(
        string operation,
        string requestId,
        string workspacePath,
        string runtimeIdentity,
        string? endpoint = "ws://127.0.0.1:9100/ws")
    {
        var query = $"app=com.dotharness.oratorio&request={Uri.EscapeDataString(requestId)}&token=request-token" +
                    $"&workspace={Uri.EscapeDataString(workspacePath)}&identity={Uri.EscapeDataString(runtimeIdentity)}";
        if (endpoint is not null) query += $"&endpoint={Uri.EscapeDataString(endpoint)}";
        return $"oratorio://dotcraft/{operation}?{query}";
    }

    private sealed class SingleClientFactory(IDotCraftAppServerClient client) : IDotCraftAppServerClientFactory
    {
        public string? LastAppServerUrl { get; private set; }
        public string? LastToken { get; private set; }

        public Task<IDotCraftAppServerClient> ConnectAsync(string appServerUrl, CancellationToken ct, string? token = null)
        {
            LastAppServerUrl = appServerUrl;
            LastToken = token;
            return Task.FromResult(client);
        }
    }

    private sealed class PassthroughSecretProtector : IConfigurationSecretProtector
    {
        public bool IsProtected(string? value) => false;
        public string Protect(string value) => value;
        public string? Unprotect(string? value) => value;
    }

    private sealed class TestJsonRpcTransport : IJsonRpcTransport
    {
        private readonly Channel<JsonDocument> _inbound = Channel.CreateUnbounded<JsonDocument>();
        private readonly Channel<JsonDocument> _outbound = Channel.CreateUnbounded<JsonDocument>();

        public Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default) =>
            ReadNullableAsync(_inbound.Reader, cancellationToken);

        public Task WriteAsync(object message, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(message, DotCraftJson.Options);
            _outbound.Writer.TryWrite(JsonDocument.Parse(json));
            return Task.CompletedTask;
        }

        public Task PushInboundAsync(object message)
        {
            var json = JsonSerializer.Serialize(message, DotCraftJson.Options);
            _inbound.Writer.TryWrite(JsonDocument.Parse(json));
            return Task.CompletedTask;
        }

        public Task PushResultAsync(JsonDocument request, object result) =>
            PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = request.RootElement.GetProperty("id").GetInt64(),
                result
            });

        public Task<JsonDocument> ReadOutboundAsync(CancellationToken cancellationToken = default) =>
            _outbound.Reader.ReadAsync(cancellationToken).AsTask();

        public ValueTask DisposeAsync()
        {
            _inbound.Writer.TryComplete();
            _outbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private static async Task<JsonDocument?> ReadNullableAsync(
            ChannelReader<JsonDocument> reader,
            CancellationToken cancellationToken)
        {
            try
            {
                return await reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }
}
