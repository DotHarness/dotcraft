using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireDynamicToolProxyTests
{
    [Fact]
    public async Task RuntimeDynamicTool_DispatchesItemToolCallAndEmitsDynamicToolCallItem()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "draft submitted" }],
            StructuredResult = JsonNode.Parse("""{"reviewId":"r1"}""")
        });
        var connection = new AppServerConnection();
        var spec = CreateReviewToolSpec();

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var started = new List<SessionItem>();
        var completed = new List<SessionItem>();
        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = started.Add,
            EmitItemCompleted = completed.Add
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["body"] = "Looks good."
        }));

        Assert.Equal(AppServerMethods.ItemToolCall, transport.Method);
        var request = Assert.IsType<DynamicToolCallParams>(transport.Params);
        Assert.Equal(thread.Id, request.ThreadId);
        Assert.Equal(turn.Id, request.TurnId);
        Assert.Equal("SubmitReviewDraft", request.Tool);
        Assert.Equal("Looks good.", request.Arguments["body"]?.GetValue<string>());

        var item = Assert.Single(turn.Items);
        Assert.Same(item, Assert.Single(started));
        Assert.Same(item, Assert.Single(completed));
        Assert.Equal(ItemType.DynamicToolCall, item.Type);
        Assert.Equal(ItemStatus.Completed, item.Status);

        var payload = Assert.IsType<DynamicToolCallPayload>(item.Payload);
        Assert.True(payload.Success);
        Assert.Equal("SubmitReviewDraft", payload.ToolName);
        Assert.Equal("draft submitted", Assert.Single(payload.ContentItems!).Text);
        Assert.Equal("r1", payload.StructuredResult?["reviewId"]?.GetValue<string>());
    }

    [Fact]
    public async Task BindThread_RebindReplacesOldTransportBinding()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var oldTransport = new RecordingTransport(new DynamicToolCallResult { Success = true });
        var oldConnection = new AppServerConnection();
        var newTransport = new RecordingTransport(new DynamicToolCallResult
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "new binding" }]
        });
        var newConnection = new AppServerConnection();
        var spec = CreateReviewToolSpec();

        proxy.BindThread(thread.Id, oldTransport, oldConnection, [spec]);
        oldConnection.MarkClosed();
        proxy.BindThread(thread.Id, newTransport, newConnection, [spec]);
        proxy.UnbindTransport(oldTransport);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["body"] = "Looks good."
        }));

        Assert.Null(oldTransport.Method);
        Assert.Equal(AppServerMethods.ItemToolCall, newTransport.Method);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.True(payload.Success);
        Assert.Equal("new binding", Assert.Single(payload.ContentItems!).Text);
    }

    [Fact]
    public async Task RuntimeDynamicTool_ApprovalRejectionBlocksClientDispatch()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult { Success = true });
        var connection = new AppServerConnection();
        var spec = CreateReviewToolSpec(new ChannelToolApprovalDescriptor
        {
            Kind = "remoteResource",
            TargetArgument = "body",
            Operation = "submitReviewDraft"
        });

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new RejectingApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["body"] = "Needs work."
        }));

        Assert.Null(transport.Method);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.False(payload.Success);
        Assert.Equal("AccessDenied", payload.ErrorCode);
    }

    [Fact]
    public async Task RuntimeDynamicTool_OptionalApprovalTargetMissing_DispatchesWithoutApproval()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult
        {
            Success = true,
            ContentItems = [new ExtChannelToolContentItem { Type = "text", Text = "done" }]
        });
        var connection = new AppServerConnection();
        var spec = CreateOptionalApprovalToolSpec();

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new RejectingApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["url"] = "https://example.test/report.pdf"
        }));

        Assert.Equal(AppServerMethods.ItemToolCall, transport.Method);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.True(payload.Success);
    }

    [Fact]
    public async Task RuntimeDynamicTool_OptionalApprovalTargetPresent_BlocksClientDispatchWhenRejected()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult { Success = true });
        var connection = new AppServerConnection();
        var spec = CreateOptionalApprovalToolSpec();

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new RejectingApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["url"] = "https://example.test/report.pdf",
            ["resource"] = "https://example.test/report.pdf"
        }));

        Assert.Null(transport.Method);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.False(payload.Success);
        Assert.Equal("AccessDenied", payload.ErrorCode);
    }

    [Fact]
    public async Task RuntimeDynamicTool_InterruptApprovalPolicyReturnsAccessDeniedWithoutCancellingTurn()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult { Success = true });
        var connection = new AppServerConnection();
        var spec = CreateReviewToolSpec(new ChannelToolApprovalDescriptor
        {
            Kind = "remoteResource",
            TargetArgument = "body",
            Operation = "submitReviewDraft"
        });

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new InterruptOnApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["body"] = "Needs work."
        }));

        Assert.Null(transport.Method);
        Assert.Equal(TurnStatus.Running, turn.Status);
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.False(payload.Success);
        Assert.Equal("AccessDenied", payload.ErrorCode);
        Assert.Contains("rejected", payload.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeDynamicTool_CarriesUiMetaToPayloadButHidesItFromModel()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var turn = CreateTurn(thread.Id);
        var transport = new RecordingTransport(new DynamicToolCallResult
        {
            Success = true,
            StructuredResult = JsonNode.Parse("""{"reviewId":"r1"}"""),
            Meta = JsonNode.Parse("""{"ui":{"badge":"secret-ui-only"}}""")
        });
        var connection = new AppServerConnection();
        var spec = CreateReviewToolSpec();

        proxy.BindThread(thread.Id, transport, connection, [spec]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(proxy.CreateToolsForThread(thread, EmptyReservedNames())));

        var seq = 0;
        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = thread.Id,
            TurnId = turn.Id,
            OriginChannel = "appserver",
            WorkspacePath = Environment.CurrentDirectory,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            Turn = turn,
            NextItemSequence = () => ++seq,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

        var modelValue = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["body"] = "Looks good."
        }));

        // _meta is carried on the stored item (host/UI surface)…
        var payload = Assert.IsType<DynamicToolCallPayload>(Assert.Single(turn.Items).Payload);
        Assert.Equal("secret-ui-only", payload.Meta?["ui"]?["badge"]?.GetValue<string>());

        // …but never leaks into the model-visible value.
        var modelJson = JsonSerializer.Serialize(modelValue, SessionWireJsonOptions.Default);
        Assert.DoesNotContain("secret-ui-only", modelJson, StringComparison.Ordinal);
        Assert.DoesNotContain("_meta", modelJson, StringComparison.Ordinal);
        Assert.Contains("r1", modelJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToolsForThread_ExcludesAppOnlyTools()
    {
        var proxy = new WireDynamicToolProxy();
        var thread = CreateThread();
        var modelTool = CreateReviewToolSpec();
        modelTool.Meta = new DynamicToolMeta
        {
            Ui = new UiToolMeta
            {
                ResourceUri = "ui://workflow/board",
                Visibility = ["model", "app"]
            }
        };
        var appOnlyTool = new DynamicToolSpec
        {
            Name = "RefreshBoard",
            Description = "Refresh the board (UI-only)",
            InputSchema = new JsonObject { ["type"] = "object" },
            Meta = new DynamicToolMeta
            {
                Ui = new UiToolMeta
                {
                    ResourceUri = "ui://workflow/board",
                    Visibility = ["app"]
                }
            }
        };

        proxy.BindThread(thread.Id, new RecordingTransport(new DynamicToolCallResult { Success = true }), new AppServerConnection(), [modelTool, appOnlyTool]);

        var tools = proxy.CreateToolsForThread(thread, EmptyReservedNames());
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools));
        Assert.Equal("SubmitReviewDraft", tool.Name);
    }

    [Fact]
    public void TryValidateSpecs_RejectsInvalidApprovalMetadata()
    {
        var spec = CreateReviewToolSpec(new ChannelToolApprovalDescriptor
        {
            Kind = "remoteResource",
            TargetArgument = "missing",
            Operation = "submitReviewDraft"
        });

        Assert.False(WireDynamicToolProxy.TryValidateSpecs([spec], out var message));
        Assert.Contains("approval references unknown property 'missing'", message);
    }

    private static SessionThread CreateThread()
        => new()
        {
            Id = "thread_test",
            WorkspacePath = Environment.CurrentDirectory,
            OriginChannel = "appserver",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = new ThreadConfiguration()
        };

    private static IReadOnlySet<string> EmptyReservedNames()
        => new HashSet<string>(StringComparer.Ordinal);

    private static SessionTurn CreateTurn(string threadId)
        => new()
        {
            Id = "turn_001",
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

    private static DynamicToolSpec CreateReviewToolSpec(ChannelToolApprovalDescriptor? approval = null)
        => new()
        {
            Name = "SubmitReviewDraft",
            Description = "Submit a structured code review draft",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["body"] = new JsonObject { ["type"] = "string" }
                },
                ["required"] = new JsonArray("body")
            },
            Approval = approval
        };

    private static DynamicToolSpec CreateOptionalApprovalToolSpec()
        => new()
        {
            Name = "SendRemoteReport",
            Description = "Send a report from either a URL or an approval-gated resource.",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["url"] = new JsonObject { ["type"] = "string" },
                    ["resource"] = new JsonObject { ["type"] = "string" }
                }
            },
            Approval = new ChannelToolApprovalDescriptor
            {
                Kind = "remoteResource",
                TargetArgument = "resource",
                Operation = "fetch"
            }
        };

    private sealed class RecordingTransport(object result) : IAppServerTransport
    {
        public string? Method { get; private set; }

        public object? Params { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default)
            => Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Method = method;
            Params = @params;
            return Task.FromResult(new AppServerIncomingMessage
            {
                Id = JsonSerializer.SerializeToElement("request-1"),
                Result = JsonSerializer.SerializeToElement(result, SessionWireJsonOptions.Default)
            });
        }
    }

    private sealed class RejectingApprovalService : IApprovalService
    {
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
            => Task.FromResult(false);

        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
            => Task.FromResult(false);

        public Task<bool> RequestResourceApprovalAsync(
            string kind,
            string operation,
            string target,
            ApprovalContext? context = null)
            => Task.FromResult(false);
    }
}
