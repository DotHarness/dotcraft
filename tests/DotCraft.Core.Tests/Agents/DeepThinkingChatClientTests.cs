using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class DeepThinkingChatClientTests
{
    [Fact]
    public void Prepare_AssistantToolCallWithReasoning_AddsReasoningContentRawRepresentation()
    {
        var client = CreateClient(endpoint: "https://api.deepseek.com/v1", model: "deepseek-reasoner");
        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
        [
            new TextReasoningContent("need a tool"),
            new TextContent("I will inspect."),
            new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?> { ["path"] = "a.txt" })
        ]);

        var prepared = client.Prepare([message], null);

        var preparedMessage = Assert.Single(prepared.Messages);
        var raw = Assert.IsType<OpenAI.Chat.AssistantChatMessage>(preparedMessage.RawRepresentation);
        var json = ModelReaderWriter.Write(raw).ToString();
        Assert.Equal(1, CountRootProperties(json, "content"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("I will inspect.", root.GetProperty("content").GetString());
        Assert.Equal("need a tool", root.GetProperty("reasoning_content").GetString());
        var toolCall = Assert.Single(root.GetProperty("tool_calls").EnumerateArray());
        Assert.Equal("call-1", toolCall.GetProperty("id").GetString());
        Assert.Equal("ReadFile", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"path":"a.txt"}""", toolCall.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public void Prepare_ForMimoModel_AddsReasoningContentRawRepresentation()
    {
        var client = CreateClient(endpoint: "https://api.openai-compatible.test/v1", model: "mimo-v2.5-pro");
        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
        [
            new TextReasoningContent("need a tool"),
            new TextContent("I will inspect."),
            new FunctionCallContent("call-1", "ReadFile", null)
        ]);

        var prepared = client.Prepare([message], null);

        var raw = Assert.IsType<OpenAI.Chat.AssistantChatMessage>(
            Assert.Single(prepared.Messages).RawRepresentation);
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.Equal("need a tool", document.RootElement.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public void Prepare_ForNamespacedMimoModel_AddsReasoningContentRawRepresentation()
    {
        var client = CreateClient(endpoint: "https://api.openai-compatible.test/v1", model: "provider/xiaomi/mimo-v2.5-pro");
        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
        [
            new TextReasoningContent("need a tool"),
            new FunctionCallContent("call-1", "ReadFile", null)
        ]);

        var prepared = client.Prepare([message], null);

        Assert.IsType<OpenAI.Chat.AssistantChatMessage>(
            Assert.Single(prepared.Messages).RawRepresentation);
    }

    [Fact]
    public void Prepare_ForNonCatalogEndpointAndModel_LeavesRawRepresentationUnchanged()
    {
        var client = CreateClient(endpoint: "https://api.openai.com/v1", model: "gpt-4o-mini");
        var message = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)
        [
            new TextReasoningContent("need a tool"),
            new FunctionCallContent("call-1", "ReadFile", null)
        ]);

        var prepared = client.Prepare([message], null);

        Assert.Same(message, Assert.Single(prepared.Messages));
        Assert.Null(prepared.Options);
        Assert.Null(message.RawRepresentation);
    }

    [Fact]
    public void Prepare_WhenReasoningEnabled_AddsDeepThinkingOption()
    {
        var client = CreateClient(
            endpoint: "https://api.openai-compatible.test/v1",
            model: "mimo-v2.5-pro",
            reasoningEnabled: true);
        var options = new ChatOptions { ModelId = "mimo-v2.5-pro" };

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], options);

        Assert.NotSame(options, prepared.Options);
        var raw = Assert.IsType<OpenAI.Chat.ChatCompletionOptions>(
            prepared.Options!.RawRepresentationFactory!(new CaptureChatClient()));
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public void Prepare_UsesReasoningOverrideWhenConfigDisabled()
    {
        var client = new DeepThinkingChatClient(
            new CaptureChatClient(),
            new AppConfig
            {
                Model = "mimo-v2.5-pro",
                Reasoning = new AppConfig.ReasoningConfig { Enabled = false }
            },
            "mimo-v2.5-pro",
            "https://api.openai-compatible.test/v1",
            new AppConfig.ReasoningConfig { Enabled = true });

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], new ChatOptions());

        var raw = Assert.IsType<OpenAI.Chat.ChatCompletionOptions>(
            prepared.Options!.RawRepresentationFactory!(new CaptureChatClient()));
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(raw).ToString());
        Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public void Prepare_ExplicitOnlyWithoutRequestReasoningDoesNotAddDeepThinkingOption()
    {
        var client = new DeepThinkingChatClient(
            new CaptureChatClient(),
            new AppConfig
            {
                Model = "mimo-v2.5-pro",
                Reasoning = new AppConfig.ReasoningConfig { Enabled = true }
            },
            "mimo-v2.5-pro",
            "https://api.openai-compatible.test/v1",
            useDefaultReasoning: false);
        var options = new ChatOptions { ModelId = "mimo-v2.5-pro" };

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], options);

        Assert.Same(options, prepared.Options);
        Assert.Null(prepared.Options!.RawRepresentationFactory);
    }

    private static DeepThinkingChatClient CreateClient(
        string endpoint,
        string model,
        bool reasoningEnabled = false) =>
        new(
            new CaptureChatClient(),
            new AppConfig
            {
                Model = model,
                Reasoning = new AppConfig.ReasoningConfig { Enabled = reasoningEnabled }
            },
            model,
            endpoint);

    private static int CountRootProperties(string json, string propertyName)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var count = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.CurrentDepth == 1 &&
                reader.ValueTextEquals(propertyName))
            {
                count++;
            }
        }

        return count;
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
