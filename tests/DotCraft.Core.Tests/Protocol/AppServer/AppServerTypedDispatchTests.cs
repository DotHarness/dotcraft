using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerTypedDispatchTests
{
    [Fact]
    public async Task ClientNotification_UsesDescriptorRegistration()
    {
        using var harness = new AppServerTestHarness();
        var initialize = harness.BuildRequest(Contract.AppServerRpc.Initialize.Name, new
        {
            clientInfo = new { name = "typed-client", version = "1.0" },
            capabilities = new { }
        });

        await harness.Handler.HandleRequestAsync(initialize, default);

        Assert.False(harness.Connection.IsClientReady);
        Assert.True(harness.Handler.HandleNotification(
            harness.BuildNotification(Contract.AppServerRpc.Initialized.Name, new { })));
        Assert.True(harness.Connection.IsClientReady);
        Assert.False(harness.Handler.HandleNotification(
            harness.BuildNotification("extension/notification", new { value = 1 })));
    }

    [Fact]
    public async Task TypedRequest_InvalidRequiredParams_UsesStableInvalidParamsError()
    {
        using var harness = new AppServerTestHarness();
        using var _ = await harness.InitializeAsync();

        var exception = await Assert.ThrowsAsync<AppServerException>(() =>
            harness.Handler.HandleRequestAsync(
                harness.BuildRequest(Contract.AppServerRpc.ThreadRead.Name, new { }),
                default));

        Assert.Equal(AppServerErrors.InvalidParamsCode, exception.Code);
    }

    [Fact]
    public async Task TypedNotification_WritesDescriptorMethodAndPayload()
    {
        var transport = new InMemoryTransport();

        await transport.NotifyAsync(
            Contract.AppServerRpc.ThreadDeleted,
            new Contract.ThreadDeletedNotification { ThreadId = "thread_001" });

        using var message = await transport.ReadNextSentAsync();
        Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(Contract.AppServerRpc.ThreadDeleted.Name, message.RootElement.GetProperty("method").GetString());
        Assert.Equal("thread_001", message.RootElement.GetProperty("params").GetProperty("threadId").GetString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.NotifyAsync(
            Contract.AppServerRpc.Initialized,
            new DotCraft.Protocol.RpcEmpty()));
    }

    [Fact]
    public async Task CatalogNotification_ProjectsEstablishedWirePayload()
    {
        var transport = new InMemoryTransport();

        await transport.NotifyContractAsync(
            Contract.AppServerRpc.AuthOpenAiAuthorizeUrl,
            new Contract.AuthOpenAiAuthorizeUrlNotification
            {
                Url = "https://example.test/authorize",
                CallbackPort = 1455
            });

        using var message = await transport.ReadNextSentAsync();
        Assert.Equal(Contract.AppServerRpc.AuthOpenAiAuthorizeUrl.Name, message.RootElement.GetProperty("method").GetString());
        Assert.Equal("https://example.test/authorize", message.RootElement.GetProperty("params").GetProperty("url").GetString());
        Assert.Equal(1455, message.RootElement.GetProperty("params").GetProperty("callbackPort").GetInt32());
    }

    [Fact]
    public async Task TypedReverseRequest_DeserializesResultAndPreservesUnknownProperties()
    {
        var transport = new InMemoryTransport
        {
            ApprovalHandler = (_, _) => InMemoryTransport.BuildClientResponse(1, new
            {
                decision = "accept",
                futureMetadata = new { source = "client" }
            })
        };

        var response = await transport.RequestAsync(
            Contract.AppServerRpc.ApprovalRequest,
            CreateApprovalRequest());

        Assert.Null(response.Error);
        Assert.Null(response.InvalidResult);
        Assert.Equal("accept", response.Result!.Decision);
        Assert.Equal(
            "client",
            response.Result.ExtensionData!["futureMetadata"].GetProperty("source").GetString());
    }

    [Fact]
    public async Task TypedReverseRequest_ReportsInvalidResultWithoutThrowing()
    {
        var transport = new InMemoryTransport
        {
            ApprovalHandler = (_, _) => InMemoryTransport.BuildClientResponse(1, new { })
        };

        var response = await transport.RequestAsync(
            Contract.AppServerRpc.ApprovalRequest,
            CreateApprovalRequest());

        Assert.Null(response.Result);
        Assert.Null(response.Error);
        Assert.NotNull(response.InvalidResult);
    }

    private static Contract.ApprovalRequestParams CreateApprovalRequest() => new()
    {
        ThreadId = "thread_001",
        TurnId = "turn_001",
        ItemId = "item_001",
        RequestId = "request_001",
        ApprovalType = "tool",
        Operation = "execute",
        Target = "test-tool",
        ScopeKey = "test-tool",
        ExpiresAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
    };
}
