using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using AnthropicCacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using System.ClientModel.Primitives;

namespace DotCraft.Tests.Agents;

public sealed class PromptCachingChatClientTests
{
    private const string AnthropicCacheControlKey = "anthropic:cache_control";

    [Fact]
    public void Prepare_ForClaudeModel_OpenAICompatibleMarksPrefixAndLatestUser()
    {
        var client = CreateClient("anthropic/claude-opus-4-1");
        var system = new ChatMessage(ChatRole.System, "stable system prompt");
        var user = new ChatMessage(ChatRole.User, "hello");

        var prepared = client.Prepare([system, user], null);

        var systemText = Assert.IsType<TextContent>(Assert.Single(prepared.Messages[0].Contents));
        AssertCacheControl(systemText, expectedTtl: null);
        var userText = AssertLastTextContent(prepared.Messages[1]);
        AssertCacheControl(userText, expectedTtl: null);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
    }

    [Fact]
    public void Prepare_WithInstructions_OpenAICompatibleMarksPrefixAndLatestUser()
    {
        var client = CreateClient("claude-3-5-sonnet");
        var options = new ChatOptions { Instructions = "stable system prompt" };
        var user = new ChatMessage(ChatRole.User, "hello");

        var prepared = client.Prepare([user], options);

        Assert.Null(prepared.Options!.Instructions);
        var system = Assert.Single(prepared.Messages, m => m.Role == ChatRole.System);
        var systemText = Assert.IsType<TextContent>(Assert.Single(system.Contents));
        Assert.Equal("stable system prompt", systemText.Text);
        AssertCacheControl(systemText, expectedTtl: null);

        var userText = AssertLastTextContent(prepared.Messages.Last());
        AssertCacheControl(userText, expectedTtl: null);
        Assert.Equal("stable system prompt", options.Instructions);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
    }

