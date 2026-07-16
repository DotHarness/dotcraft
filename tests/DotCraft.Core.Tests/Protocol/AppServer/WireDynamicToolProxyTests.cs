using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireDynamicToolProxyTests
{
    [Fact]
    public async Task Dispatcher_SendsOnlyCallbackWithOriginalProviderCallId()
    {
        var proxy = new WireDynamicToolProxy();
        var transport = SuccessTransport("draft submitted", new JsonObject { ["reviewId"] = "r1" });
        proxy.BindThread("thread_test", transport, new AppServerConnection(), [CreateReviewToolSpec()]);
        var snapshot = await BuildSnapshotAsync(proxy);
        var definition = Assert.Single(snapshot.ModelVisibleDefinitions);

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            snapshot.ProviderFlatNames[definition.Name],
            new JsonObject { ["body"] = "Looks good." },
            new ToolInvocationRequest(
                "thread_test",
                "turn_001",
                "provider-call-42",
                ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Equal("draft submitted", result.Content);
        Assert.Equal("r1", result.StructuredContent?.GetProperty("reviewId").GetString());
        Assert.Equal(AppServerMethods.ItemToolCall, transport.Method);
        var request = Assert.IsType<DynamicToolCallParams>(transport.Params);
        Assert.Equal("provider-call-42", request.CallId);
        Assert.Equal("turn_001", request.TurnId);
        Assert.Equal("SubmitReviewDraft", request.Tool);
        Assert.Equal("Looks good.", request.Arguments["body"]?.GetValue<string>());
    }

    [Fact]
    public async Task Dispatcher_PreservesDynamicImageForTheModel()
    {
        var imageBytes = "dynamic-image"u8.ToArray();
        var proxy = new WireDynamicToolProxy();
        var transport = new RecordingTransport(new RuntimeDynamicToolCallResult
        {
            Success = true,
            ContentItems =
            [
                new RuntimeDynamicToolContentItem { Type = "text", Text = "captured" },
                new RuntimeDynamicToolContentItem
                {
                    Type = "image",
                    MediaType = "image/png",
                    DataBase64 = Convert.ToBase64String(imageBytes)
                }
            ]
        });
        proxy.BindThread("thread_test", transport, new AppServerConnection(), [CreateReviewToolSpec()]);
        var snapshot = await BuildSnapshotAsync(proxy);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, "SubmitReviewDraft"),
            new JsonObject { ["body"] = "capture" },
            new ToolInvocationRequest("thread_test", "turn_001", "call_image", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        var contentItems = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result.ContentItems);
        Assert.Equal("captured", Assert.IsType<TextContent>(contentItems[0]).Text);
        var image = Assert.IsType<DataContent>(contentItems[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(imageBytes, image.Data.ToArray());
    }

    [Fact]
    public async Task Replacement_InvalidatesOldSnapshotLeaseAndUsesNewOwnerGeneration()
    {
        var proxy = new WireDynamicToolProxy();
        var oldTransport = SuccessTransport("old");
        var newTransport = SuccessTransport("new");
        proxy.BindThread("thread_test", oldTransport, new AppServerConnection(), [CreateReviewToolSpec()]);
        var oldSnapshot = await BuildSnapshotAsync(proxy);
        proxy.BindThread("thread_test", newTransport, new AppServerConnection(), [CreateReviewToolSpec()]);
        var newSnapshot = await BuildSnapshotAsync(proxy, revision: 2);
        var toolName = new ToolName(null, "SubmitReviewDraft");
        var request = new ToolInvocationRequest(
            "thread_test", "turn_001", "call_replace", ToolInvocationAudience.Model);

        var staleResult = await new ToolDispatcher().DispatchAsync(
            oldSnapshot, toolName, new JsonObject { ["body"] = "old" }, request);
        var currentResult = await new ToolDispatcher().DispatchAsync(
            newSnapshot, toolName, new JsonObject { ["body"] = "new" }, request);

        Assert.False(staleResult.Success);
        Assert.Equal(ToolErrorCodes.DynamicDisconnected, staleResult.Error?.Code);
        Assert.Null(oldTransport.Method);
        Assert.True(currentResult.Success);
        Assert.Equal("new", currentResult.Content);
        Assert.Equal(AppServerMethods.ItemToolCall, newTransport.Method);
    }

    [Fact]
    public async Task NullIsNoChangeAndEmptyClearsOnlyOwningConnection()
    {
        var proxy = new WireDynamicToolProxy();
        var owner = new AppServerConnection();
        var other = new AppServerConnection();
        var transport = SuccessTransport("ok");
        proxy.BindThread("thread_test", transport, owner, [CreateReviewToolSpec()]);

        proxy.BindThread("thread_test", transport, other, null);
        Assert.Single((await BuildSnapshotAsync(proxy)).Registrations);
        proxy.BindThread("thread_test", transport, other, []);
        Assert.Single((await BuildSnapshotAsync(proxy)).Registrations);
        proxy.BindThread("thread_test", transport, owner, []);
        Assert.Empty((await BuildSnapshotAsync(proxy)).Registrations);
    }

    [Fact]
    public async Task DisconnectImmediatelyBlocksFrozenSnapshot()
    {
        var proxy = new WireDynamicToolProxy();
        var transport = SuccessTransport("ok");
        var connection = new AppServerConnection();
        proxy.BindThread("thread_test", transport, connection, [CreateReviewToolSpec()]);
        var snapshot = await BuildSnapshotAsync(proxy);
        connection.MarkClosed();

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, "SubmitReviewDraft"),
            new JsonObject { ["body"] = "test" },
            new ToolInvocationRequest(
                "thread_test", "turn_001", "call_disconnect", ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.DynamicDisconnected, result.Error?.Code);
        Assert.Null(transport.Method);
    }

    [Fact]
    public async Task ApprovalHintIsHandledByCommonDispatcherBeforeCallback()
    {
        var proxy = new WireDynamicToolProxy();
        var transport = SuccessTransport("unexpected");
        proxy.BindThread(
            "thread_test",
            transport,
            new AppServerConnection(),
            [CreateReviewToolSpec(new ChannelToolApprovalDescriptor
            {
                Kind = "remoteResource",
                TargetArgument = "body",
                Operation = "submitReviewDraft"
            })]);
        var snapshot = await BuildSnapshotAsync(proxy);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, "SubmitReviewDraft"),
            new JsonObject { ["body"] = "Needs work." },
            new ToolInvocationRequest(
                "thread_test", "turn_001", "call_approval", ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ApprovalRejected, result.Error?.Code);
        Assert.Null(transport.Method);
    }

    [Fact]
    public async Task InvalidCallbackResultMapsToStableProtocolError()
    {
        var proxy = new WireDynamicToolProxy();
        var transport = new RecordingTransport(new RuntimeDynamicToolCallResult
        {
            Success = true,
            StructuredContent = new JsonObject { ["only"] = "structured" }
        });
        proxy.BindThread("thread_test", transport, new AppServerConnection(), [CreateReviewToolSpec()]);
        var snapshot = await BuildSnapshotAsync(proxy);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, "SubmitReviewDraft"),
            new JsonObject { ["body"] = "test" },
            new ToolInvocationRequest(
                "thread_test", "turn_001", "call_invalid", ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ResultInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task NamespacedDeclarationsAllowSameLocalNameAndDeferredDiscovery()
    {
        RuntimeDynamicToolNamespace CreateNamespace(string name, bool deferred) => new()
        {
            Name = name,
            Description = $"{name} tools.",
            Tools =
            [
                new RuntimeDynamicToolFunction
                {
                    Name = "RefreshBoard",
                    Description = "Refresh the board.",
                    InputSchema = new JsonObject { ["type"] = "object" },
                    DeferLoading = deferred
                }
            ]
        };
        var declarations = new RuntimeDynamicToolDeclaration[]
        {
            CreateNamespace("desktop", false),
            CreateNamespace("sampleboard", true)
        };
        Assert.True(WireDynamicToolProxy.TryValidateSpecs(declarations, out var message), message);
        var proxy = new WireDynamicToolProxy();
        proxy.BindThread("thread_test", SuccessTransport("ok"), new AppServerConnection(), declarations);

        var snapshot = await BuildSnapshotAsync(proxy);

        Assert.Equal(2, snapshot.Registrations.Count);
        Assert.Equal(ToolExposure.Direct, snapshot.Registrations[new ToolName("desktop", "RefreshBoard")].Exposure);
        var deferred = snapshot.Registrations[new ToolName("sampleboard", "RefreshBoard")];
        Assert.Equal(ToolExposure.Deferred, deferred.Exposure);
        Assert.Equal("sampleboard tools.", deferred.Definition.NamespaceDescription);
        Assert.Equal("sampleboard tools.", deferred.Deferred?.NamespaceDescription);
    }

    [Fact]
    public void ValidationRejectsDeferredTopLevelAndInvalidApprovalMetadata()
    {
        var deferred = CreateReviewToolSpec();
        deferred.DeferLoading = true;
        Assert.False(WireDynamicToolProxy.TryValidateSpecs([deferred], out var deferredMessage));
        Assert.Contains("cannot set deferLoading=true", deferredMessage, StringComparison.Ordinal);

        var invalidApproval = CreateReviewToolSpec(new ChannelToolApprovalDescriptor
        {
            Kind = "remoteResource",
            TargetArgument = "missing",
            Operation = "submitReviewDraft"
        });
        Assert.False(WireDynamicToolProxy.TryValidateSpecs([invalidApproval], out var approvalMessage));
        Assert.Contains("approval references unknown property 'missing'", approvalMessage);
    }

    [Fact]
    public async Task InvalidReplacementIsAtomicAndKeepsPreviousBinding()
    {
        var proxy = new WireDynamicToolProxy();
        var transport = SuccessTransport("old binding");
        var owner = new AppServerConnection();
        proxy.BindThread("thread_test", transport, owner, [CreateReviewToolSpec()]);
        var invalid = CreateReviewToolSpec();
        invalid.Description = string.Empty;

        Assert.Throws<ArgumentException>(() =>
            proxy.BindThread(
                "thread_test",
                SuccessTransport("invalid replacement"),
                new AppServerConnection(),
                [invalid]));

        var snapshot = await BuildSnapshotAsync(proxy);
        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName(null, "SubmitReviewDraft"),
            new JsonObject { ["body"] = "still old" },
            new ToolInvocationRequest(
                "thread_test", "turn_001", "call_atomic", ToolInvocationAudience.Model));
        Assert.True(result.Success);
        Assert.Equal("old binding", result.Content);
    }

    private static async Task<EffectiveToolSnapshot> BuildSnapshotAsync(
        WireDynamicToolProxy proxy,
        long revision = 1) =>
        await new EffectiveToolSnapshotBuilder().BuildAsync(
            [proxy],
            new ToolPlanningContext(
                "thread_test",
                "turn_001",
                Environment.CurrentDirectory,
                "default",
                null,
                [],
                revision));

    private static RuntimeDynamicToolFunction CreateReviewToolSpec(
        ChannelToolApprovalDescriptor? approval = null) =>
        new()
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

    private static RecordingTransport SuccessTransport(
        string text,
        JsonNode? structuredContent = null) =>
        new(new RuntimeDynamicToolCallResult
        {
            Success = true,
            ContentItems = [new RuntimeDynamicToolContentItem { Type = "text", Text = text }],
            StructuredContent = structuredContent
        });

    private sealed class RecordingTransport(object result) : IAppServerTransport
    {
        public string? Method { get; private set; }
        public object? Params { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) =>
            Task.CompletedTask;

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
}
