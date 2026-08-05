using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class AgentProfileClientTests
{
    [Fact]
    public async Task ListAsync_UsesAgentProfileEndpoint()
    {
        var (client, transport) = await ConnectAsync();
        await using (client)
        await using (transport)
        {
            var pending = client.AgentProfiles.ListAsync(new AgentProfileListParams
            {
                Source = "managed",
                IncludeInvalid = true
            });
            using var outbound = await transport.ReadOutboundAsync();
            Assert.Equal("agent/profiles/list", outbound.RootElement.GetProperty("method").GetString());
            Assert.Equal("managed", outbound.RootElement.GetProperty("params").GetProperty("source").GetString());

            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new { profiles = Array.Empty<object>() }
            });

            IReadOnlyList<AgentProfileEntry>? profiles = (await pending).Profiles.Value;
            Assert.NotNull(profiles);
            Assert.Empty(profiles);
        }
    }

    [Fact]
    public async Task ValidationError_PreservesRawAndStructuredServerData()
    {
        var (client, transport) = await ConnectAsync();
        await using (client)
        await using (transport)
        {
            var pending = client.AgentProfiles.RefreshThreadAsync(new AgentProfileRefreshThreadParams
            {
                ThreadId = "thread-1"
            });
            using var outbound = await transport.ReadOutboundAsync();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                error = new
                {
                    code = -32087,
                    message = "Agent profile validation failed",
                    data = new
                    {
                        code = "AgentProfileValidationFailed",
                        messageKey = "errors.agentProfileValidationFailed",
                        fallbackText = "Agent profile validation failed",
                        detail = "Unsupported agent profile overlay field(s): toolPolicy.",
                        @params = new
                        {
                            diagnostics = new[]
                            {
                                new { severity = "error", code = "UnsupportedOverlayField", message = "toolPolicy is unsupported" }
                            }
                        }
                    }
                }
            });

            JsonRpcException error = await Assert.ThrowsAsync<JsonRpcException>(() => pending);
            Assert.Equal(-32087, error.RpcCode);
            Assert.Equal("AgentProfileValidationFailed", error.ServerError?.Code);
            Assert.Equal("Unsupported agent profile overlay field(s): toolPolicy.", error.ServerError?.Detail);
            Assert.Equal(
                "UnsupportedOverlayField",
                error.ServerError?.Params?.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal("AgentProfileValidationFailed", error.ErrorData?.GetProperty("code").GetString());
        }
    }

    private static async Task<(DotCraftClient Client, TestJsonRpcTransport Transport)> ConnectAsync()
    {
        var transport = new TestJsonRpcTransport();
        var connecting = DotCraftClient.ConnectAsync(
            transport,
            new DotCraftClientOptions { ClientName = "test", ClientVersion = "0.1" });
        using (var initialize = await transport.ReadOutboundAsync())
        {
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = initialize.RootElement.GetProperty("id").GetInt64(),
                result = new InitializeResult
                {
                    ServerInfo = new ServerInfo { Name = "dotcraft", Version = "test", ProtocolVersion = "1" },
                    Capabilities = new ServerCapabilities { AgentProfileManagement = true }
                }
            });
        }
        using (await transport.ReadOutboundAsync())
        {
        }
        return (await connecting, transport);
    }
}
