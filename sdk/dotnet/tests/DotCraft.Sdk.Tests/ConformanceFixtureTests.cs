using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class ConformanceFixtureTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeUsesContractsShape()
    {
        await using var transport = new TestJsonRpcTransport();
        var connect = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "dotcraft-dotnet-test", ClientTitle = "DotCraft .NET Test", ClientVersion = "0.1.0"
        });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.Equal("initialize", outbound.RootElement.GetProperty("method").GetString());
            Assert.Equal("dotcraft-dotnet-test", parameters.GetProperty("clientInfo").GetProperty("name").GetString());
            await RespondInitializeAsync(transport, outbound.RootElement.GetProperty("id").GetInt64());
        }
        using (await transport.ReadOutboundAsync())
        {
        }
        await using var client = await connect;
        Assert.True(client.Capabilities.DynamicToolRebind);
    }

    [Fact]
    public async Task ThreadAndTurnRequestsUseContractsDtos()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var start = client.Threads.StartAsync(new ThreadStartParams
        {
            Identity = new SessionIdentity
            {
                ChannelName = "vscode", UserId = "user-123", WorkspacePath = "C:/workspace", ChannelContext = "workspace:C:/workspace"
            },
            HistoryMode = "server",
            DynamicTools =
            [
                new RuntimeDynamicToolNamespace
                {
                    Name = "sampleboard", Description = "Sample board tools.", Tools =
                    [
                        new RuntimeDynamicToolFunction
                        {
                            Name = "GetBoardItem", Description = "Read one item.", InputSchema = schema, DeferLoading = true,
                            Approval = new ToolApprovalDescriptor { Kind = "remoteResource", TargetArgument = "itemId" }
                        }
                    ]
                }
            ]
        });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            var parameters = outbound.RootElement.GetProperty("params");
            Assert.Equal("vscode", parameters.GetProperty("identity").GetProperty("channelName").GetString());
            Assert.Equal("function", parameters.GetProperty("dynamicTools")[0].GetProperty("tools")[0].GetProperty("type").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new ThreadStartResult { Thread = Thread(), InstructionSources = [] }
            });
        }
        Assert.Equal("thread_1", (await start).Id);

        var turn = client.Turns.StartAsync(new TurnStartParams
        {
            ThreadId = "thread_1", Input = [new InputPart { Type = "text", Text = "Run tests" }]
        });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("turn/start", outbound.RootElement.GetProperty("method").GetString());
            Assert.Equal("Run tests", outbound.RootElement.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new TurnStartResult { Turn = Turn() }
            });
        }
        Assert.Equal("turn_1", (await turn).Turn.Id);
    }

    [Fact]
    public async Task McpRuntimeMethodsUseGeneratedDescriptorsAndContractsResults()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = await ConnectInitializedAsync(transport);
        var status = client.McpRuntime.ListStatusAsync(new McpServerStatusListParams
        {
            ThreadId = "thread_1", Cursor = "2", Limit = 25, Detail = "full"
        });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("mcpServerStatus/list", outbound.RootElement.GetProperty("method").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = outbound.RootElement.GetProperty("id").GetInt64(), result = new McpServerStatusListResult
                {
                    Data = new List<McpServerRuntimeStatus> { new() { RuntimeName = "docs", Name = "docs" } },
                    NextCursor = null
                }
            });
        }
        Assert.Equal("docs", Assert.Single((await status).Data.Value!).RuntimeName.Value);

        var resource = client.McpRuntime.ReadResourceAsync(new McpServerResourceReadParams
        {
            Server = "docs", Uri = "docs://intro", ThreadId = "thread_1"
        });
        var contents = JsonSerializer.SerializeToElement(new[] { new { uri = "docs://intro" } });
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("mcpServer/resource/read", outbound.RootElement.GetProperty("method").GetString());
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new McpServerResourceReadResult { Contents = contents }
            });
        }
        Assert.Equal("docs://intro", (await resource).Contents.Value!.Value[0].GetProperty("uri").GetString());
    }

    private static async Task<DotCraftClient> ConnectInitializedAsync(TestJsonRpcTransport transport)
    {
        var connect = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions { ClientName = "test", ClientVersion = "0.1" });
        using (var outbound = await transport.ReadOutboundAsync())
            await RespondInitializeAsync(transport, outbound.RootElement.GetProperty("id").GetInt64());
        using (await transport.ReadOutboundAsync())
        {
        }
        return await connect;
    }

    private static Task RespondInitializeAsync(TestJsonRpcTransport transport, long id) =>
        transport.PushInboundAsync(new
        {
            jsonrpc = "2.0", id, result = new InitializeResult
            {
                ServerInfo = new ServerInfo { Name = "dotcraft", Version = "test", ProtocolVersion = "1" },
                Capabilities = new ServerCapabilities
                {
                    ThreadManagement = true, ThreadSubscriptions = true, DynamicToolRebind = true
                }
            }
        });

    private static SessionThread Thread() => new()
    {
        Id = "thread_1", SessionId = "session_1", WorkspacePath = "C:/workspace", Cwd = "C:/workspace",
        RuntimeWorkspaceRoots = ["C:/workspace"], EffectiveWorkspacePath = "C:/workspace", Ephemeral = false, Worktree = null,
        OriginChannel = "vscode", Status = "active", Source = new ThreadSource { Kind = "user" }, CreatedAt = Now,
        LastActiveAt = Now, HistoryMode = "server", Configuration = new ThreadConfiguration(), Metadata = new Dictionary<string, string>(),
        Runtime = new ThreadRuntimeState { Busy = false, Running = false }, QueuedInputs = []
    };

    private static SessionTurn Turn() => new()
    {
        Id = "turn_1", ThreadId = "thread_1", Status = "inProgress", StartedAt = Now, Items = []
    };
}
