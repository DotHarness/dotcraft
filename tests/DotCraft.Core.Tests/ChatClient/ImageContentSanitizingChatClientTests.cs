using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

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
                DotCraft.Sessions.SessionPersistenceJsonOptions.Default),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(CreateToolResultMessages(result)));

        Assert.DoesNotContain(
            inner.LastMessages.SelectMany(message => message.Contents).OfType<TextContent>(),
            text => text.Text.Contains("data:image/", StringComparison.OrdinalIgnoreCase));
        var syntheticUser = Assert.Single(
            inner.LastMessages,
            message => message.Role == ChatRole.User && message.Contents.OfType<DataContent>().Any());
        var image = Assert.Single(syntheticUser.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal((1, 1), ImageFixture.PngSize(image.Data.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStreamingResponseAsync_LaterRequest_StartsWithEarlierRequest(bool toolResultsAcceptImages)
    {
        using var inner = new CapturingChatClient();
        using var client = new ImageContentSanitizingChatClient(inner, toolResultsAcceptImages);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "inspect"),
            new(ChatRole.Assistant, (IList<AIContent>)
            [
                new FunctionCallContent("call-1", "Screenshot"),
                new FunctionCallContent("call-2", "Screenshot")
            ]),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call-1", ScreenshotResult())]),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call-2", ScreenshotResult())])
        };

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(history));
        var first = inner.LastMessages.Select(Signature).ToList();
        history.Add(new ChatMessage(ChatRole.Assistant, "two red pixels"));
        history.Add(new ChatMessage(ChatRole.User, "thanks"));
        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(history));
        var second = inner.LastMessages.Select(Signature).ToList();

        Assert.Equal(first, second.Take(first.Count));
        Assert.Equal(first.Count + 2, second.Count);
        var images = inner.LastMessages.SelectMany(message => message.Contents)
            .SelectMany(content => content is FunctionResultContent { Result: IEnumerable<AIContent> items } ? items : [content])
            .OfType<DataContent>();
        Assert.Equal(2, images.Count());
        if (!toolResultsAcceptImages)
        {
            Assert.Equal(ChatRole.User, inner.LastMessages[4].Role);
            Assert.Equal(2, inner.LastMessages[4].Contents.OfType<DataContent>().Count());
        }
    }

    private static List<AIContent> ScreenshotResult() =>
    [
        new TextContent("screenshot"),
        new DataContent(CreateBmpBytes(), "image/bmp")
    ];

    private static string Signature(ChatMessage message) =>
        $"{message.Role}:" + string.Join("|", message.Contents.Select(content => content switch
        {
            FunctionResultContent result => $"result:{result.CallId}:{ImageContentSanitizingChatClient.DescribeResult(result.Result)}",
            DataContent data => $"data:{data.MediaType}:{Convert.ToBase64String(data.Data.Span)}",
            _ => content.ToString()
        }));

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

    private static byte[] CreateBmpBytes() => ImageFixture.Red("image/bmp");

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
