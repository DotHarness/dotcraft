using System.Text.Json;
using DotCraft.Sdk.AppServer;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Tests;

public sealed class ConformanceFixtureTests
{
    [Fact]
    public async Task Initialize_UsesAppServerSpecShape()
    {
        await using var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "dotcraft-dotnet-test",
            ClientTitle = "DotCraft .NET Test",
            ClientVersion = "0.1.0"
        });

        using var initialize = await transport.ReadOutboundAsync();
        var root = initialize.RootElement;
        Assert.Equal("initialize", root.GetProperty("method").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal("dotcraft-dotnet-test", parameters.GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.Equal("DotCraft .NET Test", parameters.GetProperty("clientInfo").GetProperty("title").GetString());
        Assert.False(parameters.GetProperty("capabilities").GetProperty("approvalSupport").GetBoolean());
        Assert.True(parameters.GetProperty("capabilities").GetProperty("streamingSupport").GetBoolean());

        await RespondToInitializeAsync(transport, root.GetProperty("id").GetInt64());
        using var initialized = await transport.ReadOutboundAsync();
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());

        await using var client = await connectTask;
        Assert.True(client.Capabilities.DynamicToolRebind);
    }

    [Fact]
    public async Task ThreadStart_UsesSpecIdentityAndDynamicToolShape()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);
        var inputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                itemId = new { type = "string" }
            },
            required = new[] { "itemId" }
        }, DotCraftJson.Options);

        var startTask = client.Threads.StartAsync(new DotCraftThreadStartRequest(
            new SessionIdentity(
                "vscode",
                "user-123",
                "/home/dev/myproject",
                "workspace:/home/dev/myproject"),
            HistoryMode: "server",
            DynamicTools:
            [
                new RuntimeDynamicToolNamespace(
                    "sampleboard",
                    "Sample board tools.",
                    [
                        new RuntimeDynamicToolFunction(
                            "GetBoardItem",
                            "Read one sample board item.",
                            inputSchema,
                            DeferLoading: true,
                            Approval: new ToolApprovalDescriptor("remoteResource", "itemId"))
                    ])
            ]));

        using var outbound = await transport.ReadOutboundAsync();
        var root = outbound.RootElement;
        Assert.Equal("thread/start", root.GetProperty("method").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal("vscode", parameters.GetProperty("identity").GetProperty("channelName").GetString());
        Assert.Equal("/home/dev/myproject", parameters.GetProperty("identity").GetProperty("workspacePath").GetString());
        Assert.Equal("server", parameters.GetProperty("historyMode").GetString());
        var toolNamespace = parameters.GetProperty("dynamicTools")[0];
        Assert.Equal("namespace", toolNamespace.GetProperty("type").GetString());
        Assert.Equal("sampleboard", toolNamespace.GetProperty("name").GetString());
        var tool = toolNamespace.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("GetBoardItem", tool.GetProperty("name").GetString());
        Assert.True(tool.GetProperty("deferLoading").GetBoolean());
        Assert.Equal("remoteResource", tool.GetProperty("approval").GetProperty("kind").GetString());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = root.GetProperty("id").GetInt64(),
            result = new
            {
                thread = new
                {
                    id = "thread_20260316_x7k2m4",
                    status = "active",
                    workspacePath = "/home/dev/myproject",
                    turns = Array.Empty<object>()
                }
            }
        });

        var thread = await startTask;
        Assert.Equal("thread_20260316_x7k2m4", thread.Id);
    }

    [Fact]
    public async Task TurnStart_UsesSpecInputAndReadsTurnId()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);

        var startTask = client.Turns.StartAsync(
            "thread_20260316_x7k2m4",
            [new TurnInputPart("text", "Run the tests and fix any failures")]);

        using var outbound = await transport.ReadOutboundAsync();
        var root = outbound.RootElement;
        Assert.Equal("turn/start", root.GetProperty("method").GetString());
        var parameters = root.GetProperty("params");
        Assert.Equal("thread_20260316_x7k2m4", parameters.GetProperty("threadId").GetString());
        Assert.Equal("text", parameters.GetProperty("input")[0].GetProperty("type").GetString());
        Assert.Equal("Run the tests and fix any failures", parameters.GetProperty("input")[0].GetProperty("text").GetString());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = root.GetProperty("id").GetInt64(),
            result = new
            {
                turn = new
                {
                    id = "turn_001",
                    threadId = "thread_20260316_x7k2m4",
                    status = "running",
                    items = Array.Empty<object>()
                }
            }
        });

        var turn = await startTask;
        Assert.Equal("turn_001", turn.TurnId);
    }

    [Fact]
    public async Task AppBindingActivate_UsesSpecRpcMethod()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);

        var activateTask = client.AppBindings.ActivateAsync(
            "bind_req_1", "https://app.example/mcp", "one-time-bearer");

        using var outbound = await transport.ReadOutboundAsync();
        var root = outbound.RootElement;
        Assert.Equal("app/binding/activate", root.GetProperty("method").GetString());
        Assert.Equal("bind_req_1", root.GetProperty("params").GetProperty("bindingRequestId").GetString());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = root.GetProperty("id").GetInt64(),
            result = new
            {
                bindingId = "binding_1",
                threadId = "thread_1",
                appId = "com.example.board",
                state = "active"
            }
        });

        var activated = await activateTask;
        Assert.Equal("binding_1", activated.GetProperty("bindingId").GetString());
    }

    [Fact]
    public async Task McpRuntime_UsesSpecifiedMethodsAndTypedResults()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);

        var statusTask = client.McpRuntime.ListStatusAsync(
            new McpServerStatusListParams("thread_1", "2", 25, "full"));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var root = outbound.RootElement;
            Assert.Equal("mcpServerStatus/list", root.GetProperty("method").GetString());
            Assert.Equal("thread_1", root.GetProperty("params").GetProperty("threadId").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = root.GetProperty("id").GetInt64(),
                result = new
                {
                    data = new[] { new
                    {
                        name = "docs", serverInfo = new { name = "Docs", version = "1" },
                        tools = new Dictionary<string, object> { ["search"] = new { name = "search" } },
                        resources = Array.Empty<object>(), resourceTemplates = Array.Empty<object>(),
                        authStatus = "oAuth", declaredName = "docs", runtimeName = "docs"
                    } },
                    nextCursor = (string?)null
                }
            });
        }
        Assert.Equal("docs", Assert.Single((await statusTask).Data).RuntimeName);

        var resourceTask = client.McpRuntime.ReadResourceAsync(
            new McpServerResourceReadParams("docs", "docs://intro", "thread_1"));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var root = outbound.RootElement;
            Assert.Equal("mcpServer/resource/read", root.GetProperty("method").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = root.GetProperty("id").GetInt64(),
                result = new { contents = new[] { new { uri = "docs://intro" } } }
            });
        }
        Assert.Equal("docs://intro", (await resourceTask).Contents[0].GetProperty("uri").GetString());

        var toolTask = client.McpRuntime.CallToolAsync(new McpServerToolCallParams(
            "thread_1", "docs", "search",
            new Dictionary<string, object?> { ["query"] = "MCP" },
            JsonSerializer.SerializeToElement(new { trace = "t1" })));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var root = outbound.RootElement;
            Assert.Equal("mcpServer/tool/call", root.GetProperty("method").GetString());
            Assert.Equal("t1", root.GetProperty("params").GetProperty("_meta").GetProperty("trace").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = root.GetProperty("id").GetInt64(),
                result = new
                {
                    content = new[] { new { type = "text", text = "found" } },
                    structuredContent = new { count = 1 }, isError = false,
                    _meta = new { source = "docs" }
                }
            });
        }
        Assert.Equal(1, (await toolTask).StructuredContent?.GetProperty("count").GetInt32());

        var loginTask = client.McpRuntime.LoginOAuthAsync(
            new McpServerOAuthLoginParams("docs", "thread_1", ["read"], 60));
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var root = outbound.RootElement;
            Assert.Equal("mcpServer/oauth/login", root.GetProperty("method").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = root.GetProperty("id").GetInt64(),
                result = new { authorizationUrl = "https://auth.example/" }
            });
        }
        Assert.Equal("https://auth.example/", (await loginTask).AuthorizationUrl);

        var reloadTask = client.McpRuntime.ReloadAsync();
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var root = outbound.RootElement;
            Assert.Equal("config/mcpServer/reload", root.GetProperty("method").GetString());
            Assert.False(root.TryGetProperty("params", out _));
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = root.GetProperty("id").GetInt64(),
                result = new { }
            });
        }
        Assert.NotNull(await reloadTask);
    }

    private static async Task<DotCraftClient> ConnectInitializedAsync(TestJsonRpcTransport transport)
    {
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "conformance-test",
            ClientVersion = "0.1.0"
        });

        using var initialize = await transport.ReadOutboundAsync();
        await RespondToInitializeAsync(transport, initialize.RootElement.GetProperty("id").GetInt64());
        using var initialized = await transport.ReadOutboundAsync();
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
        return await connectTask;
    }

    private static Task RespondToInitializeAsync(TestJsonRpcTransport transport, long id) =>
        transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                serverInfo = new
                {
                    name = "dotcraft",
                    version = "test",
                    protocolVersion = "1",
                    extensions = Array.Empty<string>()
                },
                capabilities = new
                {
                    threadManagement = true,
                    threadSubscriptions = true,
                    dynamicToolRebind = true,
                    appBinding = true,
                    modelCatalogManagement = true
                }
            }
        });
}
