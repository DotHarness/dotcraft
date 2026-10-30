using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Providers.OpenAI;

public sealed class OpenAIResponsesProviderHistoryToolImagesTests
{
    [Fact]
    public async Task PrepareInputAsync_KeepsToolImagesInTheirOutputsAcrossRequests()
    {
        var context = CreateContext();
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "look at the screen"),
            Call("call-1"),
            Result("call-1")
        ];

        var first = await context.PrepareInputAsync(messages, null, CancellationToken.None);
        var output = Assert.IsType<JsonArray>(first.Input.OfType<JsonObject>().Single(IsCallOutput)["output"]);
        Assert.Equal(["input_text", "input_image"], output.Select(part => (string?)part!["type"]));

        messages.AddRange([Call("call-2"), Result("call-2"), new ChatMessage(ChatRole.User, "thanks")]);
        var second = await context.PrepareInputAsync(messages, null, CancellationToken.None);

        Assert.Equal(
            first.Input.Select(item => item!.ToJsonString()),
            second.Input.Take(first.Input.Count).Select(item => item!.ToJsonString()));
        Assert.Equal(2, second.Input.OfType<JsonObject>().Count(IsCallOutput));
    }

    [Fact]
    public async Task CaptureInputAsync_InvalidToolImageInUnsentTail_BecomesPlaceholder()
    {
        var context = CreateContext();
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "look at the screen"),
            Call("call-1"),
            new(ChatRole.Tool, (IList<AIContent>)
            [
                new FunctionResultContent("call-1", (IList<AIContent>)[new DataContent(new byte[] { 1, 2, 3 }, "image/png")])
            ])
        ];

        var input = await context.CaptureInputAsync(ProviderCompactionPhase.MidTurn, messages, null, CancellationToken.None);

        var output = input.Items
            .Select(item => JsonNode.Parse(item.Payload.GetRawText())!.AsObject())
            .Single(IsCallOutput)["output"];
        var part = Assert.Single(Assert.IsType<JsonArray>(output));
        Assert.Equal("input_text", (string?)part!["type"]);
        Assert.Equal(ModelImageInputPreparer.CouldNotProcessPlaceholder, (string?)part["text"]);
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
        new(ChatRole.Tool, (IList<AIContent>)
        [
            new FunctionResultContent(callId, (IList<AIContent>)
            [
                new TextContent("screenshot"),
                new DataContent(ImageFixture.Red("image/png"), "image/png")
            ])
        ]);

    private static bool IsCallOutput(JsonObject item) => (string?)item["type"] == "function_call_output";

    private static IEnumerable<JsonObject> Parts(JsonArray input) =>
        input.OfType<JsonObject>()
            .Select(item => item["content"])
            .OfType<JsonArray>()
            .SelectMany(content => content.OfType<JsonObject>());

    private static List<JsonObject> Images(JsonArray input) =>
        Parts(input).Where(part => (string?)part["type"] == "input_image").ToList();
}
