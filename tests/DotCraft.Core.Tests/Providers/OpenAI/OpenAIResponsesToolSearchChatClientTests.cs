using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DotCraft.AppServer;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIResponsesToolSearchChatClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateResponseOptions_EmitsStreamingNativeToolSearchAndToolSearchOutput()
    {
        var registry = new DeferredToolRegistry([AIFunctionFactory.Create(
            (string path) => $"read {path}",
            name: "ReadFile",
            description: "Read a file")]);
        var searchTool = new NativeToolSearchTool(registry);
        var loadedTool = NativeToolSearchTool.ToOutputTool(registry.DeferredTools["ReadFile"]);
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Find a file tool."),
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent(
                    "search-call",
                    NativeToolSearchTool.ToolName,
                    new Dictionary<string, object?> { ["query"] = "file" })
            ]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent(
                    "search-call",
                    new NativeToolSearchOutput([loadedTool]))
            ])
        };

        using var document = JsonDocument.Parse(CreateRequestJson("gpt-test", messages, new ChatOptions
        {
            Tools = [searchTool]
        }));

        var root = document.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("store").GetBoolean());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("tool_search", tool.GetProperty("type").GetString());
        Assert.Equal("client", tool.GetProperty("execution").GetString());
        var parameters = tool.GetProperty("parameters");
        Assert.Equal("object", parameters.GetProperty("type").GetString());
        Assert.Equal("string", parameters.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        Assert.Equal("integer", parameters.GetProperty("properties").GetProperty("max_results").GetProperty("type").GetString());
        Assert.Equal("query", Assert.Single(parameters.GetProperty("required").EnumerateArray()).GetString());
        Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());

        var call = root.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "tool_search_call");
        Assert.Equal("client", call.GetProperty("execution").GetString());
        Assert.Equal("completed", call.GetProperty("status").GetString());
        Assert.Equal("file", call.GetProperty("arguments").GetProperty("query").GetString());
        Assert.False(call.TryGetProperty("query", out _));

        var output = root.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "tool_search_output");
        Assert.Equal("client", output.GetProperty("execution").GetString());
        Assert.Equal("completed", output.GetProperty("status").GetString());
        var loaded = output.GetProperty("tools")[0];
        Assert.Equal("ReadFile", loaded.GetProperty("name").GetString());
        Assert.False(loaded.GetProperty("strict").GetBoolean());
        Assert.True(loaded.GetProperty("defer_loading").GetBoolean());
    }

    [Fact]
    public void CreateResponseOptions_PreservesUserImageContentParts()
    {
        var dataImage = new DataContent(CreateImageBytes("image/png"), "image/png");
        var remoteImage = new UriContent("https://example.test/cat.jpg", "image/jpeg");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.User, (IList<AIContent>)
                [
                    new TextContent("Look at this:"),
                    dataImage,
                    new TextContent(" and this:"),
                    remoteImage
                ])
            ],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var message = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("message", message.GetProperty("type").GetString());
        Assert.Equal("user", message.GetProperty("role").GetString());

        var content = message.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(4, content.Length);
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("Look at this:", content[0].GetProperty("text").GetString());
        Assert.Equal("input_image", content[1].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", content[1].GetProperty("image_url").GetString(), StringComparison.Ordinal);
        Assert.Equal("input_text", content[2].GetProperty("type").GetString());
        Assert.Equal(" and this:", content[2].GetProperty("text").GetString());
        Assert.Equal("input_image", content[3].GetProperty("type").GetString());
        Assert.Equal("https://example.test/cat.jpg", content[3].GetProperty("image_url").GetString());
    }

    [Fact]
    public void CreateResponseOptions_TranscodesUserBmpContentPartToPng()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.User, (IList<AIContent>)
                [
                    new DataContent(CreateImageBytes("image/bmp"), "image/bmp")
                ])
            ],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var message = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());

        Assert.Equal("input_image", content.GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", content.GetProperty("image_url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseOptions_DegradesInvalidImageContentWithVisiblePlaceholder()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.User, (IList<AIContent>)
                [
                    new DataContent(new byte[] { 1, 2, 3 }, "image/bmp")
                ])
            ],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var message = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());

        Assert.Equal("input_text", content.GetProperty("type").GetString());
        Assert.Equal(ModelImageInputPreparer.CouldNotProcessPlaceholder, content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithNativeToolSearch_PreservesPromotedImageMessage()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport();
        using var client = CreateClient(inner, transport);

        _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [
                new ChatMessage(ChatRole.User, (IList<AIContent>)
                [
                    new TextContent("[Image content from tool results - attached for vision analysis.]"),
                    new DataContent(CreateImageBytes("image/png"), "image/png")
                ])
            ],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        using var request = JsonDocument.Parse(SerializeOptions(Assert.Single(transport.Requests)));
        var message = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray());
        var content = message.GetProperty("content").EnumerateArray().ToArray();

        Assert.Equal(2, content.Length);
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("[Image content from tool results - attached for vision analysis.]", content[0].GetProperty("text").GetString());
        Assert.Equal("input_image", content[1].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", content[1].GetProperty("image_url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateHostedImageGenerationContent_GeneratingStatus_ReturnsFalse()
    {
        var item = CreateHostedImageGenerationCallItem(
            "ig_generating",
            "generating",
            "A red square",
            resultBase64: null);

        Assert.False(ResponsesToolSearchMapper.TryCreateHostedImageGenerationContent(item, out _));
    }

    [Fact]
    public void TryCreateHostedImageGenerationContent_CompletedStatus_ReturnsHostedContent()
    {
        var imageBytes = CreateImageBytes("image/png");
        var item = CreateHostedImageGenerationCallItem(
            "ig_123",
            "completed",
            "A red square",
            Convert.ToBase64String(imageBytes));

        Assert.True(ResponsesToolSearchMapper.TryCreateHostedImageGenerationContent(item, out var content));
        Assert.True(content.Succeeded);
        Assert.Equal("ig_123", content.Id);
        Assert.Equal("A red square", content.RevisedPrompt);
        Assert.Equal("image/png", content.MediaType);
        Assert.Equal(imageBytes, content.ImageBytes);
    }

    [Fact]
    public void TryCreateHostedImageGenerationContent_InvalidBase64_ReturnsVisibleErrorContent()
    {
        var update = CreateStreamingUpdate("""
            {
              "type": "response.image_generation_call.completed",
              "sequence_number": 1,
              "item_id": "ig_bad",
              "output_index": 0,
              "revised_prompt": "Broken",
              "result": "not-base64"
            }
            """);

        Assert.True(ResponsesToolSearchMapper.TryCreateHostedImageGenerationContent(update, out var content));
        Assert.False(content.Succeeded);
        Assert.Equal("ig_bad", content.Id);
        Assert.Equal("Image generation returned invalid image data.", content.ErrorMessage);
        Assert.Null(content.ImageBytes);
    }

    [Fact]
    public void CreateResponseOptions_DegradesUnsupportedContentWithVisiblePlaceholder()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.User, (IList<AIContent>)
                [
                    new TextContent("Read this:"),
                    new DataContent(new byte[] { 4, 5, 6 }, "application/pdf"),
                    new UnknownContent()
                ])
            ],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var message = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        var content = message.GetProperty("content").EnumerateArray().ToArray();

        Assert.Equal(3, content.Length);
        Assert.Equal("Read this:", content[0].GetProperty("text").GetString());
        Assert.Equal("[Unsupported content: DataContent (application/pdf)]", content[1].GetProperty("text").GetString());
        Assert.Equal("[Unsupported content: UnknownContent]", content[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task NativeToolSearchTool_EmitsNamespacedDeferredDynamicTool()
    {
        var dynamicTool = new DeferredDynamicFunction(
            "CreateBoardTask",
            "Create an Workflow App board task.");
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(dynamicTool, "dynamic", "workflow")],
            DeferredToolLoadingMode.Native);
        var searchTool = new NativeToolSearchTool(registry);

        var result = await searchTool.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "board task"
        });
        var output = Assert.IsType<NativeToolSearchOutput>(result);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions));
        var namespaceTool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("namespace", namespaceTool.GetProperty("type").GetString());
        Assert.Equal("workflow", namespaceTool.GetProperty("name").GetString());
        Assert.Equal("Tools in the workflow namespace.", namespaceTool.GetProperty("description").GetString());

        var child = Assert.Single(namespaceTool.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", child.GetProperty("type").GetString());
        Assert.Equal("CreateBoardTask", child.GetProperty("name").GetString());
        Assert.Equal("object", child.GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(child.GetProperty("strict").GetBoolean());
        Assert.True(child.GetProperty("defer_loading").GetBoolean());

        var display = ImageContentSanitizingChatClient.DescribeResult(output);
        Assert.Contains("Found 1 matching tool(s):", display);
        Assert.Contains("workflow.CreateBoardTask", display);
        Assert.Contains("Create an Workflow App board task.", display);
        Assert.DoesNotContain(nameof(NativeToolSearchOutput), display);
    }

    [Fact]
    public async Task NativeToolSearchTool_RecordsDeferredToolLoadingTraceOnceForNewActivations()
    {
        const string sessionKey = "native-deferred-loading-trace";
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var dynamicTool = new DeferredDynamicFunction(
            "CreateBoardTask",
            "Create an Workflow App board task.");
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(dynamicTool, "dynamic", "workflow")],
            DeferredToolLoadingMode.Native);
        var searchTool = new NativeToolSearchTool(
            registry,
            maxSearchResults: 5,
            new DeferredToolLoadingTraceContext(
                collector,
                "Auto",
                "Native",
                ModelProviderProtocols.OpenAIResponses,
                NativeToolSearchTool.ToolName,
                DeferredToolCount: 1,
                MaxSearchResults: 5));
        var previousSessionKey = TracingChatClient.CurrentSessionKey;

        try
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;
            await searchTool.InvokeAsync(new AIFunctionArguments
            {
                ["query"] = "board task",
                ["max_results"] = 5
            });
            await searchTool.InvokeAsync(new AIFunctionArguments
            {
                ["query"] = "board task",
                ["max_results"] = 5
            });
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.DeferredToolLoading);
        Assert.Equal("1 deferred tool activated", evt.ToolName);
        Assert.Equal("CreateBoardTask", evt.Content);
        Assert.NotNull(evt.ChangedToolNames);
        Assert.Equal(["CreateBoardTask"], evt.ChangedToolNames);
        Assert.Null(evt.PromptCacheEventKind);
        Assert.Null(evt.PromptCacheChangedFields);

        Assert.NotNull(evt.MetadataJson);
        using var metadata = JsonDocument.Parse(evt.MetadataJson);
        var root = metadata.RootElement;
        Assert.Equal("Auto", root.GetProperty("strategy").GetString());
        Assert.Equal("Native", root.GetProperty("effectiveMode").GetString());
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, root.GetProperty("providerProtocol").GetString());
        Assert.Equal(NativeToolSearchTool.ToolName, root.GetProperty("trigger").GetString());
        Assert.Equal("openai_responses_tool_search_output", root.GetProperty("wireShape").GetString());
        Assert.Equal("board task", root.GetProperty("query").GetString());
        Assert.Equal(1, root.GetProperty("deferredToolCount").GetInt32());
        Assert.Equal(5, root.GetProperty("requestedMaxResults").GetInt32());
        Assert.Equal(5, root.GetProperty("maxSearchResults").GetInt32());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("CreateBoardTask", tool.GetProperty("name").GetString());
        Assert.Equal("dynamic", tool.GetProperty("source").GetString());
        Assert.Equal("workflow", tool.GetProperty("namespace").GetString());

        var session = store.GetSession(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(0, session.PromptDriftCount);
        Assert.Null(session.LastPromptCacheChangeKind);
        Assert.Empty(session.LastPromptCacheChangedFields);
    }

    [Fact]
    public void CreateResponseOptions_EmitsNamespacedDynamicToolDefinition()
    {
        var dynamicTool = CreateRuntimeDynamicTool("image_gen", "imagegen");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "make an image")],
            new ChatOptions
            {
                Tools = [dynamicTool]
            }));

        var namespaceTool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("namespace", namespaceTool.GetProperty("type").GetString());
        Assert.Equal("image_gen", namespaceTool.GetProperty("name").GetString());
        var function = Assert.Single(namespaceTool.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", function.GetProperty("type").GetString());
        Assert.Equal("imagegen", function.GetProperty("name").GetString());
        Assert.Equal("Generate an image.", function.GetProperty("description").GetString());
        Assert.Equal("object", function.GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(function.TryGetProperty("defer_loading", out _));
    }

    [Fact]
    public void CreateResponseOptions_UsesOneResolvedNamespaceDescription()
    {
        var first = new TestFunction("first", "First tool.", "mcp__catalog", "Catalog tools.");
        var second = new TestFunction("second", "Second tool.", "mcp__catalog", "Catalog tools.");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "use catalog")],
            new ChatOptions { Tools = [first, second] }));

        var namespaceTool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("mcp__catalog", namespaceTool.GetProperty("name").GetString());
        Assert.Equal("Catalog tools.", namespaceTool.GetProperty("description").GetString());
        Assert.Equal(2, namespaceTool.GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public void CreateResponseOptions_ConflictingNamespaceDescriptionsUseGenericDescription()
    {
        var first = new TestFunction("first", "First tool.", "workflow", "First description.");
        var second = new TestFunction("second", "Second tool.", "workflow", "Second description.");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "use workflow")],
            new ChatOptions { Tools = [first, second] }));

        var namespaceTool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("Tools in the workflow namespace.", namespaceTool.GetProperty("description").GetString());
        Assert.Equal(2, namespaceTool.GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public void CreateResponseOptions_EmitsHostedImageGenerationToolWhenEnabled()
    {
        var dynamicTool = CreateRuntimeDynamicTool("image_gen", "imagegen");
        var options = new ChatOptions
        {
            Tools = [dynamicTool]
        };
        ResponsesToolSearchMapper.EnableHostedImageGeneration(options);

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "make an image")],
            options));

        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);
        var hostedTool = Assert.Single(tools, tool => tool.GetProperty("type").GetString() == "image_generation");
        Assert.Equal("image_generation", hostedTool.GetProperty("type").GetString());
        Assert.Equal("png", hostedTool.GetProperty("output_format").GetString());
        Assert.False(hostedTool.TryGetProperty("name", out _));
        Assert.Contains(
            tools,
            tool => tool.GetProperty("type").GetString() == "namespace"
                    && tool.GetProperty("name").GetString() == "image_gen");
    }

    [Fact]
    public void CreateResponseOptions_RejectsInvalidProviderIdentityLocally()
    {
        var invalidTool = new TestFunction("read", "Read.", "code-host-apps:code-host-apps");

        var error = Assert.Throws<InvalidOperationException>(() => CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "read")],
            new ChatOptions { Tools = [invalidTool] }));

        Assert.StartsWith("invalid_provider_tool_identity:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseOptions_ReplaysHostedImageGenerationCallInput()
    {
        var imageBytes = CreateImageBytes("image/png");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.Assistant, [
                    new HostedImageGenerationContent
                    {
                        Id = "ig_123",
                        Status = "completed",
                        RevisedPrompt = "A red square",
                        ImageBytes = imageBytes
                    }
                ])
            ],
            new ChatOptions()));

        var item = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("image_generation_call", item.GetProperty("type").GetString());
        Assert.Equal("ig_123", item.GetProperty("id").GetString());
        Assert.Equal("completed", item.GetProperty("status").GetString());
        Assert.Equal("A red square", item.GetProperty("revised_prompt").GetString());
        Assert.Equal(Convert.ToBase64String(imageBytes), item.GetProperty("result").GetString());
    }

    [Fact]
    public void CreateResponseOptions_ReplacesUnprefixedHostedImageItemId()
    {
        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [
                new ChatMessage(ChatRole.Assistant, [
                    new HostedImageGenerationContent
                    {
                        Id = "invalidid",
                        Status = "completed",
                        ImageBytes = CreateImageBytes("image/png")
                    }
                ])
            ],
            new ChatOptions());
        using var document = JsonDocument.Parse(SerializeOptions(request.Options));

        var item = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.StartsWith("ig_", item.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.NotEqual("invalidid", item.GetProperty("id").GetString());
        Assert.Equal(1, request.Shape.InputItemIdInvalidSourceCount);
        Assert.Equal(1, request.Shape.InputItemIdGeneratedCount);
        Assert.Equal(0, request.Shape.InputItemIdMissingCount);
    }

    [Fact]
    public void CreateResponseOptions_GeneratesPrefixedHostedImageItemId()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.Assistant, [
                    new HostedImageGenerationContent
                    {
                        Status = "completed",
                        ImageBytes = CreateImageBytes("image/png")
                    }
                ])
            ],
            new ChatOptions()));

        var item = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.StartsWith("ig_", item.GetProperty("id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseOptions_AssignsStableDistinctIdsAcrossModelHistoryReplay()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "create a task"),
            new ChatMessage(ChatRole.Assistant, [
                new TextReasoningContent("") { ProtectedData = "encrypted-reasoning-payload" },
                new TextContent("Creating it."),
                new FunctionCallContent(
                    "create-call",
                    "CreateBoardTask",
                    new Dictionary<string, object?> { ["title"] = "test task" })
            ]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("create-call", "Created task.")
            ])
        };
        var codec = new ModelHistoryCodec();
        var persistedHistory = messages.Select(message => codec.Encode(message, "turn_001")).ToArray();
        var firstReplay = persistedHistory.Select(codec.Decode).ToArray();
        var secondReplay = persistedHistory.Select(codec.Decode).ToArray();

        using var first = JsonDocument.Parse(CreateRequestJson("gpt-test", firstReplay, new ChatOptions()));
        using var second = JsonDocument.Parse(CreateRequestJson("gpt-test", secondReplay, new ChatOptions()));

        var firstItems = first.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var secondItems = second.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var firstIds = firstItems.Select(item => item.GetProperty("id").GetString()).ToArray();
        var secondIds = secondItems.Select(item => item.GetProperty("id").GetString()).ToArray();

        Assert.Equal(firstIds, secondIds);
        Assert.Equal(firstIds.Length, firstIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(firstIds, id => id!.StartsWith("msg_", StringComparison.Ordinal));
        Assert.Contains(firstIds, id => id!.StartsWith("rs_", StringComparison.Ordinal));
        Assert.Contains(firstIds, id => id!.StartsWith("fc_", StringComparison.Ordinal));
        Assert.Contains(firstIds, id => id!.StartsWith("fco_", StringComparison.Ordinal));

        var call = Assert.Single(firstItems, item => item.GetProperty("type").GetString() == "function_call");
        var output = Assert.Single(firstItems, item => item.GetProperty("type").GetString() == "function_call_output");
        Assert.Equal("create-call", call.GetProperty("call_id").GetString());
        Assert.Equal("create-call", output.GetProperty("call_id").GetString());

        var persisted = firstReplay.Select(message => codec.Encode(message, "turn_001")).ToArray();
        Assert.Contains(
            OpenAIResponsesItemIdentity.MetadataKey,
            JsonSerializer.Serialize(persisted, SessionJsonOptions.Default),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseOptions_PreservesValidProviderItemIds()
    {
        var call = new FunctionCallContent(
            "call-1",
            "ReadFile",
            new Dictionary<string, object?>())
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["id"] = "fc_provider"
            }
        };

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.Assistant, [call])],
            new ChatOptions()));

        var item = Assert.Single(document.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("fc_provider", item.GetProperty("id").GetString());
    }

    [Fact]
    public void CreateResponseOptions_LeavesFlatFunctionToolDefinitionUnchanged()
    {
        var tool = new TestFunction("imagegen", "Generate an image.");

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "make an image")],
            new ChatOptions
            {
                Tools = [tool]
            }));

        var functionTool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", functionTool.GetProperty("type").GetString());
        Assert.Equal("imagegen", functionTool.GetProperty("name").GetString());
        Assert.Equal("Generate an image.", functionTool.GetProperty("description").GetString());
        Assert.Equal("object", functionTool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(functionTool.TryGetProperty("namespace", out _));
        Assert.False(functionTool.TryGetProperty("tools", out _));
    }

    [Fact]
    public void CreateResponseOptions_NormalizesLegacyToolSearchArguments()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [
                new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent(
                        "search-call",
                        NativeToolSearchTool.ToolName,
                        new Dictionary<string, object?>
                        {
                            ["q"] = "file",
                            ["maxResults"] = 3
                        })
                ])
            ],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var call = document.RootElement.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "tool_search_call");
        var arguments = call.GetProperty("arguments");
        Assert.Equal("file", arguments.GetProperty("query").GetString());
        Assert.Equal(3, arguments.GetProperty("max_results").GetInt32());
        Assert.False(arguments.TryGetProperty("q", out _));
        Assert.False(arguments.TryGetProperty("maxResults", out _));
    }

    [Fact]
    public void CreateResponseOptions_EmitsReasoningIncludeAndPromptCacheKey()
    {
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = "thread-cache-key";
            using var requestContext = TestModelProviderRegistry.PushRequestContext("thread-cache-key");

            using var document = JsonDocument.Parse(CreateRequestJson(
                "gpt-test",
                [new ChatMessage(ChatRole.User, "think")],
                new ChatOptions
                {
                    Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                    Reasoning = new ReasoningOptions
                    {
                        Effort = ReasoningEffort.High,
                        Output = ReasoningOutput.Summary
                    }
                }));

            var root = document.RootElement;
            Assert.True(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("auto", root.GetProperty("reasoning").GetProperty("summary").GetString());
            Assert.Contains(
                root.GetProperty("include").EnumerateArray(),
                item => item.GetString() == "reasoning.encrypted_content");
            Assert.Equal("thread-cache-key", root.GetProperty("prompt_cache_key").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }

    [Fact]
    public void CreateResponseOptions_EmitsEmptyReasoningObjectWhenUnconfigured()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "think")],
            new ChatOptions()));

        var reasoning = document.RootElement.GetProperty("reasoning");
        Assert.Equal(JsonValueKind.Object, reasoning.ValueKind);
        Assert.Empty(reasoning.EnumerateObject());
    }

    [Fact]
    public void CreateResponseOptions_MapsExtraHighReasoningEffortToOpenAIToken()
    {
        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "think hard")],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                Reasoning = new ReasoningOptions
                {
                    Effort = ReasoningEffort.ExtraHigh
                }
            }));

        Assert.Equal("xhigh", document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public void CreateResponseOptions_PrefersPromptCacheKeyFromAdditionalProperties()
    {
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = "async-local-cache-key";

            using var document = JsonDocument.Parse(CreateRequestJson(
                "gpt-test",
                [new ChatMessage(ChatRole.User, "think")],
                new ChatOptions
                {
                    Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "snapshot-thread-cache-key"
                    }
                }));

            Assert.Equal(
                "snapshot-thread-cache-key",
                document.RootElement.GetProperty("prompt_cache_key").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }

    [Fact]
    public void CreateResponseRequestShape_RecordsOrderedByteHashesForPrefixComparison()
    {
        var options = new ChatOptions
        {
            Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
            Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.High
            },
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "thread-secret-cache-key"
            }
        };
        var first = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [
                new ChatMessage(ChatRole.System, "secret stable system"),
                new ChatMessage(ChatRole.User, "secret stable user prompt")
            ],
            options).Shape;
        var appended = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [
                new ChatMessage(ChatRole.System, "secret stable system"),
                new ChatMessage(ChatRole.User, "secret stable user prompt"),
                new ChatMessage(ChatRole.Assistant, "secret assistant tail")
            ],
            options).Shape;
        var changed = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [
                new ChatMessage(ChatRole.System, "secret stable system"),
                new ChatMessage(ChatRole.User, "secret changed user prompt")
            ],
            options).Shape;

        Assert.Equal(first.InputItemHashes, appended.InputItemHashes.Take(first.InputItemCount).ToArray());
        Assert.NotEqual(first.InputItemHashes[0], changed.InputItemHashes[0]);
        Assert.All(appended.InputItemHashes, hash => Assert.StartsWith("sha256:", hash, StringComparison.Ordinal));
        Assert.StartsWith("sha256:", appended.InputHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", appended.ToolsHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", appended.ReasoningHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", appended.PromptCacheKeyHash, StringComparison.Ordinal);

        var shapeJson = JsonSerializer.Serialize(appended, JsonOptions);
        Assert.DoesNotContain("secret stable user prompt", shapeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret assistant tail", shapeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret stable system", shapeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("thread-secret-cache-key", shapeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseOptions_MapsToolModeToResponsesToolChoice()
    {
        var tools = new List<AITool> { new TestFunction("lookup", "Look up a value.") };

        AssertToolChoice(ChatToolMode.None, "\"none\"");
        AssertToolChoice(ChatToolMode.Auto, "\"auto\"");
        AssertToolChoice(ChatToolMode.RequireAny, "\"required\"");
        AssertToolChoice(ChatToolMode.RequireSpecific("lookup"), """{"type":"function","name":"lookup"}""");

        void AssertToolChoice(ChatToolMode toolMode, string expectedJson)
        {
            var options = ResponsesToolSearchMapper.CreateResponseOptions(
                "gpt-test",
                [new ChatMessage(ChatRole.User, "use a tool")],
                new ChatOptions
                {
                    Tools = tools,
                    ToolMode = toolMode
                });
            using var document = JsonDocument.Parse(SerializeOptions(options));
            using var expected = JsonDocument.Parse(expectedJson);
            Assert.Equal(
                expected.RootElement.GetRawText(),
                document.RootElement.GetProperty("tool_choice").GetRawText());
        }
    }

    [Fact]
    public void CreateResponseOptions_DoesNotEmitToolChoiceWithoutTools()
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "do not use tools")],
            new ChatOptions
            {
                AllowMultipleToolCalls = true,
                ToolMode = ChatToolMode.None
            });

        using var document = JsonDocument.Parse(SerializeOptions(options));

        Assert.False(document.RootElement.TryGetProperty("tool_choice", out _));
        Assert.False(document.RootElement.TryGetProperty("parallel_tool_calls", out _));
    }

    [Fact]
    public void CreateResponseOptions_MapsStrictStructuredResponseFormat()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"string"}}}""");
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "return json")],
            new ChatOptions
            {
                ResponseFormat = new ChatResponseFormatJson(
                    schema.RootElement.Clone(),
                    "result",
                    "A result."),
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["strict"] = true
                }
            });

        using var document = JsonDocument.Parse(SerializeOptions(options));
        var format = document.RootElement.GetProperty("text").GetProperty("format");

        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("result", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal(
            ["value"],
            format.GetProperty("schema").GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public void CreateResponseOptions_TransformsStructuredSchemaEvenWhenStrictIsFalse()
    {
        using var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "value": {
                  "type": "string",
                  "minLength": 2
                }
              }
            }
            """);
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "return json")],
            new ChatOptions
            {
                ResponseFormat = new ChatResponseFormatJson(
                    schema.RootElement.Clone(),
                    "result"),
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["strict"] = false
                }
            });

        using var document = JsonDocument.Parse(SerializeOptions(options));
        var format = document.RootElement.GetProperty("text").GetProperty("format");
        var valueSchema = format.GetProperty("schema").GetProperty("properties").GetProperty("value");

        Assert.False(format.GetProperty("strict").GetBoolean());
        Assert.False(valueSchema.TryGetProperty("minLength", out _));
        Assert.Contains("minLength: 2", valueSchema.GetProperty("description").GetString());
    }

    [Fact]
    public void CreateResponseRequest_PreservesProviderNativeRawOptionPrecedence()
    {
        var options = new ChatOptions
        {
            Temperature = 0.1f,
            ToolMode = ChatToolMode.RequireAny,
            Tools = [new TestFunction("lookup", "Look up a value.")],
            ResponseFormat = ChatResponseFormat.Json,
            RawRepresentationFactory = _ =>
                new CreateResponseOptions
                {
                    Temperature = 0.9f,
                    ToolChoice = ResponseToolChoice.CreateNoneChoice(),
                    TextOptions = new ResponseTextOptions
                    {
                        TextFormat = ResponseTextFormat.CreateTextFormat()
                    }
                }
        };

        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "raw options")],
            options,
            rawRepresentationClient: new FakeChatClient(new ChatResponse([])));
        using var document = JsonDocument.Parse(SerializeOptions(request.Options));

        Assert.Equal(0.9, document.RootElement.GetProperty("temperature").GetDouble(), precision: 5);
        Assert.Equal("none", document.RootElement.GetProperty("tool_choice").GetString());
        Assert.Equal(
            "text",
            document.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public void CreateResponseRequestShape_RecordsSanitizedEffectiveOptions()
    {
        var shape = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "sample user prompt")],
            new ChatOptions
            {
                MaxOutputTokens = 1234,
                ToolMode = ChatToolMode.RequireAny,
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                Reasoning = new ReasoningOptions
                {
                    Effort = ReasoningEffort.High
                }
            },
            removesUnsupportedOAuthResponsesFields: true).Shape;

        var shapeJson = JsonSerializer.Serialize(shape, JsonOptions);

        Assert.Equal(1234, shape.MaxOutputTokensRequested);
        Assert.False(shape.MaxOutputTokensPresentAfterOAuthRewrite);
        Assert.True(shape.MaxOutputTokensRemovedByOAuthRewrite);
        Assert.Equal("high", shape.ReasoningEffort);
        Assert.Equal("Required", shape.ToolChoiceKind);
        Assert.Equal(1, shape.ToolCount);
        Assert.True(shape.StreamingEnabled);
        Assert.True(shape.InputBytes > 0);
        Assert.Equal(shape.InputItemCount, shape.InputItemIdEligibleCount);
        Assert.Equal(shape.InputItemIdEligibleCount, shape.InputItemIdPresentCount);
        Assert.Equal(0, shape.InputItemIdMissingCount);
        Assert.Equal(0, shape.InputItemIdInvalidSourceCount);
        Assert.DoesNotContain("sample user prompt", shapeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseRequestShape_DistinguishesFlatAndNamespacedTools()
    {
        var flatTool = new TestFunction("imagegen", "Generate an image.");
        var namespacedTool = new TestFunction("imagegen", "Generate an image.", "image_gen");

        var flatShape = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "make an image")],
            new ChatOptions { Tools = [flatTool] }).Shape;
        var namespacedShape = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "make an image")],
            new ChatOptions { Tools = [namespacedTool] }).Shape;

        Assert.Equal(1, flatShape.ToolCount);
        Assert.Equal(1, namespacedShape.ToolCount);
        Assert.StartsWith("sha256:", flatShape.ToolsHash, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", namespacedShape.ToolsHash, StringComparison.Ordinal);
        Assert.NotEqual(flatShape.ToolsHash, namespacedShape.ToolsHash);
    }

    [Fact]
    public async Task StreamingFunctionLoop_RecordsPromptCacheRequestShapeTraceForEachRequest()
    {
        const string sessionKey = "responses-request-shape-trace";
        var previous = TracingChatClient.CurrentSessionKey;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateOutputItemDone(CreateUnknownToolSearchCallItem(
                    "search-call",
                    new { query = "no matching tools" }))
            ],
            [
                new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 2,
                    ItemId = "msg-1",
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Delta = "done"
                },
                new StreamingResponseCompletedUpdate
                {
                    SequenceNumber = 3,
                    Response = new ResponseResult
                    {
                        Usage = new ResponseTokenUsage
                        {
                            InputTokenCount = 10,
                            OutputTokenCount = 1
                        }
                    }
                }
            ]
        ]);
        using var responsesClient = CreateClient(inner, transport);
        using var client = new TracingChatClient(
            new StreamingFunctionInvokingChatClient(responsesClient),
            collector);

        try
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;
            using var requestContext = TestModelProviderRegistry.PushRequestContext(sessionKey, diagnostics: collector);
            using var attemptScope = ModelStreamAttemptRuntimeScope.Begin(3);

            _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(
                [
                    new ChatMessage(ChatRole.System, "secret stable system"),
                    new ChatMessage(ChatRole.User, "secret user prompt")
                ],
                new ChatOptions
                {
                    Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "thread-secret-cache-key"
                    }
                }));
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previous;
        }

        var shapeEvents = store.GetEvents(sessionKey)
            .Where(e => e.Type == TraceEventType.PromptCacheRequestShape)
            .ToArray();
        Assert.Equal([1, 2], shapeEvents.Select(static e => e.RequestIndex).ToArray());
        Assert.Contains(
            store.GetEvents(sessionKey),
            static e => e.Type == TraceEventType.ProviderResponseDiagnostic);

        var evt = shapeEvents[0];
        Assert.Equal(1, evt.RequestIndex);
        Assert.Equal("gpt-test", evt.ModelId);
        Assert.NotNull(evt.MetadataJson);
        Assert.DoesNotContain("secret user prompt", evt.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret stable system", evt.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("thread-secret-cache-key", evt.MetadataJson, StringComparison.Ordinal);

        using var metadata = JsonDocument.Parse(evt.MetadataJson);
        var root = metadata.RootElement;
        Assert.Equal(1, root.GetProperty("requestIndex").GetInt32());
        Assert.Equal(3, root.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("openai-responses", root.GetProperty("protocol").GetString());
        Assert.False(root.GetProperty("maxOutputTokensPresentAfterOAuthRewrite").GetBoolean());
        Assert.False(root.GetProperty("maxOutputTokensRemovedByOAuthRewrite").GetBoolean());
        Assert.Equal("Auto", root.GetProperty("toolChoiceKind").GetString());
        Assert.True(root.GetProperty("streamingEnabled").GetBoolean());
        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.Equal(1, root.GetProperty("inputItemCount").GetInt32());
        Assert.True(root.GetProperty("inputBytes").GetInt32() > 0);
        Assert.Equal(1, root.GetProperty("inputItemIdEligibleCount").GetInt32());
        Assert.Equal(1, root.GetProperty("inputItemIdPresentCount").GetInt32());
        Assert.Equal(1, root.GetProperty("inputItemIdGeneratedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("inputItemIdMissingCount").GetInt32());
        Assert.Equal(0, root.GetProperty("inputItemIdInvalidSourceCount").GetInt32());
        Assert.StartsWith("sha256:", root.GetProperty("inputHash").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("sha256:", root.GetProperty("inputItemHashes")[0].GetString(), StringComparison.Ordinal);
        Assert.StartsWith("sha256:", root.GetProperty("promptCacheKeyHash").GetString(), StringComparison.Ordinal);

        using var secondMetadata = JsonDocument.Parse(shapeEvents[1].MetadataJson!);
        Assert.True(secondMetadata.RootElement.GetProperty("inputItemCount").GetInt32() > 1);
    }

    [Fact]
    public async Task GetResponseAsync_WithoutNativeToolSearchAddsPromptCacheKeyFromActiveThread()
    {
        var previous = TracingChatClient.CurrentSessionKey;
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));
        var transport = new FakeToolSearchTransport();
        using var client = CreateClient(inner, transport);
        try
        {
            TracingChatClient.CurrentSessionKey = "thread-cache-key";
            using var requestContext = TestModelProviderRegistry.PushRequestContext("thread-cache-key");

            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                new ChatOptions { ConversationId = "stale-provider-conversation" });

            Assert.Equal(0, inner.GetResponseCalls);
            Assert.Equal(0, inner.GetStreamingResponseCalls);
            Assert.Null(response.ConversationId);

            using var document = JsonDocument.Parse(SerializeOptions(Assert.Single(transport.Requests)));
            Assert.False(document.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("thread-cache-key", document.RootElement.GetProperty("prompt_cache_key").GetString());
            Assert.Contains(
                document.RootElement.GetProperty("include").EnumerateArray(),
                item => item.GetString() == "reasoning.encrypted_content");
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithoutNativeToolSearchPrefersConfiguredPromptCacheKey()
    {
        var previous = TracingChatClient.CurrentSessionKey;
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));
        var transport = new FakeToolSearchTransport();
        using var client = CreateClient(inner, transport);
        try
        {
            TracingChatClient.CurrentSessionKey = "async-local-cache-key";

            await CollectStreamingAsync(client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                new ChatOptions
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "configured-cache-key"
                    }
                }));

            Assert.Equal(0, inner.GetStreamingResponseCalls);
            using var document = JsonDocument.Parse(SerializeOptions(Assert.Single(transport.Requests)));
            Assert.False(document.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("configured-cache-key", document.RootElement.GetProperty("prompt_cache_key").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }

    [Fact]
    public void MaintenanceForkCacheShaper_OpenAIResponsesPatchesRawFactoryAndPreservesStoreFalse()
    {
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new CreateResponseOptions
            {
                StoredOutputEnabled = false
            }
        };
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "stable prefix")],
            new ChatOptions { ModelId = "gpt-test" },
            providerId: "responses",
            mode: "agent",
            threadId: "thread_1",
            turnId: "turn_1");

        var diagnostics = MaintenanceForkCacheShaper.Apply(
            snapshot,
            [],
            options,
            new MaintenanceForkCacheOptions(
                ModelProviderProtocols.OpenAIResponses,
                null,
                "gpt-test"));

        Assert.True(diagnostics.CacheShapeApplied);
        Assert.Equal("openai-responses-prompt-cache-key", diagnostics.CacheShapeKind);
        Assert.True(diagnostics.PromptCacheKeyPresent);
        Assert.Equal("providerImplicit", diagnostics.CacheWriteMode);
        Assert.Null(diagnostics.TailCacheWriteSkipped);
        Assert.True(diagnostics.ProviderImplicitCacheWrite);

        var raw = Assert.IsType<CreateResponseOptions>(
            options.RawRepresentationFactory!(new FakeChatClient(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "ok")
            ]))));
        using var document = JsonDocument.Parse(SerializeOptions(raw));
        Assert.False(document.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal(
            "thread_1",
            options.AdditionalProperties?[ProviderPromptCacheMetadata.PromptCacheKey]);
    }

    [Fact]
    public void CreateResponseOptions_ReplaysProtectedReasoningBeforeToolOutput()
    {
        var reasoning = new TextReasoningContent("")
        {
            ProtectedData = "encrypted-reasoning-payload"
        };
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "create a task"),
            new ChatMessage(ChatRole.Assistant, [
                reasoning,
                new TextContent("Creating it."),
                new FunctionCallContent(
                    "create-call",
                    "CreateBoardTask",
                    new Dictionary<string, object?> { ["title"] = "test task" })
            ]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent(
                    "create-call",
                    "Created Workflow App local task.")
            ])
        };

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            messages,
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var reasoningIndex = Array.FindIndex(input, item => item.GetProperty("type").GetString() == "reasoning");
        var callIndex = Array.FindIndex(input, item => item.GetProperty("type").GetString() == "function_call");
        var outputIndex = Array.FindIndex(input, item => item.GetProperty("type").GetString() == "function_call_output");

        Assert.True(reasoningIndex >= 0);
        Assert.True(callIndex > reasoningIndex);
        Assert.True(outputIndex > callIndex);
        Assert.Equal("encrypted-reasoning-payload", input[reasoningIndex].GetProperty("encrypted_content").GetString());
        Assert.Equal(JsonValueKind.Array, input[reasoningIndex].GetProperty("content").ValueKind);
        Assert.Equal(JsonValueKind.Array, input[reasoningIndex].GetProperty("summary").ValueKind);
        Assert.Equal("CreateBoardTask", input[callIndex].GetProperty("name").GetString());
        Assert.Equal("Created Workflow App local task.", input[outputIndex].GetProperty("output").GetString());
    }

    [Fact]
    public void CreateResponseOptions_PreservesFunctionCallNamespaceAndSerializesAIContentToolResultAsText()
    {
        var call = new FunctionCallContent(
            "create-call",
            "CreateBoardTask",
            new Dictionary<string, object?> { ["title"] = "Ship it" })
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesToolSearchMapper.FunctionCallNamespaceMetadataKey] = "workflow"
            }
        };
        var messages = new[]
        {
            new ChatMessage(ChatRole.Assistant, [call]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent(
                    "create-call",
                    new List<AIContent>
                    {
                        new TextContent("Created Workflow App task DEF-188."),
                        new TextContent("{\"id\":\"DEF-188\"}")
                    })
            ])
        };

        using var document = JsonDocument.Parse(CreateRequestJson(
            "gpt-test",
            messages,
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))]
            }));

        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var functionCall = input.Single(item => item.GetProperty("type").GetString() == "function_call");
        Assert.Equal("workflow", functionCall.GetProperty("namespace").GetString());

        var output = input.Single(item => item.GetProperty("type").GetString() == "function_call_output");
        AssertFunctionCallOutputHasOnlyProviderFields(output, "create-call");
        var outputText = output.GetProperty("output").GetString();
        Assert.Equal("Created Workflow App task DEF-188.\n{\"id\":\"DEF-188\"}", outputText);
        Assert.DoesNotContain(nameof(TextContent), outputText, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AIContent.AdditionalProperties), outputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResponseAsync_WithoutNativeToolSearch_UsesTransportPath()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));
        var transport = new FakeToolSearchTransport();
        using var client = CreateClient(inner, transport);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(
                        () => "ok",
                        name: "RegularTool",
                        description: "A regular function tool")
                ]
            });

        Assert.Equal(0, inner.GetResponseCalls);
        Assert.Equal(0, inner.GetStreamingResponseCalls);
        using var document = JsonDocument.Parse(SerializeOptions(Assert.Single(transport.Requests)));
        var tool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("RegularTool", tool.GetProperty("name").GetString());
        Assert.False(document.RootElement.GetProperty("store").GetBoolean());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithNativeToolSearch_UsesTransportAndMapsUnknownCall()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(CreateOutputItemDone(CreateUnknownToolSearchCallItem(
            "search-call",
            new { query = "github issue", max_results = 3 })));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "need a tool")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        Assert.Equal(0, inner.GetStreamingResponseCalls);
        using var request = JsonDocument.Parse(SerializeOptions(Assert.Single(transport.Requests)));
        Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());

        var call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("SearchTools", call.Name);
        Assert.Equal("search-call", call.CallId);
        Assert.Equal("github issue", ReadStringArgument(ReadArgument(call, "query")));
        Assert.Equal(3, ReadIntArgument(ReadArgument(call, "max_results")));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithLegacyToolSearchQuery_MapsCall()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(CreateOutputItemDone(CreateLegacyToolSearchCallItem(
            "search-call",
            "github issue")));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "need a tool")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        var call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("github issue", ReadStringArgument(ReadArgument(call, "query")));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithResponsesFunctionArgumentDeltas_InjectsToolCallArgumentPreviews()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(
            CreateStreamingUpdate("""
                {
                  "type": "response.output_item.added",
                  "output_index": 0,
                  "item": {
                    "type": "function_call",
                    "id": "fc_001",
                    "call_id": "call_123",
                    "name": "CreatePlan",
                    "arguments": "",
                    "status": "in_progress"
                  }
                }
                """),
            CreateStreamingUpdate("""
                {
                  "type": "response.function_call_arguments.delta",
                  "item_id": "fc_001",
                  "output_index": 0,
                  "delta": "{\"plan\":\"shi"
                }
                """),
            CreateStreamingUpdate("""
                {
                  "type": "response.function_call_arguments.delta",
                  "item_id": "fc_001",
                  "output_index": 0,
                  "delta": "p\"}"
                }
                """),
            CreateStreamingUpdate("""
                {
                  "type": "response.function_call_arguments.done",
                  "item_id": "fc_001",
                  "output_index": 0,
                  "arguments": "{\"plan\":\"ship\"}"
                }
                """),
            CreateStreamingUpdate("""
                {
                  "type": "response.output_item.done",
                  "output_index": 0,
                  "item": {
                    "type": "function_call",
                    "id": "fc_001",
                    "call_id": "call_123",
                    "name": "CreatePlan",
                    "arguments": "{\"plan\":\"ship\"}",
                    "status": "completed"
                  }
                }
                """));
        using var responsesClient = CreateClient(inner, transport);
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient)
        {
            EnableToolCallArgumentPreviews = true,
            IsStreamableTool = name => string.Equals(name, "CreatePlan", StringComparison.Ordinal),
            TerminateOnUnknownCalls = true
        };

        var updates = new List<ChatResponseUpdate>();
        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in invokingClient.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "make a plan")]))
        {
            updates.Add(update);
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());
        }

        Assert.Equal(2, deltas.Count);
        Assert.Equal(0, deltas[0].ToolCallIndex);
        Assert.Equal("CreatePlan", deltas[0].ToolName);
        Assert.Equal("call_123", deltas[0].CallId);
        Assert.Equal("{\"plan\":\"shi", deltas[0].ArgumentsDelta);
        Assert.Equal(0, deltas[1].ToolCallIndex);
        Assert.Null(deltas[1].ToolName);
        Assert.Null(deltas[1].CallId);
        Assert.Equal("p\"}", deltas[1].ArgumentsDelta);

        var finalCall = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("CreatePlan", finalCall.Name);
        Assert.Equal("call_123", finalCall.CallId);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithDuplicateInputRawResponseItem_DoesNotThrow()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(CreateOutputItemDone(CreateResponseItemWithDuplicateInputKeys()));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "inspect")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        Assert.NotEmpty(updates);
        Assert.Empty(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task StreamingFunctionLoop_WithNativeToolSearch_ReplaysNamespaceToolWithoutTopLevelInjection()
    {
        var dynamicTool = new DeferredDynamicFunction(
            "CreateBoardTask",
            "Create an Workflow App board task.",
            new List<AIContent>
            {
                new TextContent("Created Workflow App task DEF-188."),
                new TextContent("{\"id\":\"DEF-188\"}")
            });
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(dynamicTool, "dynamic", "workflow")],
            DeferredToolLoadingMode.Native);
        var searchTool = new NativeToolSearchTool(registry);
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateOutputItemDone(CreateUnknownToolSearchCallItem(
                    "search-call",
                    new { query = "board task", max_results = 5 }))
            ],
            [
                CreateOutputItemDone(CreateFunctionCallItem(
                    "create-call",
                    "workflow",
                    "CreateBoardTask",
                    new { title = "Ship it" }))
            ],
            [
                new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 3,
                    ItemId = "msg-1",
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Delta = "done"
                }
            ]
        ]);
        using var responsesClient = CreateClient(inner, transport);
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient)
        {
            AdditionalTools = registry.ActivatedToolsList
        };

        _ = await CollectStreamingAsync(invokingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Need to create a board item.")],
            new ChatOptions { Tools = [searchTool] }));

        Assert.Equal(3, transport.Requests.Count);
        using var firstRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[0]));
        using var secondRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        using var thirdRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[2]));

        AssertOnlyNativeSearchTool(firstRequest.RootElement);
        AssertOnlyNativeSearchTool(secondRequest.RootElement);
        AssertOnlyNativeSearchTool(thirdRequest.RootElement);

        var searchOutput = secondRequest.RootElement.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "tool_search_output");
        var namespaceTool = Assert.Single(searchOutput.GetProperty("tools").EnumerateArray());
        Assert.Equal("namespace", namespaceTool.GetProperty("type").GetString());
        Assert.Equal("workflow", namespaceTool.GetProperty("name").GetString());
        var child = Assert.Single(namespaceTool.GetProperty("tools").EnumerateArray());
        Assert.Equal("CreateBoardTask", child.GetProperty("name").GetString());
        Assert.False(child.GetProperty("strict").GetBoolean());
        Assert.True(child.GetProperty("defer_loading").GetBoolean());

        var thirdInput = thirdRequest.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var functionCall = thirdInput.Single(item => item.GetProperty("type").GetString() == "function_call");
        Assert.Equal("CreateBoardTask", functionCall.GetProperty("name").GetString());
        Assert.Equal("workflow", functionCall.GetProperty("namespace").GetString());

        var functionOutput = thirdInput.Single(item => item.GetProperty("type").GetString() == "function_call_output");
        AssertFunctionCallOutputHasOnlyProviderFields(functionOutput, "create-call");
        var outputText = functionOutput.GetProperty("output").GetString();
        Assert.Equal("Created Workflow App task DEF-188.\n{\"id\":\"DEF-188\"}", outputText);
        Assert.DoesNotContain(nameof(TextContent), outputText, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(AIContent.AdditionalProperties), outputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingFunctionLoop_RebuiltNativeRegistryExecutesToolFromHistoricalSearchOutput()
    {
        var previousTool = new DeferredDynamicFunction(
            "CreateBoardTask",
            "Create a Workflow App board task.");
        var previousRegistry = new DeferredToolRegistry(
            [new DeferredToolEntry(previousTool, "dynamic", "workflow")],
            DeferredToolLoadingMode.Native);
        var previousSearch = new NativeToolSearchTool(previousRegistry);
        var searchResult = await previousSearch.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "board task"
        });

        var currentTool = new DeferredDynamicFunction(
            "CreateBoardTask",
            "Create a Workflow App board task.",
            "Created Workflow App task DEF-188.");
        var currentRegistry = new DeferredToolRegistry(
            [new DeferredToolEntry(currentTool, "dynamic", "workflow")],
            DeferredToolLoadingMode.Native);
        var currentSearch = new NativeToolSearchTool(currentRegistry);
        var history = new ChatMessage[]
        {
            new(ChatRole.User, "Find the board task tool."),
            new(ChatRole.Assistant, [
                new FunctionCallContent(
                    "search-call",
                    NativeToolSearchTool.ToolName,
                    new Dictionary<string, object?> { ["query"] = "board task" })
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("search-call", searchResult)])
        };
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateOutputItemDone(CreateFunctionCallItem(
                    "create-call",
                    "workflow",
                    "CreateBoardTask",
                    new { title = "Ship it" }))
            ],
            [
                new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 2,
                    ItemId = "msg-1",
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Delta = "done"
                }
            ]
        ]);
        using var responsesClient = CreateClient(inner, transport);
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient)
        {
            AdditionalTools = currentRegistry.DeferredTools.Values.ToArray()
        };

        _ = await CollectStreamingAsync(invokingClient.GetStreamingResponseAsync(
            history,
            new ChatOptions { Tools = [currentSearch] }));

        Assert.Empty(currentRegistry.ActivatedToolsList);
        Assert.Equal(2, transport.Requests.Count);
        using var firstRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[0]));
        using var secondRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        AssertOnlyNativeSearchTool(firstRequest.RootElement);
        AssertOnlyNativeSearchTool(secondRequest.RootElement);
        Assert.Contains(
            firstRequest.RootElement.GetProperty("input").EnumerateArray(),
            static item => item.GetProperty("type").GetString() == "tool_search_output");
        var functionOutput = secondRequest.RootElement.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "function_call_output"
                            && item.GetProperty("call_id").GetString() == "create-call");
        Assert.Equal("Created Workflow App task DEF-188.", functionOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithReasoning_DoesNotRetryWithoutReasoning()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(CreateOutputItemDone(CreateUnknownToolSearchCallItem(
            "search-call",
            new { query = "github issue" })));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "need a tool")],
            new ChatOptions
            {
                Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))],
                Reasoning = new ReasoningOptions
                {
                    Effort = ReasoningEffort.High,
                    Output = ReasoningOutput.Summary
                }
            }));

        Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        var requestJson = SerializeOptions(Assert.Single(transport.Requests));
        Assert.Contains("\"reasoning\"", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"include\"", requestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithEncryptedReasoning_PreservesProviderItemIdAcrossModelHistory()
    {
        const string providerItemId = "rs_provider_reasoning";
        const string encryptedContent = "encrypted-provider-reasoning";
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.added",
                  "sequence_number": 1,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "id": "{{providerItemId}}",
                    "status": "in_progress",
                    "summary": []
                  }
                }
                """),
            new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 2,
                ItemId = providerItemId,
                OutputIndex = 0,
                SummaryIndex = 0,
                Delta = "checking "
            },
            new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 3,
                ItemId = providerItemId,
                OutputIndex = 0,
                SummaryIndex = 0,
                Delta = "inputs"
            },
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.done",
                  "sequence_number": 4,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "status": "completed",
                    "encrypted_content": "{{encryptedContent}}",
                    "summary": []
                  }
                }
                """));
        using var client = CreateClient(inner, transport);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "reason")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] });

        var message = Assert.Single(response.Messages);
        var reasoning = Assert.Single(message.Contents.OfType<TextReasoningContent>());
        Assert.Equal("checking inputs", reasoning.Text);
        Assert.Equal(encryptedContent, reasoning.ProtectedData);
        Assert.Equal(
            providerItemId,
            reasoning.AdditionalProperties![OpenAIResponsesItemIdentity.MetadataKey]);

        var codec = new ModelHistoryCodec();
        var restored = codec.Decode(codec.Encode(message, "turn_001"));
        using var request = JsonDocument.Parse(CreateRequestJson("gpt-test", [restored], new ChatOptions()));
        var replayedReasoning = Assert.Single(
            request.RootElement.GetProperty("input").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "reasoning");
        Assert.Equal(providerItemId, replayedReasoning.GetProperty("id").GetString());
        Assert.Equal(encryptedContent, replayedReasoning.GetProperty("encrypted_content").GetString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenReasoningDoneOmitsId_ReusesAddedProviderItemId()
    {
        const string providerItemId = "rs_added_reasoning";
        const string encryptedContent = "encrypted-added-reasoning";
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.added",
                  "sequence_number": 1,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "id": "{{providerItemId}}",
                    "status": "in_progress",
                    "summary": []
                  }
                }
                """),
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.done",
                  "sequence_number": 2,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "status": "completed",
                    "encrypted_content": "{{encryptedContent}}",
                    "summary": []
                  }
                }
                """));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "reason")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        var reasoning = Assert.Single(
            updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>());
        Assert.Equal(encryptedContent, reasoning.ProtectedData);
        Assert.Equal(
            providerItemId,
            reasoning.AdditionalProperties![OpenAIResponsesItemIdentity.MetadataKey]);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningEventsKeepProviderIdsIsolatedByOutputIndex()
    {
        const string firstItemId = "rs_first_reasoning";
        const string secondItemId = "rs_second_reasoning";
        const string firstEncryptedContent = "encrypted-first-reasoning";
        const string secondEncryptedContent = "encrypted-second-reasoning";
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(
            new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 1,
                ItemId = firstItemId,
                OutputIndex = 0,
                SummaryIndex = 0,
                Delta = "first"
            },
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.done",
                  "sequence_number": 2,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "status": "completed",
                    "encrypted_content": "{{firstEncryptedContent}}",
                    "summary": []
                  }
                }
                """),
            new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 3,
                ItemId = secondItemId,
                OutputIndex = 1,
                SummaryIndex = 0,
                Delta = "second"
            },
            CreateStreamingUpdate($$"""
                {
                  "type": "response.output_item.done",
                  "sequence_number": 4,
                  "output_index": 1,
                  "item": {
                    "type": "reasoning",
                    "status": "completed",
                    "encrypted_content": "{{secondEncryptedContent}}",
                    "summary": []
                  }
                }
                """));
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "reason twice")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        var reasoningContents = updates
            .SelectMany(update => update.Contents)
            .OfType<TextReasoningContent>()
            .ToArray();
        Assert.Equal(4, reasoningContents.Length);
        AssertReasoningItemId(
            Assert.Single(reasoningContents, reasoning => reasoning.Text == "first"),
            firstItemId);
        AssertReasoningItemId(
            Assert.Single(reasoningContents, reasoning => reasoning.ProtectedData == firstEncryptedContent),
            firstItemId);
        AssertReasoningItemId(
            Assert.Single(reasoningContents, reasoning => reasoning.Text == "second"),
            secondItemId);
        AssertReasoningItemId(
            Assert.Single(reasoningContents, reasoning => reasoning.ProtectedData == secondEncryptedContent),
            secondItemId);

        static void AssertReasoningItemId(TextReasoningContent reasoning, string expectedItemId) =>
            Assert.Equal(
                expectedItemId,
                reasoning.AdditionalProperties![OpenAIResponsesItemIdentity.MetadataKey]);
    }

    [Fact]
    public async Task StreamingFunctionLoop_WithEncryptedReasoning_ReplaysProviderItemIdAfterParallelTools()
    {
        const string providerItemId = "rs_parallel_reasoning";
        const string encryptedContent = "encrypted-parallel-reasoning";
        var tools = new[]
        {
            AIFunctionFactory.Create(() => "alpha", name: "ToolAlpha"),
            AIFunctionFactory.Create(() => "beta", name: "ToolBeta"),
            AIFunctionFactory.Create(() => "gamma", name: "ToolGamma")
        };
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateStreamingUpdate($$"""
                    {
                      "type": "response.output_item.added",
                      "sequence_number": 1,
                      "output_index": 0,
                      "item": {
                        "type": "reasoning",
                        "id": "{{providerItemId}}",
                        "status": "in_progress",
                        "summary": []
                      }
                    }
                    """),
                new StreamingResponseReasoningSummaryTextDeltaUpdate
                {
                    SequenceNumber = 2,
                    ItemId = providerItemId,
                    OutputIndex = 0,
                    SummaryIndex = 0,
                    Delta = "checking "
                },
                new StreamingResponseReasoningSummaryTextDeltaUpdate
                {
                    SequenceNumber = 3,
                    ItemId = providerItemId,
                    OutputIndex = 0,
                    SummaryIndex = 0,
                    Delta = "tools"
                },
                CreateStreamingUpdate($$"""
                    {
                      "type": "response.output_item.done",
                      "sequence_number": 4,
                      "output_index": 0,
                      "item": {
                        "type": "reasoning",
                        "status": "completed",
                        "encrypted_content": "{{encryptedContent}}",
                        "summary": []
                      }
                    }
                    """),
                CreateFunctionCallDoneUpdate(5, 1, "fc_alpha", "call_alpha", "ToolAlpha"),
                CreateFunctionCallDoneUpdate(6, 2, "fc_beta", "call_beta", "ToolBeta"),
                CreateFunctionCallDoneUpdate(7, 3, "fc_gamma", "call_gamma", "ToolGamma")
            ],
            [
                new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 8,
                    ItemId = "msg_done",
                    OutputIndex = 0,
                    ContentIndex = 0,
                    Delta = "done"
                }
            ]
        ]);
        using var responsesClient = CreateClient(inner, transport);
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient)
        {
            AdditionalTools = tools
        };

        _ = await CollectStreamingAsync(invokingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "run tools")],
            new ChatOptions { Tools = tools.Cast<AITool>().ToList() }));

        Assert.Equal(2, transport.Requests.Count);
        using var secondRequest = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        var input = secondRequest.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var reasoning = Assert.Single(input, item => item.GetProperty("type").GetString() == "reasoning");
        Assert.Equal(providerItemId, reasoning.GetProperty("id").GetString());
        Assert.Equal(encryptedContent, reasoning.GetProperty("encrypted_content").GetString());
        Assert.Equal(3, input.Count(item => item.GetProperty("type").GetString() == "function_call"));
        Assert.Equal(3, input.Count(item => item.GetProperty("type").GetString() == "function_call_output"));
    }

    [Fact]
    public async Task StreamingFunctionLoop_PostToolReasoningStartsAssistantMessage()
    {
        const string firstReasoningId = "rs_before_tools";
        const string secondReasoningId = "rs_after_tools";
        var tools = new[]
        {
            AIFunctionFactory.Create(() => "alpha", name: "ToolAlpha"),
            AIFunctionFactory.Create(() => "beta", name: "ToolBeta"),
            AIFunctionFactory.Create(() => "gamma", name: "ToolGamma")
        };
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateStreamingUpdate($$"""
                    {
                      "type": "response.output_item.added",
                      "sequence_number": 1,
                      "output_index": 0,
                      "item": {
                        "type": "reasoning",
                        "id": "{{firstReasoningId}}",
                        "status": "in_progress",
                        "summary": []
                      }
                    }
                    """),
                new StreamingResponseReasoningSummaryTextDeltaUpdate
                {
                    SequenceNumber = 2,
                    ItemId = firstReasoningId,
                    OutputIndex = 0,
                    SummaryIndex = 0,
                    Delta = "before tools"
                },
                CreateFunctionCallDoneUpdate(3, 1, "fc_alpha", "call_alpha", "ToolAlpha"),
                CreateFunctionCallDoneUpdate(4, 2, "fc_beta", "call_beta", "ToolBeta"),
                CreateFunctionCallDoneUpdate(5, 3, "fc_gamma", "call_gamma", "ToolGamma")
            ],
            [
                CreateStreamingUpdate($$"""
                    {
                      "type": "response.output_item.added",
                      "sequence_number": 6,
                      "output_index": 0,
                      "item": {
                        "type": "reasoning",
                        "id": "{{secondReasoningId}}",
                        "status": "in_progress",
                        "summary": []
                      }
                    }
                    """),
                new StreamingResponseReasoningSummaryTextDeltaUpdate
                {
                    SequenceNumber = 7,
                    ItemId = secondReasoningId,
                    OutputIndex = 0,
                    SummaryIndex = 0,
                    Delta = "after tools"
                },
                CreateStreamingUpdate("""
                    {
                      "type": "response.output_item.done",
                      "sequence_number": 8,
                      "output_index": 0,
                      "item": {
                        "type": "reasoning",
                        "id": "rs_after_tools",
                        "status": "completed",
                        "encrypted_content": "encrypted-after-tools",
                        "summary": []
                      }
                    }
                    """),
                CreateStreamingUpdate("""
                    {
                      "type": "response.output_item.added",
                      "sequence_number": 9,
                      "output_index": 1,
                      "item": {
                        "type": "message",
                        "id": "msg_done",
                        "status": "in_progress",
                        "content": [],
                        "role": "assistant"
                      }
                    }
                    """),
                new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 10,
                    ItemId = "msg_done",
                    OutputIndex = 1,
                    ContentIndex = 0,
                    Delta = "done"
                }
            ]
        ]);
        using var responsesClient = CreateClient(inner, transport);
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient)
        {
            AdditionalTools = tools
        };

        var updates = await CollectStreamingAsync(invokingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "run tools")],
            new ChatOptions { Tools = tools.Cast<AITool>().ToList() }));
        var response = updates.ToChatResponse();

        var toolMessage = Assert.Single(response.Messages, message => message.Role == ChatRole.Tool);
        Assert.Equal(3, toolMessage.Contents.OfType<FunctionResultContent>().Count());
        Assert.DoesNotContain(toolMessage.Contents, content => content is TextReasoningContent);

        var postToolReasoning = Assert.Single(
            response.Messages
                .Where(message => message.Role == ChatRole.Assistant)
                .SelectMany(message => message.Contents)
                .OfType<TextReasoningContent>(),
            reasoning => reasoning.Text == "after tools");
        Assert.Equal(
            secondReasoningId,
            postToolReasoning.AdditionalProperties![OpenAIResponsesItemIdentity.MetadataKey]);
        Assert.Equal("encrypted-after-tools", postToolReasoning.ProtectedData);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithTextReasoningAndUsage_UsesMeaiStreamingMapping()
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(
            new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 1,
                ItemId = "msg-1",
                OutputIndex = 0,
                ContentIndex = 0,
                Delta = "hel"
            },
            new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 2,
                ItemId = "msg-1",
                OutputIndex = 0,
                ContentIndex = 0,
                Delta = "lo"
            },
            new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 3,
                ItemId = "rs-1",
                OutputIndex = 0,
                SummaryIndex = 0,
                Delta = "thinking"
            },
            new StreamingResponseCompletedUpdate
            {
                SequenceNumber = 4,
                Response = new ResponseResult
                {
                    Usage = new ResponseTokenUsage
                    {
                        InputTokenCount = 10,
                        OutputTokenCount = 5,
                        TotalTokenCount = 15,
                        InputTokenDetails = new ResponseInputTokenUsageDetails
                        {
                            CachedTokenCount = 4
                        },
                        OutputTokenDetails = new ResponseOutputTokenUsageDetails
                        {
                            ReasoningTokenCount = 2
                        }
                    }
                }
            });
        using var client = CreateClient(inner, transport);

        var updates = await CollectStreamingAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));

        Assert.Equal("hello", string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text)));
        Assert.Equal(
            "thinking",
            string.Concat(updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Select(reasoning => reasoning.Text)));
        var usage = Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
        Assert.Equal(10, usage.Details.InputTokenCount);
        Assert.Equal(5, usage.Details.OutputTokenCount);
        Assert.Equal(4, usage.Details.CachedInputTokenCount);
        Assert.Equal(2, usage.Details.ReasoningTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RecordsProviderDiagnosticForIncompleteResponse()
    {
        const string sessionKey = "responses-provider-incomplete-trace";
        var previous = TracingChatClient.CurrentSessionKey;
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport(CreateStreamingUpdate("""
            {
              "type": "response.incomplete",
              "sequence_number": 1,
              "response": {
                "id": "resp-incomplete",
                "object": "response",
                "created_at": 0,
                "model": "gpt-test",
                "status": "incomplete",
                "incomplete_details": {
                  "reason": "max_output_tokens"
                },
                "usage": {
                  "input_tokens": 10,
                  "output_tokens": 4,
                  "total_tokens": 14
                }
              }
            }
            """));
        using var client = CreateClient(inner, transport, collector);

        try
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;
            using var requestContext = TestModelProviderRegistry.PushRequestContext(sessionKey, diagnostics: collector);

            _ = await CollectStreamingAsync(client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                new ChatOptions { Tools = [new NativeToolSearchTool(new DeferredToolRegistry([]))] }));
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previous;
        }

        var diagnostic = Assert.Single(
            store.GetEvents(sessionKey),
            e => e.Type == TraceEventType.ProviderResponseDiagnostic);

        Assert.Equal("resp-incomplete", diagnostic.ResponseId);
        Assert.Equal("gpt-test", diagnostic.ModelId);
        Assert.Equal("max_output_tokens", diagnostic.FinishReason);
        Assert.Contains("response.incomplete", diagnostic.MetadataJson);
        Assert.Contains("max_output_tokens", diagnostic.MetadataJson);
        Assert.Contains("\"usagePresent\":true", diagnostic.MetadataJson);
    }

    private static async Task<List<ChatResponseUpdate>> CollectStreamingAsync(
        IAsyncEnumerable<ChatResponseUpdate> streaming)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in streaming)
            updates.Add(update);
        return updates;
    }

    private static string CreateRequestJson(
        string model,
        IEnumerable<ChatMessage> messages,
        ChatOptions? options) =>
        SerializeOptions(ResponsesToolSearchMapper.CreateResponseOptions(model, messages, options));

    private static string SerializeOptions(CreateResponseOptions options) =>
        ModelReaderWriter.Write(options).ToString();

    private static void AssertFunctionCallOutputHasOnlyProviderFields(JsonElement output, string callId)
    {
        Assert.Equal("function_call_output", output.GetProperty("type").GetString());
        Assert.Equal(callId, output.GetProperty("call_id").GetString());
        Assert.StartsWith("fco_", output.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.False(output.TryGetProperty("namespace", out _));
        Assert.Equal(
            ["call_id", "id", "output", "type"],
            output.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    private static StreamingResponseOutputItemDoneUpdate CreateOutputItemDone(ResponseItem item) =>
        new()
        {
            SequenceNumber = 1,
            OutputIndex = 0,
            Item = item
        };

    private static StreamingResponseUpdate CreateFunctionCallDoneUpdate(
        int sequenceNumber,
        int outputIndex,
        string itemId,
        string callId,
        string name) =>
        CreateStreamingUpdate($$"""
            {
              "type": "response.output_item.done",
              "sequence_number": {{sequenceNumber}},
              "output_index": {{outputIndex}},
              "item": {
                "type": "function_call",
                "id": "{{itemId}}",
                "call_id": "{{callId}}",
                "name": "{{name}}",
                "arguments": "{}",
                "status": "completed"
              }
            }
            """);

    private static StreamingResponseUpdate CreateStreamingUpdate(string json) =>
        ModelReaderWriter.Read<StreamingResponseUpdate>(
            BinaryData.FromString(json),
            ModelReaderWriterOptions.Json)!;

    private static ResponseItem CreateHostedImageGenerationCallItem(
        string itemId,
        string status,
        string revisedPrompt,
        string? resultBase64)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "image_generation_call",
            ["id"] = itemId,
            ["status"] = status,
            ["revised_prompt"] = revisedPrompt
        };
        if (resultBase64 != null)
            payload["result"] = resultBase64;

        return ModelReaderWriter.Read<ResponseItem>(
            BinaryData.FromString(JsonSerializer.Serialize(payload, JsonOptions)),
            ModelReaderWriterOptions.Json)!;
    }

    private static ResponseItem CreateUnknownToolSearchCallItem(string callId, object arguments)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "tool_search_call",
            call_id = callId,
            execution = "client",
            status = "completed",
            arguments
        }, JsonOptions);
        return ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private static ResponseItem CreateLegacyToolSearchCallItem(string callId, string query)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "tool_search_call",
            call_id = callId,
            query
        }, JsonOptions);
        return ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private static ResponseItem CreateResponseItemWithDuplicateInputKeys()
    {
        const string json = """
            {
              "type": "apply_patch_call",
              "id": "item-duplicate-input",
              "call_id": "call-duplicate-input",
              "input": { "old": true },
              "input": { "new": true }
            }
            """;
        return ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private static ResponseItem CreateFunctionCallItem(
        string callId,
        string functionNamespace,
        string name,
        object arguments)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "function_call",
            call_id = callId,
            @namespace = functionNamespace,
            name,
            arguments = JsonSerializer.Serialize(arguments, JsonOptions)
        }, JsonOptions);
        return ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
    }

    private static void AssertOnlyNativeSearchTool(JsonElement root)
    {
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("tool_search", tool.GetProperty("type").GetString());
        Assert.DoesNotContain(
            root.GetProperty("tools").EnumerateArray(),
            item => item.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() == "CreateBoardTask");
    }

    private static object? ReadArgument(FunctionCallContent call, string name) =>
        call.Arguments != null && call.Arguments.TryGetValue(name, out var value)
            ? value
            : null;

    private static string? ReadStringArgument(object? value) =>
        value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString()
        };

    private static int? ReadIntArgument(object? value) =>
        value switch
        {
            int number => number,
            long number => checked((int)number),
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
            string text when int.TryParse(text, out var number) => number,
            _ => null
        };

    private static OpenAIResponsesToolSearchChatClient CreateClient(
        IChatClient innerClient,
        IResponsesToolSearchTransport transport,
        TraceCollector? traceCollector = null) =>
        new(
            new ResponsesClient("sk-test"),
            "gpt-test",
            innerClient,
            transport,
            traceCollector);

    private static AITool CreateRuntimeDynamicTool(string? toolNamespace, string name)
    {
        var proxy = new WireDynamicToolProxy();
        var thread = new SessionThread
        {
            Id = $"thread_{Guid.NewGuid():N}",
            WorkspacePath = Environment.CurrentDirectory,
            OriginChannel = "appserver",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = new ThreadConfiguration()
        };

        RuntimeDynamicToolDeclarationSpec declaration = toolNamespace is null
            ? new RuntimeDynamicToolFunctionSpec
            {
                Name = name,
                Description = "Generate an image.",
                InputSchema = new JsonObject { ["type"] = "object" }
            }
            : new RuntimeDynamicToolNamespaceSpec
            {
                Name = toolNamespace,
                Description = $"{toolNamespace} tools.",
                Tools =
                [
                    new RuntimeDynamicToolFunctionSpec
                    {
                        Name = name,
                        Description = "Generate an image.",
                        InputSchema = new JsonObject { ["type"] = "object" }
                    }
                ]
            };

        proxy.BindThread(
            thread.Id,
            new NoopAppServerTransport(),
            new AppServerConnection(),
            [declaration]);

        var planning = new ToolPlanningContext(
            thread.Id,
            null,
            thread.WorkspacePath,
            Path.Combine(thread.WorkspacePath, ".craft"),
            "default",
            null,
            [],
            1);
        var snapshot = new EffectiveToolSnapshotBuilder()
            .BuildAsync([proxy], planning)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return Assert.Single(AgentFactory.ProjectSnapshotTools(snapshot));
    }

    private static byte[] CreateImageBytes(string mediaType)
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(0xff, 0, 0));
        using var stream = new MemoryStream();
        switch (mediaType)
        {
            case "image/bmp":
                image.SaveAsBmp(stream);
                break;
            default:
                image.SaveAsPng(stream);
                break;
        }

        return stream.ToArray();
    }

    private sealed class FakeToolSearchTransport : IResponsesToolSearchTransport
    {
        private readonly Queue<IReadOnlyList<StreamingResponseUpdate>> _responses;

        public FakeToolSearchTransport(params StreamingResponseUpdate[] updates)
            : this(new[] { updates })
        {
        }

        public FakeToolSearchTransport(IEnumerable<IReadOnlyList<StreamingResponseUpdate>> responses)
        {
            _responses = new Queue<IReadOnlyList<StreamingResponseUpdate>>(responses);
        }

        public List<CreateResponseOptions> Requests { get; } = [];

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(options);
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : [];
            foreach (var update in response)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FakeChatClient(
        ChatResponse response,
        ChatResponseUpdate? streamingUpdate = null) : IChatClient
    {
        public int GetResponseCalls { get; private set; }
        public int GetStreamingResponseCalls { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            GetResponseCalls++;
            LastOptions = options;
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            GetStreamingResponseCalls++;
            LastOptions = options;
            yield return streamingUpdate ?? new ChatResponseUpdate(ChatRole.Assistant, "inner stream");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class UnknownContent : AIContent;

    private sealed class NoopAppServerTransport : IAppServerTransport
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();
    }

    private sealed class TestFunction(
        string name,
        string description,
        string? toolNamespace = null,
        string? namespaceDescription = null) : AIFunction, IToolNamespaceMetadata
    {
        private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object"
        });

        public string? ToolNamespace => toolNamespace;

        public string? ToolNamespaceDescription => namespaceDescription;

        public override string Name => name;

        public override string Description => description;

        public override JsonElement JsonSchema => Schema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            _ = arguments;
            _ = cancellationToken;
            return ValueTask.FromResult<object?>("ok");
        }
    }

    private sealed class DeferredDynamicFunction(
        string name,
        string description,
        object? result = null) : AIFunction
    {
        private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string" }
            }
        });

        public override string Name => name;

        public override string Description => description;

        public override JsonElement JsonSchema => Schema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            _ = arguments;
            _ = cancellationToken;
            return ValueTask.FromResult<object?>(result ?? "ok");
        }
    }
}
