using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicToolSchemaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_narrows_unsupported_shapes_and_describes_removed_constraints(bool deferred)
    {
        var schema = Schema("""
            {
              "type": "object", "description": "Original constraints.",
              "oneOf": [{ "required": ["a"] }, { "required": ["b"] }],
              "properties": { "a": { "type": "string" }, "b": { "type": "string" } },
              "unevaluatedProperties": false, "minProperties": 2
            }
            """);

        var (request, _) = await CaptureRequest(Tool(schema), deferred);
        var input = request.GetProperty("tools")[0].GetProperty("input_schema");

        Assert.False(input.TryGetProperty("oneOf", out _));
        Assert.Equal(2, input.GetProperty("anyOf").GetArrayLength());
        Assert.False(input.TryGetProperty("unevaluatedProperties", out _));
        Assert.False(input.TryGetProperty("minProperties", out _));
        var description = input.GetProperty("description").GetString()!;
        Assert.StartsWith("Original constraints.", description, StringComparison.Ordinal);
        Assert.Contains("minProperties: 2", description, StringComparison.Ordinal);
        Assert.Contains("unevaluatedProperties: false", description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_preserves_nullable_union_members_and_nested_constraints(bool deferred)
    {
        var schema = Schema("""
            {
              "type": "object",
              "properties": {
                "email": { "type": ["string", "null"], "format": "email" },
                "entries": {
                  "type": ["array", "null"], "minItems": 1,
                  "items": {
                    "type": ["object", "null"],
                    "properties": { "id": { "type": ["string", "null"], "format": "uuid" } },
                    "required": ["id"]
                  }
                },
                "choice": {
                  "type": ["string", "array", "null"], "format": "date", "minItems": 1,
                  "items": { "type": "string" }
                }
              }
            }
            """);

        var tool = AIFunctionFactory.CreateDeclaration("records__Lookup", "Look up records.", schema);
        var (request, _) = await CaptureRequest(tool, deferred);

        var wireTool = Assert.Single(request.GetProperty("tools").EnumerateArray());
        Assert.Equal(tool.Name, wireTool.GetProperty("name").GetString());
        Assert.Equal(tool.Description, wireTool.GetProperty("description").GetString());
        Assert.False(wireTool.TryGetProperty("strict", out _));
        Assert.Equal(deferred, wireTool.TryGetProperty("defer_loading", out var deferLoading));
        if (deferred)
            Assert.True(deferLoading.GetBoolean());
        var input = wireTool.GetProperty("input_schema");
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        var properties = input.GetProperty("properties");
        Assert.Equal("email", properties.GetProperty("email").GetProperty("format").GetString());
        Assert.Equal(new[] { "string", "null" }, Strings(properties.GetProperty("email").GetProperty("type")));
        var entries = properties.GetProperty("entries");
        Assert.Equal(new[] { "array", "null" }, Strings(entries.GetProperty("type")));
        Assert.Equal(1, entries.GetProperty("minItems").GetInt32());
        var item = entries.GetProperty("items");
        Assert.Equal(new[] { "object", "null" }, Strings(item.GetProperty("type")));
        Assert.Equal("id", Assert.Single(Strings(item.GetProperty("required"))));
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("uuid", item.GetProperty("properties").GetProperty("id").GetProperty("format").GetString());
        var choice = properties.GetProperty("choice");
        Assert.Equal("date", choice.GetProperty("format").GetString());
        Assert.Equal(1, choice.GetProperty("minItems").GetInt32());
        Assert.Equal("string", choice.GetProperty("items").GetProperty("type").GetString());
        Assert.True(JsonElement.DeepEquals(schema, tool.JsonSchema));
        Assert.False(tool.AdditionalProperties.ContainsKey(nameof(BetaTool.DeferLoading)));
    }

    [Fact]
    public async Task Deferred_projection_does_not_enable_other_sdk_tool_options_or_mutate_the_source()
    {
        var properties = new Dictionary<string, object?>
        {
            [nameof(BetaTool.DeferLoading)] = false,
            [nameof(BetaTool.Strict)] = true,
            [nameof(BetaTool.InputExamples)] = new List<Dictionary<string, JsonElement>> { new() },
            [nameof(BetaTool.AllowedCallers)] = new List<ApiEnum<string, BetaToolAllowedCaller>> { BetaToolAllowedCaller.Direct },
            [nameof(BetaTool.CacheControl)] = new BetaCacheControlEphemeral()
        };
        var tool = new TestDeclaration(Schema("""{"type":"object"}"""), properties);
        var (request, _) = await CaptureRequest(tool, deferred: true);

        var wireTool = request.GetProperty("tools")[0];
        Assert.True(wireTool.GetProperty("defer_loading").GetBoolean());
        Assert.False(wireTool.TryGetProperty("strict", out _));
        Assert.False(wireTool.TryGetProperty("input_examples", out _));
        Assert.False(wireTool.TryGetProperty("cache_control", out _));
        Assert.False(wireTool.TryGetProperty("allowed_callers", out _));
        Assert.False(Assert.IsType<bool>(properties[nameof(BetaTool.DeferLoading)]));
        Assert.True(Assert.IsType<bool>(properties[nameof(BetaTool.Strict)]));
        Assert.Equal(5, properties.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_tools_without_a_schema_keep_the_default_object_schema(bool declaration)
    {
        AITool tool = declaration ? new TestDeclaration(default, new Dictionary<string, object?>()) : new SchemalessTool();
        var (request, _) = await CaptureRequest(tool, deferred: true);

        var wireTool = request.GetProperty("tools")[0];
        Assert.Equal(tool.Name, wireTool.GetProperty("name").GetString());
        Assert.Equal("object", wireTool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.True(wireTool.GetProperty("defer_loading").GetBoolean());
        if (tool is AIFunctionDeclaration function)
            Assert.Equal(JsonValueKind.Undefined, function.JsonSchema.ValueKind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Required_tool_choice_and_beta_headers_survive_deferred_projection(bool streaming, bool specific)
    {
        var options = new ChatOptions
        {
            ToolMode = specific ? ChatToolMode.RequireSpecific("Probe") : ChatToolMode.RequireAny,
            AllowMultipleToolCalls = false,
            RawRepresentationFactory = _ => new MessageCreateParams
            {
                Model = "claude-sonnet-4-5", MaxTokens = 1024, Messages = [],
                Betas = ["fast-mode-2026-02-01"]
            }
        };
        var (request, betaHeaders) = await CaptureRequest(Tool(Schema("""{"type":"object"}""")), true, streaming, options);

        var choice = request.GetProperty("tool_choice");
        Assert.Equal(specific ? "tool" : "any", choice.GetProperty("type").GetString());
        Assert.True(choice.GetProperty("disable_parallel_tool_use").GetBoolean());
        if (specific)
            Assert.Equal("Probe", choice.GetProperty("name").GetString());
        else
            Assert.False(choice.TryGetProperty("name", out _));
        Assert.Equal(
            new[] { "fast-mode-2026-02-01", AnthropicDeferredToolLoadingChatClient.ToolSearchBetaHeader }.Order(),
            betaHeaders.SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries)).Order());
    }

    private static async Task<(JsonElement Request, string[] BetaHeaders)> CaptureRequest(
        AITool tool, bool deferred, bool streaming = false, ChatOptions? options = null)
    {
        using var handler = new CaptureHandler(streaming);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var sdk = new AnthropicClient { HttpClient = httpClient, ApiKey = "test-key", MaxRetries = 0 };
        using IChatClient client = new AnthropicDeferredToolLoadingChatClient(
            sdk.Beta.AsIChatClient("claude-sonnet-4-5", 1024), "claude-sonnet-4-5", 1024,
            deferred ? new ActivatedToolView(tool) : null);
        options ??= new ChatOptions();
        options.Tools = deferred ? [] : [tool];
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Probe.")], options)) { }
        }
        else
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Probe.")], options);
        }
        return (Schema(handler.RequestJson!), handler.BetaHeaders);
    }

    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static AITool Tool(JsonElement schema) =>
        AIFunctionFactory.CreateDeclaration("Probe", "A probe.", schema);

    private static string?[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()).ToArray();

    private sealed class ActivatedToolView(AITool tool) : IDeferredToolActivationView
    {
        public IReadOnlyList<string> GetActivatedToolNames() => [tool.Name];
        public bool TryGetTool(string name, out AITool? result)
        {
            result = tool;
            return name == tool.Name;
        }
    }

    private sealed class TestDeclaration(JsonElement schema, IReadOnlyDictionary<string, object?> properties) : AIFunctionDeclaration
    {
        public override string Name => "Probe";
        public override string Description => "A probe.";
        public override JsonElement JsonSchema => schema;
        public override IReadOnlyDictionary<string, object?> AdditionalProperties => properties;
    }

    private sealed class SchemalessTool : AITool
    {
        public override string Name => "Schemaless";
        public override string Description => "A tool without a function declaration.";
    }

    private sealed class CaptureHandler(bool streaming) : HttpMessageHandler
    {
        public string? RequestJson { get; private set; }
        public string[] BetaHeaders { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            BetaHeaders = request.Headers.TryGetValues("anthropic-beta", out var values) ? values.ToArray() : [];
            const string message = """
                {"id":"msg_probe","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":1}}
                """;
            var body = streaming
                ? $"event: message_start\ndata: {{\"type\":\"message_start\",\"message\":{message}}}\n\nevent: message_delta\ndata: {{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"end_turn\",\"stop_sequence\":null}},\"usage\":{{\"output_tokens\":1}}}}\n\nevent: message_stop\ndata: {{\"type\":\"message_stop\"}}\n\n"
                : message;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
