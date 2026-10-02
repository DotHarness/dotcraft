using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    private const string FreeformGrammar = "start: SOURCE\nSOURCE: /[\\s\\S]+/\n";

    [Fact]
    public void CreateResponseOptions_ProjectsFreeformDefinitionAsCustomGrammarTool()
    {
        var tools = ToolSchemaSanitizer.SanitizeTools(AgentFactory.ProjectSnapshotTools(CreateFreeformSnapshot()));

        var json = CreateRequestJson(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "run it")],
            new ChatOptions { Tools = tools });

        using var document = JsonDocument.Parse(json);
        var tool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(
                """
                {
                  "type": "custom",
                  "name": "exec",
                  "description": "Run a program.",
                  "format": {
                    "type": "grammar",
                    "syntax": "lark",
                    "definition": "start: SOURCE\nSOURCE: /[\\s\\S]+/\n"
                  }
                }
                """),
            JsonNode.Parse(tool.GetRawText())));
    }

    [Fact]
    public async Task StreamingFunctionLoop_DispatchesCustomToolCallAndRepliesWithCustomOutput()
    {
        const string program = "// @exec: {\"timeout_ms\": 1000}\nreturn \"a\\\\b\";";
        var exec = new FreeformFunction("exec", "code", "Script completed");
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner response")]));
        var transport = new FakeToolSearchTransport([
            [
                CreateStreamingUpdate(
                    $$"""
                    {
                      "type": "response.output_item.done",
                      "sequence_number": 1,
                      "output_index": 0,
                      "item": {
                        "type": "custom_tool_call",
                        "id": "ctc_001",
                        "call_id": "call_exec",
                        "name": "exec",
                        "input": {{JsonSerializer.Serialize(program)}},
                        "status": "completed"
                      }
                    }
                    """)
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
        using var invokingClient = new StreamingFunctionInvokingChatClient(responsesClient);

        var updates = await CollectStreamingAsync(invokingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "run it")],
            new ChatOptions { Tools = [exec] }));

        Assert.Equal(program, Assert.Single(exec.Programs));
        var call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.True(ProviderFunctionCallMetadata.IsCustomToolCall(call));
        Assert.Equal(2, transport.Requests.Count);
        using var second = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        var input = second.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.DoesNotContain(input, item => item.GetProperty("type").GetString() is "function_call" or "function_call_output");
        var customCall = input.Single(item => item.GetProperty("type").GetString() == "custom_tool_call");
        Assert.Equal("call_exec", customCall.GetProperty("call_id").GetString());
        Assert.Equal("exec", customCall.GetProperty("name").GetString());
        Assert.Equal(program, customCall.GetProperty("input").GetString());
        Assert.StartsWith("ctc_", customCall.GetProperty("id").GetString(), StringComparison.Ordinal);
        var output = input.Single(item => item.GetProperty("type").GetString() == "custom_tool_call_output");
        Assert.Equal(
            ["call_id", "output", "type"],
            output.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("call_exec", output.GetProperty("call_id").GetString());
        Assert.Equal("Script completed", output.GetProperty("output").GetString());
    }

    [Fact]
    public void CreateResponseOptions_ReplaysCallAsCustomOnlyWhenMarked()
    {
        var unmarked = new FunctionCallContent("call_exec", "exec", new Dictionary<string, object?> { ["code"] = "return 1;" });
        var marked = new FunctionCallContent("call_exec", "exec", new Dictionary<string, object?> { ["code"] = "return 1;" });
        ProviderFunctionCallMetadata.MarkCustomToolCall(marked);
        ChatMessage[] Replay(FunctionCallContent call) =>
        [
            new(ChatRole.User, "run it"),
            new(ChatRole.Assistant, [call]),
            new(ChatRole.Tool, [new FunctionResultContent("call_exec", "Script completed")])
        ];
        var declaredTools = new ChatOptions { Tools = [new FreeformFunction("exec", "code", "unused")] };

        using var unmarkedDeclared = JsonDocument.Parse(CreateRequestJson("gpt-test", Replay(unmarked), declaredTools));
        using var markedUndeclared = JsonDocument.Parse(CreateRequestJson("gpt-test", Replay(marked), new ChatOptions()));

        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            unmarkedDeclared.RootElement.GetProperty("input").EnumerateArray().Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal(
            ["message", "custom_tool_call", "custom_tool_call_output"],
            markedUndeclared.RootElement.GetProperty("input").EnumerateArray().Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal(
            "return 1;",
            markedUndeclared.RootElement.GetProperty("input")[1].GetProperty("input").GetString());
    }

    [Fact]
    public void CreateResponseRequest_WithoutFreeformSupport_ProjectsFunctionToolAndReplaysMarkedCallAsFunction()
    {
        var tools = ToolSchemaSanitizer.SanitizeTools(AgentFactory.ProjectSnapshotTools(CreateFreeformSnapshot()));
        var call = new FunctionCallContent("call_exec", "exec", new Dictionary<string, object?> { ["code"] = "return 1;" });
        ProviderFunctionCallMetadata.MarkCustomToolCall(call);
        ChatMessage[] history =
        [
            new(ChatRole.User, "run it"),
            new(ChatRole.Assistant, [call]),
            new(ChatRole.Tool, [new FunctionResultContent("call_exec", "Script completed")])
        ];

        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            history,
            new ChatOptions { Tools = tools },
            supportsFreeformTools: false);

        using var document = JsonDocument.Parse(SerializeOptions(request.Options));
        var tool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("exec", tool.GetProperty("name").GetString());
        Assert.Equal(
            "string",
            tool.GetProperty("parameters").GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
        Assert.False(tool.TryGetProperty("format", out _));
        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            input.Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal("{\"code\":\"return 1;\"}", input[1].GetProperty("arguments").GetString());
        Assert.Equal("Script completed", input[2].GetProperty("output").GetString());
    }

    [Fact]
    public void CreateResponseRequest_WithoutFreeformSupport_SendsCanonicalCustomItemsAsFunctionItems()
    {
        var canonicalInput = new JsonArray
        {
            new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = "run it" },
            new JsonObject
            {
                ["type"] = "custom_tool_call",
                ["id"] = "ctc_001",
                ["call_id"] = "call_exec",
                ["name"] = "exec",
                ["input"] = "return 1;"
            },
            new JsonObject { ["type"] = "custom_tool_call_output", ["call_id"] = "call_exec", ["output"] = "ok" }
        };

        var request = ResponsesToolSearchMapper.CreateResponseRequest(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "run it")],
            new ChatOptions { Tools = [new FreeformFunction("exec", "code", "unused")] },
            canonicalInput: canonicalInput,
            supportsFreeformTools: false);

        using var document = JsonDocument.Parse(SerializeOptions(request.Options));
        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            input.Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal("{\"code\":\"return 1;\"}", input[1].GetProperty("arguments").GetString());
        Assert.False(input[1].TryGetProperty("input", out _));
        Assert.Equal("call_exec", input[2].GetProperty("call_id").GetString());
    }

    private static EffectiveToolSnapshot CreateFreeformSnapshot()
    {
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, "code-mode", new SourceToolId("exec"));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(null, "exec"),
            "Run a program.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { code = new { type = "string", description = "The program." } },
                required = new[] { "code" }
            }),
            freeformInput: new ToolFreeformInput("code", "lark", FreeformGrammar));
        var registration = new ToolRegistration(
            definition,
            new ToolRuntimeBinding(
                new RuntimeBindingId("code-mode:exec"),
                definitionId,
                new FreeformToolRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "code-mode",
                1),
            ToolProjectionShape.StandardPair);
        return new EffectiveToolSnapshotBuilder().Build([registration], 1);
    }

    private sealed class FreeformToolRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class FreeformFunction(string name, string parameterName, string result)
        : AIFunction, IOpenAIResponsesFunctionToolMetadata
    {
        private readonly JsonElement _schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object> { [parameterName] = new { type = "string" } },
            required = new[] { parameterName }
        });

        public List<string> Programs { get; } = [];

        public override string Name => name;

        public override string Description => "Run a program.";

        public override JsonElement JsonSchema => _schema;

        public override MethodInfo? UnderlyingMethod => null;

        public bool? Strict => null;

        public bool ReservedSchema => false;

        public ToolFreeformInput? FreeformInput { get; } = new(parameterName, "lark", FreeformGrammar);

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            Programs.Add(arguments[parameterName] switch
            {
                JsonElement element => element.GetString()!,
                var value => value!.ToString()!
            });
            return ValueTask.FromResult<object?>(result);
        }
    }
}