    [Fact]
    public void Prepare_WithToolTail_OpenAICompatibleMarksOnlyLatestToolResult()
    {
        var client = CreateClient("claude-opus-4-1");
        var firstResult = new FunctionResultContent("call_1", "first");
        var secondResult = new FunctionResultContent("call_2", "second")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["existing"] = true
            },
            Exception = new InvalidOperationException("boom")
        };
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[firstResult, secondResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant response"),
            tool
        ], null);

        var preparedUserText = AssertLastTextContent(prepared.Messages[0]);
        AssertNoCacheControl(preparedUserText);
        var preparedAssistantText = AssertLastTextContent(prepared.Messages[1]);
        AssertNoCacheControl(preparedAssistantText);
        Assert.Equal(4, prepared.Messages.Count);
        var firstPrepared = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[2].Contents));
        var secondPrepared = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[3].Contents));
        Assert.Same(firstResult, firstPrepared);
        Assert.NotSame(secondResult, secondPrepared);
        Assert.Equal(ChatRole.Tool, prepared.Messages[2].Role);
        Assert.Equal(ChatRole.Tool, prepared.Messages[3].Role);
        Assert.Equal("call_2", secondPrepared.CallId);
        Assert.Equal("second", secondPrepared.Result);
        Assert.Same(secondResult.Exception, secondPrepared.Exception);
        Assert.True((bool)secondPrepared.AdditionalProperties!["existing"]!);
        AssertCacheControl(secondPrepared, expectedTtl: null);
        Assert.Equal([ChatRole.Tool.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
        Assert.False(secondResult.AdditionalProperties?.ContainsKey(PromptCachingChatClient.CacheControlKey) ?? false);
    }

    [Fact]
    public void Prepare_WithToolTailAndNoAssistantText_OpenAICompatibleMarksOnlyToolResult()
    {
        var client = CreateClient("claude-opus-4-1");
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_1", "result text")
        ]);

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello"), tool], null);

        var preparedUserText = AssertLastTextContent(prepared.Messages[0]);
        AssertNoCacheControl(preparedUserText);
        Assert.NotSame(tool, prepared.Messages[1]);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[1].Contents));
        AssertCacheControl(result, expectedTtl: null);
        Assert.Equal("call_1", result.CallId);
        Assert.Equal("result text", result.Result);
        Assert.Equal([ChatRole.Tool.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
    }

    [Fact]
    public void Prepare_WithTextContentToolResult_MarksToolResultWithoutMutatingOriginal()
    {
        var client = CreateClient("claude-opus-4-1");
        var toolResultContents = (IList<AIContent>)[new TextContent("file contents")];
        var originalResult = new FunctionResultContent("call_1", toolResultContents);
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[originalResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            tool
        ], null);

        Assert.NotSame(tool, prepared.Messages[1]);
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[1].Contents));
        Assert.NotSame(originalResult, result);
        Assert.Equal("call_1", result.CallId);
        Assert.Same(toolResultContents, result.Result);
        AssertCacheControl(result, expectedTtl: null);
        Assert.Null(originalResult.AdditionalProperties);
    }

    [Fact]
    public void Prepare_WithMixedToolResult_DoesNotMarkToolResult()
    {
        var client = CreateClient("claude-opus-4-1");
        var originalResult = new FunctionResultContent(
            "call_1",
            (IList<AIContent>)[
                new TextContent("text"),
                new DataContent(new BinaryData([1, 2, 3]), "image/png")
            ]);
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[originalResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            tool
        ], null);

        Assert.Same(tool, prepared.Messages[1]);
        Assert.Null(originalResult.AdditionalProperties);
    }

    [Fact]
    public void Prepare_ForNonMatchingModel_LeavesMessagesAndOptionsUnchanged()
    {
        var client = CreateClient("gpt-4o-mini");
        var options = new ChatOptions { Instructions = "stable system prompt" };
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };

        var prepared = client.Prepare(messages, options);

        Assert.Same(messages, prepared.Messages);
        Assert.Same(options, prepared.Options);
    }

    [Fact]
    public void Prepare_WhenDisabled_LeavesMessagesAndOptionsUnchanged()
    {
        var config = new AppConfig.PromptCachingConfig { Enabled = false };
        var client = new PromptCachingChatClient(new CaptureChatClient(), config, "claude-opus-4-1");
        var options = new ChatOptions { Instructions = "stable system prompt" };
        var messages = new[] { new ChatMessage(ChatRole.User, "hello") };

        var prepared = client.Prepare(messages, options);

        Assert.Same(messages, prepared.Messages);
        Assert.Same(options, prepared.Options);
    }

    [Fact]
    public void Prepare_WithTtl_AddsTtlToCacheControl()
    {
        var client = CreateClient("claude-opus-4-1", ttl: "1h");

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], null);

        var user = Assert.Single(prepared.Messages);
        var text = AssertLastTextContent(user);
        AssertCacheControl(text, expectedTtl: "1h");
    }

    [Fact]
    public void Prepare_AnthropicNative_MarksTextAndToolResultWithSdkCacheControl()
    {
        var client = CreateAnthropicNativeClient("claude-opus-4-1");
        var firstResult = new FunctionResultContent("call_1", "first");
        var secondResult = new FunctionResultContent("call_2", "second")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["existing"] = true
            },
            Exception = new InvalidOperationException("boom")
        };
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[firstResult, secondResult]);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant response"),
            tool
        ], null);

        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[1]), expectedTtl: null);
        Assert.Equal(4, prepared.Messages.Count);
        var firstPrepared = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[2].Contents));
        var secondPrepared = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages[3].Contents));
        Assert.Same(firstResult, firstPrepared);
        Assert.NotSame(secondResult, secondPrepared);
        Assert.Equal("call_2", secondPrepared.CallId);
        Assert.Equal("second", secondPrepared.Result);
        Assert.Same(secondResult.Exception, secondPrepared.Exception);
        Assert.True((bool)secondPrepared.AdditionalProperties!["existing"]!);
        AssertAnthropicCacheControl(secondPrepared, expectedTtl: null);
        Assert.False(secondResult.AdditionalProperties?.ContainsKey(AnthropicCacheControlKey) ?? false);
        Assert.False(secondResult.AdditionalProperties?.ContainsKey(PromptCachingChatClient.CacheControlKey) ?? false);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("1h", "1h")]
    [InlineData("5m", "5m")]
    public void Prepare_AnthropicNative_WithTtl_AddsSdkTtl(string configuredTtl, string? expectedTtl)
    {
        var client = CreateAnthropicNativeClient("claude-opus-4-1", ttl: configuredTtl);

        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], null);

        var text = AssertLastTextContent(Assert.Single(prepared.Messages));
        AssertAnthropicCacheControl(text, expectedTtl);
    }

    [Fact]
    public void Prepare_AnthropicNative_WithInstructions_KeepsStableSystemBreakpointAndTail()
    {
        var client = CreateAnthropicNativeClient("claude-opus-4-1");

        var prepared = client.Prepare(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Instructions = "stable system prompt" });

        Assert.Equal(2, prepared.Messages.Count);
        Assert.Equal(ChatRole.System, prepared.Messages[0].Role);
        Assert.Equal(ChatRole.User, prepared.Messages[1].Role);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertAnthropicCacheControl(AssertLastTextContent(prepared.Messages[1]), expectedTtl: null);
        Assert.Equal([ChatRole.System.Value, ChatRole.User.Value], prepared.PendingCachePoints.Select(p => p.Trace.Role).ToArray());
        Assert.Equal(1, prepared.LlmCallIndex);
    }

    [Fact]
    public async Task Prepare_AnthropicNative_WithRememberedTail_PreservesSystemAndCapsAtFourBreakpoints()
    {
        var capture = new CaptureChatClient();
        var client = CreateAnthropicNativeClient("claude-opus-4-1", capture: capture);
        var options = new ChatOptions { Instructions = "stable system prompt" };

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_1", "tool one")])
        ], options);

        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_1", "tool one")]),
            new ChatMessage(ChatRole.Assistant, "assistant two"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call_2", "tool two")])
        ], options);

        Assert.True(prepared.PendingCachePoints.Count <= 4);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Role == ChatRole.System.Value);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Role == ChatRole.Tool.Value && p.Trace.Latest);
        Assert.Contains(prepared.PendingCachePoints, p => p.Trace.Remembered);
        Assert.Equal(2, prepared.LlmCallIndex);
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_SerializesUserAndToolResultCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicNativeHttpClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "result text")
            ])
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var messages = root.GetProperty("messages");
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        var userText = messages[0].GetProperty("content")[0];
        Assert.Equal("text", userText.GetProperty("type").GetString());
        Assert.Equal("hello", userText.GetProperty("text").GetString());
        AssertWireCacheControl(userText, expectedTtl: null);

        var toolResult = FindFirstContentBlock(root, "tool_result");
        Assert.Equal("call_1", toolResult.GetProperty("tool_use_id").GetString());
        AssertWireCacheControl(toolResult, expectedTtl: null);
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_SerializesSystemCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicNativeHttpClient(handler, ttl: "1h");

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "stable system prompt")
        ]);

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var system = document.RootElement.GetProperty("system")[0];
        Assert.Equal("text", system.GetProperty("type").GetString());
        Assert.Equal("stable system prompt", system.GetProperty("text").GetString());
        AssertWireCacheControl(system, expectedTtl: "1h");
    }

    [Fact]
    public async Task GetResponseAsync_AnthropicNative_WithInstructionsSerializesSystemAndTailCacheControl()
    {
        var handler = new AnthropicCaptureHandler();
        var client = CreateAnthropicNativeHttpClient(handler);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Instructions = "stable system prompt" });

        Assert.NotNull(handler.LastRequestJson);
        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var root = document.RootElement;
        var system = root.GetProperty("system")[0];
        Assert.Equal("stable system prompt", system.GetProperty("text").GetString());
        AssertWireCacheControl(system, expectedTtl: null);

        var userText = root.GetProperty("messages")[0].GetProperty("content")[0];
        Assert.Equal("hello", userText.GetProperty("text").GetString());
        AssertWireCacheControl(userText, expectedTtl: null);
    }

    [Fact]
    public void Prepare_DoesNotMutateOriginalMessagesOrContents()
    {
        var client = CreateClient("claude-opus-4-1");
        var text = new TextContent("hello");
        var user = new ChatMessage(ChatRole.User, (IList<AIContent>)[text]);

        var prepared = client.Prepare([user], null);

        Assert.NotSame(user, prepared.Messages[0]);
        Assert.Same(text, user.Contents[0]);
        Assert.Null(text.AdditionalProperties);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsesSamePreparationLogic()
    {
        var capture = new CaptureChatClient();
        var client = new PromptCachingChatClient(capture, new AppConfig.PromptCachingConfig(), "claude-opus-4-1");

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
        }

        var text = AssertLastTextContent(capture.LastMessages![0]);
        AssertCacheControl(text, expectedTtl: null);
    }

    [Fact]
    public void OpenAIAdapter_SingleTextMessageAddsRootMarkerForPipelineRewrite()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([new ChatMessage(ChatRole.User, "hello")], null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Single();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("hello", root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("hello", content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAIAdapter_ContentArrayMessagePreservesCacheControlOnTextBlock()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, (IList<AIContent>)[
                new TextContent("hello"),
                new TextContent("again")
            ])
        ], null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Single();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = root.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        var block = content[content.GetArrayLength() - 1];
        Assert.Equal("ephemeral", block.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAIAdapter_ToolResultMovesCacheControlToTextBlock()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "result text")
            ])
        ], null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Last();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("tool", root.GetProperty("role").GetString());
        Assert.Equal("call_1", root.GetProperty("tool_call_id").GetString());
        Assert.Equal("result text", root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.Equal("tool", message.GetProperty("role").GetString());
        Assert.Equal("call_1", message.GetProperty("tool_call_id").GetString());
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("result text", content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAIAdapter_MultiMessageRequestWritesPrefixAndTailCacheControls()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([
            new ChatMessage(ChatRole.System, "stable system prompt"),
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "result text")
            ])
        ], null);

        var messageJson = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Select(static message => ModelReaderWriter.Write(message).ToString());
        var body = $$"""{"messages":[{{string.Join(",", messageJson)}}]}""";
        using var document = JsonDocument.Parse(body);
        Assert.Equal(2, CountCacheControls(document.RootElement));

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(body);
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        Assert.Equal(2, CountCacheControls(rewrittenDocument.RootElement));
    }

    [Fact]
    public void OpenAIAdapter_TextContentToolResultMovesCacheControlToTextBlock()
    {
        var client = CreateClient("claude-opus-4-1");
        var toolResultContents = (IList<AIContent>)[
            new TextContent("line one"),
            new TextContent("line two")
        ];
        var expectedWireText = JsonSerializer.Serialize(toolResultContents, AIJsonUtilities.DefaultOptions);
        ChatMessage[] messages = [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", toolResultContents)
            ])
        ];
        var unmarkedOpenAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(messages, null)
            .Last();
        var unmarkedJson = ModelReaderWriter.Write(unmarkedOpenAiMessage).ToString();
        using var unmarkedDocument = JsonDocument.Parse(unmarkedJson);

        var prepared = client.Prepare(messages, null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Last();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("tool", root.GetProperty("role").GetString());
        Assert.Equal("call_1", root.GetProperty("tool_call_id").GetString());
        Assert.Equal(
            unmarkedDocument.RootElement.GetProperty("content").GetString(),
            root.GetProperty("content").GetString());
        Assert.Equal(expectedWireText, root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(expectedWireText, content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAIAdapter_JsonElementToolResultMovesCacheControlToTextBlock()
    {
        var client = CreateClient("claude-opus-4-1");
        const string resultJson = """{"answers":{"question_1":{"answers":["yes"]}}}""";
        var resultElement = JsonElementFrom(resultJson);
        ChatMessage[] messages = [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "RequestUserInput", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", resultElement)
            ])
        ];
        var originalResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[2].Contents));
        var unmarkedOpenAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(messages, null)
            .Last();
        var unmarkedJson = ModelReaderWriter.Write(unmarkedOpenAiMessage).ToString();
        using var unmarkedDocument = JsonDocument.Parse(unmarkedJson);
        var expectedWireText = unmarkedDocument.RootElement.GetProperty("content").GetString();

        var prepared = client.Prepare(messages, null);

        Assert.Null(originalResult.AdditionalProperties);
        Assert.Equal(resultElement.GetRawText(), Assert.IsType<JsonElement>(originalResult.Result).GetRawText());
        Assert.Contains(prepared.PendingCachePoints, p =>
            p.Trace.Role == ChatRole.Tool.Value &&
            p.Trace.ContentKind == "function_result" &&
            p.Trace.Latest);

        var preparedResult = Assert.IsType<FunctionResultContent>(Assert.Single(prepared.Messages.Last().Contents));
        Assert.NotSame(originalResult, preparedResult);
        Assert.Equal(resultElement.GetRawText(), Assert.IsType<JsonElement>(preparedResult.Result).GetRawText());
        AssertCacheControl(preparedResult, expectedTtl: null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Last();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("tool", root.GetProperty("role").GetString());
        Assert.Equal("call_1", root.GetProperty("tool_call_id").GetString());
        Assert.Equal(expectedWireText, root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(expectedWireText, content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_UsesPreviousTailBridgeAndLatestTail()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello")
        ]);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![0]), expectedTtl: null);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one")
        ]);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![0]), expectedTtl: null);
        AssertCacheControl(AssertSingleTextContent(capture.LastMessages![1]), expectedTtl: null);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.Assistant, "assistant two")
        ]);

        AssertNoCacheControl(AssertLastTextContent(capture.LastMessages![0]));
        AssertCacheControl(AssertSingleTextContent(capture.LastMessages![1]), expectedTtl: null);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![2]), expectedTtl: null);
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_NewUserTurnPreservesPreviousTailBridge()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one"),
            new ChatMessage(ChatRole.User, "next request")
        ]);

        AssertCacheControl(AssertLastTextContent(capture.LastMessages![1]), expectedTtl: null);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![2]), expectedTtl: null);
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_CompactedPrefixMarksSystemAndLatestUser()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "assistant one")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "compacted summary"),
            new ChatMessage(ChatRole.User, "next request")
        ]);

        var systemText = AssertLastTextContent(capture.LastMessages![0]);
        AssertCacheControl(systemText, expectedTtl: null);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![1]), expectedTtl: null);
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_WithSystemToolLoopUsesPrefixBridgeAndLatestTool()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        for (var completedRounds = 0; completedRounds < 6; completedRounds++)
            await client.GetResponseAsync(BuildToolLoopMessages(completedRounds));

        var prepared = client.Prepare(BuildToolLoopMessages(6), null);

        Assert.Collection(
            prepared.PendingCachePoints,
            point =>
            {
                Assert.Equal(ChatRole.System.Value, point.Trace.Role);
                Assert.Equal("text", point.Trace.ContentKind);
                Assert.True(point.Trace.Remembered);
                Assert.True(point.Trace.Latest);
            },
            point =>
            {
                Assert.Equal(ChatRole.Tool.Value, point.Trace.Role);
                Assert.Equal("function_result", point.Trace.ContentKind);
                Assert.True(point.Trace.Remembered);
                Assert.False(point.Trace.Latest);
            },
            point =>
            {
                Assert.Equal(ChatRole.Tool.Value, point.Trace.Role);
                Assert.Equal("function_result", point.Trace.ContentKind);
                Assert.False(point.Trace.Remembered);
                Assert.True(point.Trace.Latest);
            });
        AssertCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertNoCacheControl(AssertLastTextContent(prepared.Messages[1]));
        AssertNoCacheControl(AssertSingleTextContent(prepared.Messages.Last(m => m.Role == ChatRole.Assistant)));
        var toolMessages = prepared.Messages.Where(m => m.Role == ChatRole.Tool).ToArray();
        var bridgeTool = Assert.IsType<FunctionResultContent>(Assert.Single(toolMessages[^2].Contents));
        var latestTool = Assert.IsType<FunctionResultContent>(Assert.Single(toolMessages[^1].Contents));
        AssertCacheControl(bridgeTool, expectedTtl: null);
        AssertCacheControl(latestTool, expectedTtl: null);
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_WithJsonToolLoopUsesPrefixBridgeAndLatestTool()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);

        for (var completedRounds = 0; completedRounds < 6; completedRounds++)
            await client.GetResponseAsync(BuildJsonToolLoopMessages(completedRounds));

        var prepared = client.Prepare(BuildJsonToolLoopMessages(6), null);

        Assert.Collection(
            prepared.PendingCachePoints,
            point =>
            {
                Assert.Equal(ChatRole.System.Value, point.Trace.Role);
                Assert.Equal("text", point.Trace.ContentKind);
                Assert.True(point.Trace.Remembered);
                Assert.True(point.Trace.Latest);
            },
            point =>
            {
                Assert.Equal(ChatRole.Tool.Value, point.Trace.Role);
                Assert.Equal("function_result", point.Trace.ContentKind);
                Assert.True(point.Trace.Remembered);
                Assert.False(point.Trace.Latest);
            },
            point =>
            {
                Assert.Equal(ChatRole.Tool.Value, point.Trace.Role);
                Assert.Equal("function_result", point.Trace.ContentKind);
                Assert.False(point.Trace.Remembered);
                Assert.True(point.Trace.Latest);
            });
        AssertCacheControl(AssertLastTextContent(prepared.Messages[0]), expectedTtl: null);
        AssertNoCacheControl(AssertLastTextContent(prepared.Messages[1]));
        AssertNoCacheControl(AssertSingleTextContent(prepared.Messages.Last(m => m.Role == ChatRole.Assistant)));
        var toolMessages = prepared.Messages.Where(m => m.Role == ChatRole.Tool).ToArray();
        var bridgeTool = Assert.IsType<FunctionResultContent>(Assert.Single(toolMessages[^2].Contents));
        var latestTool = Assert.IsType<FunctionResultContent>(Assert.Single(toolMessages[^1].Contents));
        AssertCacheControl(bridgeTool, expectedTtl: null);
        AssertCacheControl(latestTool, expectedTtl: null);
    }

    [Fact]
    public void OpenAIAdapter_SystemOnlyCacheControlMovesToTextBlock()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([
            new ChatMessage(ChatRole.System, "stable system prompt")
        ], null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .First();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("system", root.GetProperty("role").GetString());
        Assert.Equal("stable system prompt", root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("stable system prompt", content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAIAdapter_AssistantTextWithFunctionCallMovesCacheControlToTextBlockAndPreservesToolCalls()
    {
        var client = CreateClient("claude-opus-4-1");
        var prepared = client.Prepare([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new TextContent("I will read that."),
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?> { ["path"] = "a.txt" })
            ])
        ], null);

        var openAiMessage = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(prepared.Messages, prepared.Options)
            .Last();
        var json = ModelReaderWriter.Write(openAiMessage).ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("assistant", root.GetProperty("role").GetString());
        Assert.True(root.TryGetProperty("tool_calls", out var toolCalls));
        Assert.Equal(1, toolCalls.GetArrayLength());
        Assert.Equal("call_1", toolCalls[0].GetProperty("id").GetString());
        Assert.Equal("function", toolCalls[0].GetProperty("type").GetString());
        var function = toolCalls[0].GetProperty("function");
        Assert.Equal("ReadFile", function.GetProperty("name").GetString());
        Assert.Equal("""{"path":"a.txt"}""", function.GetProperty("arguments").GetString());
        Assert.Equal("I will read that.", root.GetProperty("content").GetString());
        Assert.Equal("ephemeral", root.GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());

        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{json}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var message = rewrittenDocument.RootElement.GetProperty("messages")[0];
        Assert.False(message.TryGetProperty(PromptCachingChatClient.CacheControlKey, out _));
        Assert.True(message.TryGetProperty("tool_calls", out var rewrittenToolCalls));
        Assert.Equal(1, rewrittenToolCalls.GetArrayLength());
        Assert.Equal("call_1", rewrittenToolCalls[0].GetProperty("id").GetString());
        var content = message.GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("I will read that.", content[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_AssistantToolCallHistoryRestoresPreviousTailBridge()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);
        var assistant = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
            new TextContent("I will read that."),
            new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?> { ["path"] = "a.txt" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            assistant
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            assistant,
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "file contents")
            ]),
            new ChatMessage(ChatRole.User, "continue")
        ]);

        AssertCacheControl(AssertSingleTextContent(capture.LastMessages![1]), expectedTtl: null);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![3]), expectedTtl: null);

        var openAiMessages = OpenAI.Chat.MicrosoftExtensionsAIChatExtensions
            .AsOpenAIChatMessages(capture.LastMessages!, null)
            .ToList();
        var assistantJson = ModelReaderWriter.Write(openAiMessages[1]).ToString();
        var rewritten = PromptCacheControlPipelinePolicy.RewriteJson(
            $$"""{"messages":[{{assistantJson}}]}""");
        Assert.NotNull(rewritten);
        using var rewrittenDocument = JsonDocument.Parse(rewritten);
        var content = rewrittenDocument.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("ephemeral", content[0].GetProperty(PromptCachingChatClient.CacheControlKey).GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_ToolHistoryRestoresPreviousTailBridge()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);
        var tool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_1", (IList<AIContent>)[new TextContent("file contents")])
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "I will read that."),
            tool
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "I will read that."),
            tool,
            new ChatMessage(ChatRole.User, "continue")
        ]);

        AssertNoCacheControl(AssertLastTextContent(capture.LastMessages![1]));
        var toolResult = Assert.IsType<FunctionResultContent>(Assert.Single(capture.LastMessages![2].Contents));
        AssertCacheControl(toolResult, expectedTtl: null);
        AssertCacheControl(AssertLastTextContent(capture.LastMessages![3]), expectedTtl: null);
    }

    [Fact]
    public async Task OpenAICompatibleRollingBreakpoints_ContinuousEmptyAssistantToolLoopsKeepBridgeAndLatestToolResult()
    {
        var capture = new CaptureChatClient();
        var client = CreateClient("claude-opus-4-1", capture: capture);
        var firstTool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_1", "first result")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            firstTool
        ]);

        var secondTool = new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
            new FunctionResultContent("call_2", "second result")
        ]);
        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "ReadFile", new Dictionary<string, object?>())
            ]),
            firstTool,
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_2", "ReadFile", new Dictionary<string, object?>())
            ]),
            secondTool
        ]);

        var restoredTool = Assert.IsType<FunctionResultContent>(Assert.Single(capture.LastMessages![2].Contents));
        var latestTool = Assert.IsType<FunctionResultContent>(Assert.Single(capture.LastMessages![4].Contents));
        AssertCacheControl(restoredTool, expectedTtl: null);
        AssertCacheControl(latestTool, expectedTtl: null);
    }

    [Fact]
    public async Task GetResponseAsync_WhenTraceCollectorProvided_RecordsPromptCachePointSummaries()
    {
        const string sessionKey = "trace-cache";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var client = CreateClient(
            "claude-opus-4-1",
            capture: new CaptureChatClient(),
            sessionKey: sessionKey,
            traceCollector: collector);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "secret prompt"),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent("call_1", "secret tool result")
            ])
        ]);

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.PromptCachePoint);
        Assert.DoesNotContain("secret prompt", evt.MetadataJson);
        Assert.DoesNotContain("secret tool result", evt.MetadataJson);

        using var document = JsonDocument.Parse(evt.MetadataJson!);
        var root = document.RootElement;
        Assert.Equal(sessionKey, root.GetProperty("sessionKey").GetString());
        Assert.Equal("claude-opus-4-1", root.GetProperty("model").GetString());
        Assert.Equal(1, evt.LlmCallIndex);
        Assert.Equal(1, root.GetProperty("llmCallIndex").GetInt32());
        var points = root.GetProperty("points");
        Assert.Equal(1, points.GetArrayLength());
        Assert.Equal("tool", points[0].GetProperty("Role").GetString());
        Assert.Equal("function_result", points[0].GetProperty("ContentKind").GetString());
        Assert.True(points[0].GetProperty("Latest").GetBoolean());
    }

    [Fact]
    public async Task GetResponseAsync_WithJsonElementToolResult_RecordsToolPromptCachePoint()
    {
        const string sessionKey = "trace-json-cache";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var client = CreateClient(
            "claude-opus-4-1",
            capture: new CaptureChatClient(),
            sessionKey: sessionKey,
            traceCollector: collector);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "secret prompt"),
            new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call_1", "RequestUserInput", new Dictionary<string, object?>())
            ]),
            new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent(
                    "call_1",
                    JsonElementFrom("""{"answers":{"question_1":{"answers":["secret tool result"]}}}"""))
            ])
        ]);

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.PromptCachePoint);
        Assert.DoesNotContain("secret prompt", evt.MetadataJson);
        Assert.DoesNotContain("secret tool result", evt.MetadataJson);

        using var document = JsonDocument.Parse(evt.MetadataJson!);
        var points = document.RootElement.GetProperty("points");
        Assert.Equal(1, points.GetArrayLength());
        Assert.Equal("tool", points[0].GetProperty("Role").GetString());
        Assert.Equal("function_result", points[0].GetProperty("ContentKind").GetString());
        Assert.True(points[0].GetProperty("Latest").GetBoolean());
    }

    [Fact]
    public async Task GetResponseAsync_ForNonClaudeOrDisabled_DoesNotRecordPromptCachePointTrace()
    {
        var nonClaudeStore = new TraceStore();
        var nonClaudeClient = CreateClient(
            "gpt-4o-mini",
            capture: new CaptureChatClient(),
            sessionKey: "non-claude",
            traceCollector: new TraceCollector(nonClaudeStore));
        await nonClaudeClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.DoesNotContain(nonClaudeStore.GetEvents("non-claude"), e => e.Type == TraceEventType.PromptCachePoint);

        var disabledStore = new TraceStore();
        var disabledClient = new PromptCachingChatClient(
            new CaptureChatClient(),
            new AppConfig.PromptCachingConfig { Enabled = false },
            "claude-opus-4-1",
            new TraceCollector(disabledStore),
            () => "disabled");
        await disabledClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        Assert.DoesNotContain(disabledStore.GetEvents("disabled"), e => e.Type == TraceEventType.PromptCachePoint);
    }

    private static PromptCachingChatClient CreateClient(
        string model,
        string ttl = "",
        CaptureChatClient? capture = null,
        string? sessionKey = null,
        TraceCollector? traceCollector = null)
    {
        var key = sessionKey ?? Guid.NewGuid().ToString("N");
        return new(
            capture ?? new CaptureChatClient(),
            new AppConfig.PromptCachingConfig { Ttl = ttl },
            model,
            traceCollector,
            sessionKeyAccessor: () => key);
    }

    private static ChatMessage[] BuildToolLoopMessages(int completedRounds)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "stable system prompt"),
            new(ChatRole.User, "start tool loop")
        };

        for (var i = 0; i < completedRounds; i++)
        {
            var callId = $"call_{i}";
            messages.Add(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new TextContent($"assistant question {i}"),
                new FunctionCallContent(callId, "RequestUserInput", new Dictionary<string, object?>())
            ]));
            messages.Add(new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent(callId, $"answer {i}")
            ]));
        }

        return messages.ToArray();
    }

    private static ChatMessage[] BuildJsonToolLoopMessages(int completedRounds)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "stable system prompt"),
            new(ChatRole.User, "start tool loop")
        };

        for (var i = 0; i < completedRounds; i++)
        {
            var callId = $"call_{i}";
            messages.Add(new ChatMessage(ChatRole.Assistant, (IList<AIContent>)[
                new TextContent($"assistant question {i}"),
                new FunctionCallContent(callId, "RequestUserInput", new Dictionary<string, object?>())
            ]));
            messages.Add(new ChatMessage(ChatRole.Tool, (IList<AIContent>)[
                new FunctionResultContent(
                    callId,
                    JsonElementFrom("{\"answers\":{\"question_" + i + "\":{\"answers\":[\"answer " + i + "\"]}}}"))
            ]));
        }

        return messages.ToArray();
    }

    private static JsonElement JsonElementFrom(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static PromptCachingChatClient CreateAnthropicNativeClient(
        string model,
        string ttl = "",
        CaptureChatClient? capture = null,
        string? sessionKey = null,
        TraceCollector? traceCollector = null)
    {
        var key = sessionKey ?? Guid.NewGuid().ToString("N");
        return new(
            capture ?? new CaptureChatClient(),
            new AppConfig.PromptCachingConfig { Ttl = ttl },
            model,
            PromptCacheMarkerStrategy.AnthropicNative,
            traceCollector,
            sessionKeyAccessor: () => key);
    }

    private static PromptCachingChatClient CreateAnthropicNativeHttpClient(
        AnthropicCaptureHandler handler,
        string model = "claude-haiku-4-5",
        string ttl = "")
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        return new PromptCachingChatClient(
            anthropicClient.AsIChatClient(model),
            new AppConfig.PromptCachingConfig { Ttl = ttl },
            model,
            PromptCacheMarkerStrategy.AnthropicNative,
            sessionKeyAccessor: () => Guid.NewGuid().ToString("N"));
    }

    private static void AssertCacheControl(AIContent content, string? expectedTtl)
    {
        Assert.NotNull(content.AdditionalProperties);
        var cacheControl = Assert.IsType<Dictionary<string, object>>(content.AdditionalProperties![PromptCachingChatClient.CacheControlKey]);
        Assert.Equal("ephemeral", cacheControl["type"]);
        if (expectedTtl is null)
        {
            Assert.False(cacheControl.ContainsKey("ttl"));
        }
        else
        {
            Assert.Equal(expectedTtl, cacheControl["ttl"]);
        }
    }

    private static void AssertNoCacheControl(AIContent content)
    {
        Assert.False(content.AdditionalProperties?.ContainsKey(PromptCachingChatClient.CacheControlKey) ?? false);
    }

    private static void AssertAnthropicCacheControl(AIContent content, string? expectedTtl)
    {
        Assert.NotNull(content.AdditionalProperties);
        Assert.False(content.AdditionalProperties!.ContainsKey(PromptCachingChatClient.CacheControlKey));
        var cacheControl = Assert.IsType<AnthropicCacheControlEphemeral>(content.AdditionalProperties[AnthropicCacheControlKey]);
        Assert.Equal("ephemeral", cacheControl.Type.GetString());
        if (expectedTtl is null)
            Assert.Null(cacheControl.Ttl);
        else
            Assert.Equal(expectedTtl, cacheControl.Ttl!.Raw());
    }

    private static void AssertWireCacheControl(JsonElement block, string? expectedTtl)
    {
        var cacheControl = block.GetProperty(PromptCachingChatClient.CacheControlKey);
        Assert.Equal("ephemeral", cacheControl.GetProperty("type").GetString());
        if (expectedTtl is null)
            Assert.False(cacheControl.TryGetProperty("ttl", out _));
        else
            Assert.Equal(expectedTtl, cacheControl.GetProperty("ttl").GetString());
    }

    private static JsonElement FindFirstContentBlock(JsonElement root, string type)
    {
        foreach (var message in root.GetProperty("messages").EnumerateArray())
        {
            foreach (var block in message.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var blockType) &&
                    string.Equals(blockType.GetString(), type, StringComparison.Ordinal))
                {
                    return block;
                }
            }
        }

        throw new InvalidOperationException($"Content block '{type}' was not found.");
    }

    private static TextContent AssertLastTextContent(ChatMessage message) =>
        Assert.IsType<TextContent>(message.Contents.Last());

    private static TextContent AssertSingleTextContent(ChatMessage message) =>
        Assert.Single(message.Contents.OfType<TextContent>());

    private static int CountCacheControls(JsonElement element)
    {
        var count = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, PromptCachingChatClient.CacheControlKey, StringComparison.Ordinal))
                    count++;
                count += CountCacheControls(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                count += CountCacheControls(item);
        }

        return count;
    }

    private sealed class CaptureChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class AnthropicCaptureHandler : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "id": "msg_cache_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-haiku-4-5",
                        "content": [{
                            "type": "text",
                            "text": "ok"
                        }],
                        "stop_reason": "end_turn",
                        "usage": {
                            "input_tokens": 10,
                            "output_tokens": 1
                        }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
