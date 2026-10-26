using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Providers.OpenAI;

public sealed class OpenAIResponsesProviderHistoryToolImagesTests
{
    [Fact]
    public async Task PrepareInputAsync_SendsOnlyTheCurrentToolRoundImages()
    {
        var context = CreateContext();
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "look at the screen"),
            Call("call-1"),
            Result("call-1"),
            ToolImages()
        ];

        var first = await context.PrepareInputAsync(messages, null, CancellationToken.None);
        Assert.Single(Images(first.Input));

        // The sanitizer re-attaches only the newest round, so the earlier attachment message drops out.
        messages.RemoveAt(messages.Count - 1);
        messages.AddRange([Call("call-2"), Result("call-2"), ToolImages()]);
        var second = await context.PrepareInputAsync(messages, null, CancellationToken.None);
        Assert.Single(Images(second.Input));
        Assert.Contains(Texts(second.Input), text => text.StartsWith("[Image (image/png), ", StringComparison.Ordinal));

        // A retry of the same request keeps the current round's image.
        var retry = await context.PrepareInputAsync(messages, null, CancellationToken.None);
        Assert.Single(Images(retry.Input));

        messages.RemoveAt(messages.Count - 1);
        messages.Add(new ChatMessage(ChatRole.User, "thanks"));
        var next = await context.PrepareInputAsync(messages, null, CancellationToken.None);
        Assert.Empty(Images(next.Input));
    }

    [Fact]
    public async Task PrepareInputAsync_KeepsUserAttachedImages()
    {
        var context = CreateContext();
        List<ChatMessage> messages =
        [
            new(ChatRole.User, (IList<AIContent>)[new TextContent("what is this"), new DataContent(ImageFixture.Red("image/png"), "image/png")]),
            new(ChatRole.Assistant, "a red pixel"),
            new(ChatRole.User, "thanks")
        ];

        var input = await context.PrepareInputAsync(messages, null, CancellationToken.None);

        Assert.Single(Images(input.Input));
    }

    private static OpenAIResponsesProviderHistoryContext CreateContext() =>
        new(
            new ProviderConversationIdentity(
                CurrentThreadId: "thread-images",
                RootThreadId: "thread-images",
                ParentThreadId: null,
                ForkedFromThreadId: null,
                TurnId: "turn-images",
                ContextWindowId: "window-images",
                RequestKind: ProviderRequestKind.Turn,
                TurnStartedAtUnixMs: 1,
                ThreadSource: "test",
                SubagentKind: null),
            providerId: "provider-images",
            ProviderHistorySnapshot.Empty("window-images"),
            coveredMessages: [],
            appendAsync: null,
            replaceAsync: null,
            abortAsync: null);

    private static ChatMessage Call(string callId) =>
        new(ChatRole.Assistant, (IList<AIContent>)[new FunctionCallContent(callId, "Screenshot")]);

    private static ChatMessage Result(string callId) =>
        new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent(callId, "[Image (image/png), 70 bytes]")]);

    private static ChatMessage ToolImages() =>
        new(ChatRole.User, (IList<AIContent>)
        [
            new TextContent(ModelImageInputPreparer.ToolResultImagesMarker),
            new DataContent(ImageFixture.Red("image/png"), "image/png")
        ]);

    private static IEnumerable<JsonObject> Parts(JsonArray input) =>
        input.OfType<JsonObject>()
            .Select(item => item["content"])
            .OfType<JsonArray>()
            .SelectMany(content => content.OfType<JsonObject>());

    private static List<JsonObject> Images(JsonArray input) =>
        Parts(input).Where(part => (string?)part["type"] == "input_image").ToList();

    private static List<string> Texts(JsonArray input) =>
        Parts(input).Where(part => (string?)part["type"] == "input_text").Select(part => (string)part["text"]!).ToList();
}
