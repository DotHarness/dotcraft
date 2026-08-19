using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicEagerToolInputStreamingChatClientTests
{
    [Fact]
    public async Task ProviderAdapter_SerializesEagerInputStreamingForFunctionTools()
    {
        var handler = new CaptureHandler();
        using var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            new AnthropicEagerToolInputStreamingChatClient(CreateBetaClient(handler)),
            new AppConfig(),
            CreateRuntime());
        var function = new SchemaFunction("CreatePlan", "Create a plan.", RichSchema);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "plan")],
            new ChatOptions { Tools = [function] });

        using var document = JsonDocument.Parse(handler.LastRequestJson!);
        var tool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.True(tool.GetProperty("eager_input_streaming").GetBoolean());
        Assert.Equal("CreatePlan", tool.GetProperty("name").GetString());
        Assert.Equal("Create a plan.", tool.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ProviderAdapter_SerializesModeDiscriminatedWorkflowSchemaWithoutTopLevelUnions()
    {
        var handler = new CaptureHandler();
        using var client = new AnthropicEagerToolInputStreamingChatClient(CreateBetaClient(handler));
        var function = new SchemaFunction("Workflow", "Start or resume a workflow.", WorkflowSchema);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "run")],
            new ChatOptions { Tools = [function] });

        var tool = GetSerializedTool(handler.LastRequestJson!);
        Assert.True(tool["eager_input_streaming"]!.GetValue<bool>());
        var inputSchema = tool["input_schema"]!.AsObject();
        Assert.Equal("object", inputSchema["type"]!.GetValue<string>());
        Assert.False(inputSchema.ContainsKey("oneOf"));
        Assert.False(inputSchema.ContainsKey("allOf"));
        Assert.False(inputSchema.ContainsKey("anyOf"));
        Assert.Equal(["mode"], inputSchema["required"]!.AsArray().Select(static item => item!.GetValue<string>()).ToArray());
        Assert.Equal(["script", "path", "name", "resume"],
            inputSchema["properties"]!["mode"]!["enum"]!.AsArray()
                .Select(static item => item!.GetValue<string>())
                .ToArray());
    }

    [Fact]
    public void PrepareOptions_ConvertsFunctionsAndPreservesOriginalOptionsAndOrder()
    {
        var function = new SchemaFunction("CreatePlan", "Create a plan.", RichSchema);
        var hostedTool = new HostedWebSearchTool();
        var options = new ChatOptions { Tools = [hostedTool, function] };

        var prepared = AnthropicEagerToolInputStreamingChatClient.PrepareOptions(options);

        Assert.NotSame(options, prepared);
        Assert.Same(hostedTool, prepared!.Tools![0]);
        Assert.Same(function, options.Tools![1]);
        var betaTool = GetBetaTool(prepared.Tools[1]);
        Assert.True(betaTool.EagerInputStreaming);
        Assert.Equal(function.Name, betaTool.Name);
        Assert.Equal(function.Description, betaTool.Description);
    }

    [Fact]
    public void PrepareOptions_EnablesUnsetNativeBetaToolAndPreservesExplicitFalse()
    {
        var unset = CreateNativeTool("unset", eagerInputStreaming: null);
        var disabled = CreateNativeTool("disabled", eagerInputStreaming: false);
        var options = new ChatOptions { Tools = [unset, disabled] };

        var prepared = AnthropicEagerToolInputStreamingChatClient.PrepareOptions(options);

        Assert.NotSame(options, prepared);
        Assert.True(GetBetaTool(prepared!.Tools![0]).EagerInputStreaming);
        Assert.Same(disabled, prepared.Tools[1]);
        Assert.False(GetBetaTool(prepared.Tools[1]).EagerInputStreaming);
        Assert.Null(GetBetaTool(unset).EagerInputStreaming);
    }

    [Fact]
    public async Task StreamingAndNonStreamingPathsPrepareEquivalentToolDefinitions()
    {
        var inner = new CapturingChatClient();
        using var client = new AnthropicEagerToolInputStreamingChatClient(inner);
        var options = new ChatOptions { Tools = [new SchemaFunction("LargeInput", "Large input.", RichSchema)] };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")], options);
        var responseTool = GetBetaTool(Assert.Single(inner.LastOptions!.Tools!));

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "two")],
            options))
        {
        }
        var streamingTool = GetBetaTool(Assert.Single(inner.LastOptions!.Tools!));

        Assert.True(responseTool.EagerInputStreaming);
        Assert.True(streamingTool.EagerInputStreaming);
        Assert.True(JsonElement.DeepEquals(
            new BetaToolUnion(responseTool).Json,
            new BetaToolUnion(streamingTool).Json));
    }

    [Fact]
    public async Task ToolSchemaMatchesSdkMappingExceptForEagerInputStreaming()
    {
        var function = new SchemaFunction("SchemaParity", "Schema parity.", RichSchema);
        var baselineHandler = new CaptureHandler();
        var adaptedHandler = new CaptureHandler();
        using var baseline = CreateBetaClient(baselineHandler);
        using var adapted = new AnthropicEagerToolInputStreamingChatClient(CreateBetaClient(adaptedHandler));
        var options = new ChatOptions { Tools = [function] };
        var messages = new[] { new ChatMessage(ChatRole.User, "compare") };

        await baseline.GetResponseAsync(messages, options);
        await adapted.GetResponseAsync(messages, options);

        var baselineTool = GetSerializedTool(baselineHandler.LastRequestJson!);
        var adaptedTool = GetSerializedTool(adaptedHandler.LastRequestJson!);
        Assert.True(adaptedTool.Remove("eager_input_streaming"));
        Assert.True(JsonNode.DeepEquals(baselineTool, adaptedTool));

        var inputSchema = baselineTool["input_schema"]!.AsObject();
        var planSchema = inputSchema["properties"]!["plan"]!.AsObject();
        Assert.False(planSchema.ContainsKey("format"));
        Assert.Contains("format", planSchema["description"]!.GetValue<string>());
        var todoSchema = inputSchema["properties"]!["todos"]!.AsObject();
        Assert.False(todoSchema.ContainsKey("minItems"));
        Assert.Contains("minItems", todoSchema["description"]!.GetValue<string>());
        Assert.True(inputSchema["properties"]!["choice"]!.AsObject().ContainsKey("anyOf"));
    }

    private static readonly JsonElement RichSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            plan = new
            {
                type = "string",
                description = "A large plan.",
                format = "markdown",
                minLength = 1
            },
            todos = new
            {
                type = "array",
                minItems = 2,
                items = new { type = "string" }
            },
            choice = new
            {
                oneOf = new object[]
                {
                    new { type = "string" },
                    new { type = "number" }
                }
            }
        },
        required = new[] { "plan" }
    });

    private static readonly JsonElement WorkflowSchema = JsonDocument.Parse(
        """
        {
          "type":"object",
          "properties":{
            "mode":{"type":"string","enum":["script","path","name","resume"],"description":"Required workflow source mode."},
            "script":{"type":"string","description":"Required when mode is script."},
            "scriptPath":{"type":"string","description":"Required when mode is path."},
            "name":{"type":"string","description":"Required when mode is name."},
            "args":{"description":"Optional JSON arguments exposed to the workflow."},
            "resumeFromRunId":{"type":"string","description":"Required when mode is resume."}
          },
          "required":["mode"],
          "additionalProperties":false
        }
        """).RootElement.Clone();

    private static AITool CreateNativeTool(string name, bool? eagerInputStreaming) =>
        new BetaToolUnion(new BetaTool
        {
            Name = name,
            InputSchema = new InputSchema(),
            EagerInputStreaming = eagerInputStreaming
        }).AsAITool();

    private static BetaTool GetBetaTool(AITool tool)
    {
        var union = Assert.IsType<BetaToolUnion>(tool.GetService(typeof(BetaToolUnion)));
        return Assert.IsType<BetaTool>(union.Value);
    }

    private static JsonObject GetSerializedTool(string requestJson)
    {
        var root = JsonNode.Parse(requestJson)!.AsObject();
        return Assert.IsType<JsonObject>(Assert.Single(root["tools"]!.AsArray()));
    }

    private static IChatClient CreateBetaClient(CaptureHandler handler)
    {
        var anthropicClient = new AnthropicClient
        {
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            ApiKey = "test-key"
        };
        return anthropicClient.Beta.AsIChatClient("claude-sonnet-4-6", defaultMaxOutputTokens: 1024);
    }

    private static EffectiveModelRuntime CreateRuntime() => new(
        "provider",
        "claude-sonnet-4-6",
        ModelProviderProtocols.Anthropic,
        "Provider",
        "test-key",
        "https://api.anthropic.com",
        1024,
        null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic));

    private sealed class SchemaFunction(string name, string description, JsonElement schema) : AIFunction
    {
        public override string Name => name;
        public override string Description => description;
        public override JsonElement JsonSchema => schema;
        public override JsonElement? ReturnJsonSchema => null;
        public override MethodInfo? UnderlyingMethod => null;
        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>("ok");
    }

    private sealed class CaptureHandler : HttpMessageHandler
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
                        "id": "msg_eager_input_test",
                        "type": "message",
                        "role": "assistant",
                        "model": "claude-sonnet-4-6",
                        "content": [{ "type": "text", "text": "ok" }],
                        "stop_reason": "end_turn",
                        "usage": { "input_tokens": 10, "output_tokens": 1 }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
