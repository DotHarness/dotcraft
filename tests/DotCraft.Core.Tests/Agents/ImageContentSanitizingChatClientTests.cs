using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DotCraft.Tests.Agents;

public sealed class ImageContentSanitizingChatClientTests
{
    public static TheoryData<string> RichResultShapes => new()
    {
        "list",
        "array",
        "deserialized"
    };

    [Fact]
    public async Task GetStreamingResponseAsync_ToolBmpResult_PromotesPreparedPngImage()
    {
        using var inner = new CapturingChatClient();
        using var client = new ImageContentSanitizingChatClient(inner);

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(CreateToolResultMessages(
            new DataContent(CreateBmpBytes(), "image/bmp"))));

        var syntheticUser = Assert.Single(
            inner.LastMessages,
            message => message.Role == ChatRole.User &&
                       message.Contents.OfType<DataContent>().Any());
        var image = Assert.Single(syntheticUser.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("image/png", Image.DetectFormat(image.Data.ToArray()).DefaultMimeType);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InvalidToolImage_AddsPlaceholderWithoutPromotingImage()
    {
        using var inner = new CapturingChatClient();
        using var client = new ImageContentSanitizingChatClient(inner);

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(CreateToolResultMessages(
            new DataContent(new byte[] { 1, 2, 3 }, "image/bmp"))));

        Assert.DoesNotContain(
            inner.LastMessages,
            message => message.Role == ChatRole.User &&
                       message.Contents.OfType<DataContent>().Any());
        var tool = Assert.Single(inner.LastMessages, message => message.Role == ChatRole.Tool);
        var result = Assert.Single(tool.Contents.OfType<FunctionResultContent>());
        var text = Assert.IsType<string>(result.Result);
        Assert.Contains(ModelImageInputPreparer.CouldNotProcessPlaceholder, text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RichResultShapes))]
    public async Task GetStreamingResponseAsync_RichResultShapes_NeverSerializeImageAsText(string shape)
    {
        using var inner = new CapturingChatClient();
        using var client = new ImageContentSanitizingChatClient(inner);
        var contents = new List<AIContent>
        {
            new TextContent("screenshot"),
            new DataContent(CreateBmpBytes(), "image/bmp")
        };
        object result = shape switch
        {
            "list" => contents,
            "array" => contents.ToArray(),
            "deserialized" => JsonSerializer.SerializeToElement(
                contents,
                DotCraft.Protocol.SessionPersistenceJsonOptions.Default),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(CreateToolResultMessages(result)));

        Assert.DoesNotContain(
            inner.LastMessages.SelectMany(message => message.Contents).OfType<TextContent>(),
            text => text.Text.Contains("data:image/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            inner.LastMessages,
            message => message.Role == ChatRole.User && message.Contents.OfType<DataContent>().Any());
    }

    private static IReadOnlyList<ChatMessage> CreateToolResultMessages(DataContent image) =>
        CreateToolResultMessages((IList<AIContent>)
        [
            new TextContent("Image: text_object0.bmp"),
            image
        ]);

    private static IReadOnlyList<ChatMessage> CreateToolResultMessages(object result) =>
    [
        new ChatMessage(ChatRole.User, "inspect"),
        new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
        [
            new FunctionCallContent(
                "call-1",
                "ReadFile",
                new Dictionary<string, object?> { ["path"] = "text_object0.bmp" })
        ]),
        new ChatMessage(ChatRole.Tool, (IList<AIContent>)
        [
            new FunctionResultContent(
                "call-1",
                result)
        ])
    ];

    private static async Task<List<ChatResponseUpdate>> CollectStreamingAsync(
        IAsyncEnumerable<ChatResponseUpdate> streaming)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in streaming)
            updates.Add(update);
        return updates;
    }

    private static byte[] CreateBmpBytes()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(0xff, 0, 0));
        using var stream = new MemoryStream();
        image.SaveAsBmp(stream);
        return stream.ToArray();
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
