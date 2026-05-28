using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for the external channel adapter protocol extensions:
/// - channelAdapter capability in initialize handshake
/// - AppServerConnection channel adapter state tracking
/// - ext/channel/* method constants
/// - ChannelRejected error code
/// </summary>
public sealed class ExternalChannelProtocolTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // channelAdapter capability in initialize
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_WithChannelAdapter_SetsConnectionState()
    {
        // Arrange: build an initialize request with channelAdapter capability
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "telegram-adapter", version = "1.0.0" },
            capabilities = new
            {
                approvalSupport = true,
                streamingSupport = true,
                channelAdapter = new
                {
                    channelName = "telegram"
                }
            }
        });

        // Act
        await _h.ExecuteRequestAsync(initMsg);

        // Assert: read the response the handler wrote to the transport
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        // Assert: connection state reflects channel adapter
        Assert.True(_h.Connection.IsInitialized);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.Equal("telegram", _h.Connection.ChannelAdapterName);
        Assert.Null(_h.Connection.DeliveryCapabilities);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapterStructuredDelivery_SetsStructuredState()
    {
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "media-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "telegram",
                    deliveryCapabilities = new
                    {
                        structuredDelivery = true,
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
                    }
                }
            }
        });

        await _h.ExecuteRequestAsync(initMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(_h.Connection.SupportsStructuredDelivery);
        Assert.NotNull(_h.Connection.DeliveryCapabilities);
        Assert.True(_h.Connection.DeliveryCapabilities!.StructuredDelivery);
        Assert.NotNull(_h.Connection.DeliveryCapabilities.Media?.File);
        Assert.True(_h.Connection.DeliveryCapabilities.Media!.File!.SupportsHostPath);
        Assert.True(_h.Connection.DeliveryCapabilities.Media.File!.SupportsBase64);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapterTools_SetsToolState()
    {
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "tool-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "telegram",
                    channelTools = new object[]
                    {
                        new
                        {
                            name = "TelegramSendDocumentToCurrentChat",
                            description = "Send a document to the current chat.",
                            requiresChatContext = true,
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    fileName = new { type = "string" }
                                },
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
        Assert.Single(_h.Connection.DeclaredChannelTools);
        Assert.Equal("TelegramSendDocumentToCurrentChat", _h.Connection.DeclaredChannelTools[0].Name);
        Assert.Empty(_h.Connection.RegisteredChannelTools);
        Assert.False(_h.Connection.ChannelToolRegistrationFinalized);
    }

    [Fact]
    public async Task Initialize_WithChannelAdapter_WithoutDeliveryCapabilities_RemainsSendDisabledUntilAdvertised()
    {
        var initMsg = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
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
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(_h.Connection.IsChannelAdapter);
        Assert.False(_h.Connection.SupportsStructuredDelivery);
        Assert.Null(_h.Connection.DeliveryCapabilities);
    }

    [Fact]
    public async Task Initialize_WithoutChannelAdapter_ConnectionIsNotAdapter()
    {
        // Regular client without channelAdapter
        await _h.InitializeAsync();

        Assert.False(_h.Connection.IsChannelAdapter);
        Assert.Null(_h.Connection.ChannelAdapterName);
        Assert.Null(_h.Connection.DeliveryCapabilities);
    }

    // -------------------------------------------------------------------------
    // Protocol constants
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtChannelMethods_HaveCorrectValues()
    {
        Assert.Equal("ext/channel/send", AppServerMethods.ExtChannelSend);
        Assert.Equal("ext/channel/toolCall", AppServerMethods.ExtChannelToolCall);
        Assert.Equal("ext/channel/heartbeat", AppServerMethods.ExtChannelHeartbeat);
    }

    [Fact]
    public void ChannelRejectedErrorCode_HasCorrectValue()
    {
        Assert.Equal(-32030, AppServerErrors.ChannelRejectedCode);
    }

    [Fact]
    public void ChannelRejectedError_ContainsChannelName()
    {
        var ex = AppServerErrors.ChannelRejected("test-channel");
        Assert.Equal(AppServerErrors.ChannelRejectedCode, ex.Code);
        Assert.Contains("test-channel", ex.Message);
    }

    // -------------------------------------------------------------------------
    // ChannelAdapterCapability serialization
    // -------------------------------------------------------------------------

    [Fact]
    public void ChannelAdapterCapability_RoundTrips()
    {
        var cap = new ChannelAdapterCapability
        {
            ChannelName = "discord",
            DeliveryCapabilities = new ChannelDeliveryCapabilities
            {
                StructuredDelivery = true,
                Media = new ChannelMediaCapabilitySet
                {
                    Audio = new ChannelMediaConstraints
                    {
                        SupportsBase64 = true,
                        SupportsCaption = false
                    }
                }
            },
            ChannelTools =
            [
                new ChannelToolDescriptor
                {
                    Name = "discordSendAttachment",
                    Description = "Send an attachment to the current Discord channel.",
                    RequiresChatContext = true,
                    InputSchema = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["fileName"] = new JsonObject
                            {
                                ["type"] = "string"
                            }
                        },
                        ["required"] = new JsonArray("fileName")
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(cap, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var deserialized = JsonSerializer.Deserialize<ChannelAdapterCapability>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal("discord", deserialized.ChannelName);
        Assert.True(deserialized.DeliveryCapabilities?.StructuredDelivery);
        Assert.True(deserialized.DeliveryCapabilities?.Media?.Audio?.SupportsBase64);
        Assert.Single(deserialized.ChannelTools!);
        Assert.Equal("discordSendAttachment", deserialized.ChannelTools![0].Name);
    }

    [Fact]
    public void AppServerClientCapabilities_WithChannelAdapter_RoundTrips()
    {
        var caps = new AppServerClientCapabilities
        {
            ApprovalSupport = true,
            StreamingSupport = true,
            ChannelAdapter = new ChannelAdapterCapability
            {
                ChannelName = "telegram",
                DeliveryCapabilities = new ChannelDeliveryCapabilities
                {
                    StructuredDelivery = true
                }
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(caps, options);
        var deserialized = JsonSerializer.Deserialize<AppServerClientCapabilities>(json, options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.ChannelAdapter);
        Assert.Equal("telegram", deserialized.ChannelAdapter.ChannelName);
        Assert.True(deserialized.ChannelAdapter.DeliveryCapabilities?.StructuredDelivery);
    }

    [Fact]
    public void AppServerClientCapabilities_WithoutChannelAdapter_OmitsInJson()
    {
        var caps = new AppServerClientCapabilities
        {
            ApprovalSupport = true,
            ChannelAdapter = null
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(caps, options);

        // channelAdapter should not appear in JSON when null
        Assert.DoesNotContain("channelAdapter", json);
    }
}
