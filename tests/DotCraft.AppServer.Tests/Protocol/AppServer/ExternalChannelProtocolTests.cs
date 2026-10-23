using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ExternalChannelProtocolTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Initialize_WithChannelAdapter_RecordsAdapterDeliveryCapabilitiesAndDeclaredTools()
    {
        var initMsg = InMemoryTransport.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "telegram-adapter", version = "1.0.0" },
            capabilities = new
            {
                approvalSupport = true,
                streamingSupport = true,
                channelAdapter = new
                {
                    channelName = "telegram",
                    displayName = "Telegram",
                    deliveryCapabilities = new
                    {
                        text = new { format = "markdown" },
                        targets = new { group = true, direct = "knownOnly" },
                        media = new
                        {
                            file = new
                            {
                                supportsHostPath = true,
                                supportsUrl = false,
                                supportsBase64 = true,
                                supportsCaption = true
                            }
                        }
                    },
                    channelTools = new object[]
                    {
                        new
                        {
                            name = "TelegramExportChatHistory",
                            description = "Export the current chat history to a file.",
                            requiresChatContext = true,
                            inputSchema = new
                            {
                                type = "object",
                                properties = new { fileName = new { type = "string" } },
                                required = new[] { "fileName" }
                            }
                        }
                    }
                }
            }
        });

        await _h.ExecuteRequestAsync(initMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(_h.Connection.IsInitialized);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.Equal("telegram", _h.Connection.ChannelAdapterName);
        Assert.Equal("Telegram", _h.Connection.ChannelDisplayName);
        var delivery = Assert.IsType<DotCraft.Channels.ChannelDeliveryCapabilitySnapshot>(_h.Connection.DeliveryCapabilities);
        Assert.Equal("markdown", delivery.Text.Format);
        Assert.True(delivery.Targets.Group);
        Assert.Equal("knownOnly", delivery.Targets.Direct);
        Assert.True(delivery.Media!.File!.SupportsHostPath);
        Assert.True(delivery.Media.File.SupportsBase64);
        Assert.Equal("TelegramExportChatHistory", Assert.Single(_h.Connection.DeclaredChannelTools).Name);
        Assert.Empty(_h.Connection.RegisteredChannelTools);
        Assert.False(_h.Connection.ChannelToolRegistrationFinalized);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapterWithoutDeliveryCapabilities_IsRejected()
    {
        var initMsg = InMemoryTransport.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "simple-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "simple"
                }
            }
        });

        await _h.ExecuteRequestAsync(initMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.False(_h.Connection.IsChannelAdapter);
    }
}
