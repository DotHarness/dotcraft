using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.ExternalChannel.Messaging;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Channels;

public sealed class ReadChannelHistoryTests
{
    private const string ThreadId = "thread-1";
    private readonly ChannelRuntimeRegistry _registry = new();
    private readonly FakeChannel _qq = new();

    public ReadChannelHistoryTests() => _registry.Register(_qq);

    [Fact]
    public async Task Read_TargetsTheBoundChat_CapsTheLimit_AndReturnsTheCursor()
    {
        _qq.Capabilities.History = new ChannelHistoryCapabilitySnapshot { MaxMessages = 10 };
        _qq.Result = new ChannelHistoryResult
        {
            Messages =
            [
                new ChannelHistoryEntry("first", "1", "7", "Carol", "2026-11-11T01:00:00Z"),
                new ChannelHistoryEntry("mine", "2", "42", "Bot", FromBot: true)
            ],
            Before = "seq-1"
        };

        var result = await Runtime().InvokeAsync(Context(), Args("""{ "limit": 30, "before": "seq-9" }"""));

        Assert.True(result.Success);
        Assert.Equal(new ChannelHistoryRequest("group:42", 10, "seq-9"), Assert.Single(_qq.Requests));
        Assert.Contains("Carol (7): first", result.Content);
        Assert.Contains("You: mine", result.Content);
        Assert.Equal("seq-1", result.StructuredContent!.Value.GetProperty("before").GetString());
    }

    [Theory]
    [InlineData("""{ "limit": 0 }""")]
    [InlineData("""{ "limit": 51 }""")]
    [InlineData("""{ "before": "" }""")]
    public async Task InvalidArguments_AreRejectedBeforeReading(string arguments)
    {
        _qq.Capabilities.History = new ChannelHistoryCapabilitySnapshot { MaxMessages = 50 };

        var result = await Runtime().InvokeAsync(Context(), Args(arguments));

        Assert.Equal(ReadChannelHistoryErrorCodes.InvalidArguments, result.Error?.Code);
        Assert.Empty(_qq.Requests);
    }

    [Fact]
    public async Task ChannelWithoutHistory_IsUnsupported()
    {
        var result = await Runtime().InvokeAsync(Context(), Args("{}"));

        Assert.Equal(ReadChannelHistoryErrorCodes.Unsupported, result.Error?.Code);
        Assert.Empty(_qq.Requests);
    }

    [Theory]
    [InlineData("HistoryUnavailable", ReadChannelHistoryErrorCodes.HistoryUnavailable)]
    [InlineData("InvalidCursor", ReadChannelHistoryErrorCodes.InvalidArguments)]
    [InlineData("AdapterDisconnected", ReadChannelHistoryErrorCodes.ChannelUnavailable)]
    public async Task AdapterFailures_MapToToolErrors(string adapterCode, string toolCode)
    {
        _qq.Capabilities.History = new ChannelHistoryCapabilitySnapshot { MaxMessages = 50 };
        _qq.Result = ChannelHistoryResult.Fail(adapterCode, "nope");

        var result = await Runtime().InvokeAsync(Context(), Args("{}"));

        Assert.Equal(toolCode, result.Error?.Code);
    }

    private ReadChannelHistoryRuntime Runtime() =>
        new(new ChatBindingResolver(_registry), ThreadId, "qq", "group:42");

    private static ToolInvocationContext Context() => new(
        ThreadId,
        "turn-1",
        "call-1",
        ToolInvocationAudience.Model,
        new ToolName(null, ReadChannelHistoryToolSource.ToolName),
        new ToolDefinitionId(ToolSourceKind.CoreNative, "channel-history", new SourceToolId(ReadChannelHistoryToolSource.ToolName)),
        new RuntimeBindingId("binding"),
        1,
        DateTimeOffset.UtcNow);

    private static JsonObject Args(string json) => JsonNode.Parse(json)!.AsObject();

    private sealed class FakeChannel : IChannelRuntime
    {
        public string Name => "qq";

        public string DisplayName => "QQ";

        public ChannelDeliveryCapabilitySnapshot Capabilities { get; } = new();

        public ChannelHistoryResult Result { get; set; } = new();

        public List<ChannelHistoryRequest> Requests { get; } = [];

        public ChannelDeliveryCapabilitySnapshot? GetDeliveryCapabilities() => Capabilities;

        public Task<ChannelHistoryResult> ReadHistoryAsync(ChannelHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }

        public Task<ChannelDeliveryResult> DeliverAsync(
            string target,
            ChannelDeliveryMessage message,
            object? metadata = null,
            string? deliveryId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelDeliveryResult { Delivered = true });
    }
}
