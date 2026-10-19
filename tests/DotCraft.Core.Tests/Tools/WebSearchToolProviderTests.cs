using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class WebSearchToolProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-websearch-test-" + Guid.NewGuid().ToString("N"));

    public WebSearchToolProviderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".craft"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    public static TheoryData<string, AppConfig, bool> Availability => new()
    {
        { "subscription oauth", OAuthConfig(), true },
        { "subscription with explicit DeepSeek search", OAuthConfig(searchProvider: "DeepSeek"), true },
        { "subscription oauth chat completions", OAuthConfig(ModelProviderProtocols.OpenAIChatCompletions), true },
        { "api key", ApiKeyConfig(), false },
        { "anthropic", AnthropicConfig(), false }
    };

    [Theory]
    [MemberData(nameof(Availability))]
    public async Task Registrations_ExposeWebRunAndHideBuiltInSearchOnlyOnSubscriptionRuntimes(
        string scenario,
        AppConfig config,
        bool hosted)
    {
        var registry = new ChatClientRegistry(new FakeSearchProvider());
        var hostedRegistrations = await new WebSearchToolSource(config, registry).GetRegistrationsAsync(PlanningContext());
        var coreNames = (await new CoreToolSource(
                config,
                registry,
                new SkillsLoader(_root),
                new AutoApproveApprovalService(),
                new StubBackgroundTerminalService())
            .GetRegistrationsAsync(PlanningContext()))
            .Select(static registration => registration.Definition.Name.ToString())
            .ToList();

        Assert.Contains("WebFetch", coreNames);
        if (!hosted)
        {
            Assert.True(hostedRegistrations.Count == 0, scenario);
            Assert.Contains("WebSearch", coreNames);
            return;
        }

        var registration = Assert.Single(hostedRegistrations);
        Assert.Equal(new ToolName("web", "run"), registration.Definition.Name);
        Assert.Equal(ToolProjectionShape.WebSearch, registration.ProjectionShape);
        Assert.True(ReservedToolSchema.IsReserved(registration.Definition));
        Assert.DoesNotContain("WebSearch", coreNames);
    }

    [Fact]
    public async Task ResponsesRequest_SendsTheReservedSchemaVerbatimAndNonStrict()
    {
        var registration = Assert.Single(await new WebSearchToolSource(
                OAuthConfig(),
                new ChatClientRegistry(new FakeSearchProvider()))
            .GetRegistrationsAsync(PlanningContext()));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        var tools = ToolSchemaSanitizer.SanitizeTools(AgentFactory.ProjectSnapshotTools(snapshot));

        var request = JsonNode.Parse(ModelReaderWriter.Write(ResponsesToolSearchMapper.CreateResponseOptions(
            "gpt-test",
            [new ChatMessage(ChatRole.User, "news")],
            new ChatOptions { Tools = tools })).ToString())!;

        var tool = Assert.Single(request["tools"]!.AsArray())!["tools"]![0]!;
        Assert.Equal("run", tool["name"]!.GetValue<string>());
        Assert.False(tool["strict"]!.GetValue<bool>());
        Assert.Equal(
            JsonNode.Parse(registration.Definition.InputSchema.GetRawText())!.ToJsonString(),
            tool["parameters"]!.ToJsonString());
        Assert.Equal(
            new[] { "click", "finance", "find", "image_query", "open", "response_length", "screenshot", "search_query", "sports", "time", "weather" },
            tool["parameters"]!["properties"]!.AsObject().Select(static pair => pair.Key));
    }

    [Fact]
    public async Task Invoke_SendsThreadIdConversationTailAndCommandsWithoutNulls()
    {
        var provider = new FakeSearchProvider
        {
            Result = new ProviderWebSearchResult(
                "Result citeturn0search0",
                [new JsonObject { ["type"] = "text_result", ["url"] = "https://example.com", ["ref_id"] = "turn0search0" }])
        };
        var runtime = await CreateRuntimeAsync(provider);

        var result = await runtime.InvokeAsync(
            InvocationContext("call_1"),
            JsonNode.Parse("""{"search_query":[{"q":"dotnet 10","recency":null}],"open":null,"response_length":"short"}""")!.AsObject());

        Assert.True(result.Success, result.Content);
        Assert.Equal("Result citeturn0search0", result.Content);
        var request = Assert.Single(provider.Requests);
        Assert.Equal("thread_1", request.Id);
        Assert.Equal(
            """{"search_query":[{"q":"dotnet 10"}],"response_length":"short"}""",
            request.Commands.ToJsonString());
        Assert.Equal(
            "https://example.com",
            result.StructuredContent!.Value[0].GetProperty("url").GetString());

        var item = WebSearchProjection.Completed(
            WebSearchProjection.Started("call_1", JsonNode.Parse("""{"search_query":[{"q":"dotnet 10"}]}""")!.AsObject()),
            result);
        Assert.Equal("completed", item.Status);
        Assert.Equal("dotnet 10", item.Query);
        Assert.Equal("""{"type":"search","query":"dotnet 10","queries":null}""", item.Action!.ToJsonString());
        Assert.Equal("turn0search0", item.Results![0]!["ref_id"]!.GetValue<string>());
        Assert.Equal(result.Content, item.Output);
    }

    [Fact]
    public void BuildInput_KeepsTextFromTheSecondToLastUserMessageAndSkipsContextAndTools()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.System, "system prompt"),
            new(ChatRole.User, "first question"),
            new(ChatRole.Assistant, "first answer"),
            new(ChatRole.User, [new TextContent("second question"), new DataContent(new byte[] { 1 }, "image/png"), new TextContent("\n<system-reminder>\nctx\n</system-reminder>")]),
            ThreadContextItems.Create(ThreadContextCarrier.DeveloperMessage, ThreadContextItems.ClientContextKind, "client"),
            new(ChatRole.Assistant, [new TextReasoningContent("thinking"), new TextContent("checking"), new FunctionCallContent("c1", "run")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "tool output")]),
            new(ChatRole.User, "third question"),
            new(ChatRole.Assistant, [new FunctionCallContent("c2", "run")])
        ];

        var input = WebSearchToolRuntime.BuildInput(messages);

        Assert.Equal(
            """
            [{"type":"message","role":"user","content":[{"type":"input_text","text":"second question"}]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"checking"}]},{"type":"message","role":"user","content":[{"type":"input_text","text":"third question"}]}]
            """,
            input.ToJsonString());
    }

    [Fact]
    public void BuildInput_CapsAssistantTextByDroppingTheOldestFirst()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, new string('a', 1000)),
            new(ChatRole.Assistant, new string('b', 3500)),
            new(ChatRole.User, "second"),
            new(ChatRole.Assistant, new string('c', 3000))
        ];

        var input = WebSearchToolRuntime.BuildInput(messages);

        var texts = input
            .Select(static message => message!["content"]![0]!["text"]!.GetValue<string>())
            .ToList();
        Assert.Equal(new[] { "first", new string('b', 1000), "second", new string('c', 3000) }, texts);
    }

    public static TheoryData<string, string, string> Actions => new()
    {
        {
            """{"search_query":[{"q":"a"},{"q":"b"}]}""",
            """{"type":"search","query":null,"queries":["a","b"]}""",
            "a ..."
        },
        {
            """{"search_query":[],"image_query":[{"q":"cats"}]}""",
            """{"type":"search","query":"cats","queries":null}""",
            "cats"
        },
        {
            """{"open":[{"ref_id":"turn0search1"},{"ref_id":"https://example.com/a"}]}""",
            """{"type":"openPage","url":"https://example.com/a"}""",
            "https://example.com/a"
        },
        {
            """{"find":[{"ref_id":"https://example.com/a","pattern":"price"}]}""",
            """{"type":"findInPage","url":"https://example.com/a","pattern":"price"}""",
            "'price' in https://example.com/a"
        },
        {
            """{"open":[{"ref_id":"turn0search1"}],"find":[{"ref_id":"turn0fetch2","pattern":"price"}]}""",
            """{"type":"findInPage","url":null,"pattern":"price"}""",
            "'price'"
        },
        {
            """{"weather":[{"location":"Japan, Tokyo, Tokyo"}]}""",
            """{"type":"other"}""",
            ""
        }
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public void Projection_DerivesActionAndQueryFromCommands(string commands, string action, string query)
    {
        var derived = WebSearchProjection.DeriveAction(JsonNode.Parse(commands)!.AsObject());

        Assert.Equal(action, derived.ToJsonString());
        Assert.Equal(query, WebSearchProjection.DeriveQuery(derived));
    }

    [Fact]
    public async Task Invoke_ServiceFailureReturnsErrorTextAndFailsTheItem()
    {
        var provider = new FakeSearchProvider
        {
            Error = new HttpRequestException("Web search request failed with HTTP 403: error code: 1010", null, HttpStatusCode.Forbidden)
        };
        var runtime = await CreateRuntimeAsync(provider);

        var result = await runtime.InvokeAsync(InvocationContext("call_fail"), new JsonObject());

        Assert.False(result.Success);
        Assert.Equal(WebSearchToolRuntime.FailedErrorCode, result.Error?.Code);
        Assert.Equal("web search failed: Web search request failed with HTTP 403: error code: 1010", result.Content);
        Assert.Equal("{}", Assert.Single(provider.Requests).Commands.ToJsonString());
        var item = WebSearchProjection.Completed(WebSearchProjection.Started("call_fail", new JsonObject()), result);
        Assert.Equal("failed", item.Status);
        Assert.Equal("Web search request failed with HTTP 403: error code: 1010", item.ErrorMessage);
        Assert.Null(item.Action);
        Assert.Null(item.Results);
    }

    [Fact]
    public async Task Invoke_CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeSearchProvider { CancelWith = cancellation };
        var runtime = await CreateRuntimeAsync(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runtime.InvokeAsync(InvocationContext("call_cancel"), new JsonObject(), cancellation.Token));
    }

    private async Task<WebSearchToolRuntime> CreateRuntimeAsync(FakeSearchProvider provider)
    {
        var registration = Assert.Single(await new WebSearchToolSource(OAuthConfig(), new ChatClientRegistry(provider))
            .GetRegistrationsAsync(PlanningContext()));
        return Assert.IsType<WebSearchToolRuntime>(registration.Binding.Runtime);
    }

    private ToolPlanningContext PlanningContext() =>
        new("thread_1", "turn_1", _root, Path.Combine(_root, ".craft"), "agent", null, null, 1);

    private ToolInvocationContext InvocationContext(string callId) =>
        new("thread_1", "turn_1", callId, ToolInvocationAudience.Model,
            new ToolName("web", "run"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "web-search", new SourceToolId("run")),
            new RuntimeBindingId("native:web-search:run:1"), 1, DateTimeOffset.UtcNow,
            WorkspacePath: _root);

    private static AppConfig OAuthConfig(string protocol = ModelProviderProtocols.OpenAIResponses, string searchProvider = "Auto")
    {
        var config = new AppConfig
        {
            ProviderId = "subscription",
            ProviderPreferences = new() { ["subscription"] = new ModelPreference { Model = "gpt-5" } }
        };
        config.Providers["subscription"] = new AppConfig.ModelProviderConfig
        {
            Protocol = protocol,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
        };
        config.Tools.Web.SearchProvider = searchProvider;
        return config;
    }

    private static AppConfig ApiKeyConfig()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-5" } }
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = "sk-test"
        };
        return config;
    }

    [Fact]
    public async Task WebSearch_SearchesThroughTheChatGptSubscriptionFromAnotherProvider()
    {
        var config = AnthropicConfig();
        config.Tools.Web.SearchProvider = "ChatGPT";
        config.Providers["subscription"] = new AppConfig.ModelProviderConfig
        {
            AuthMethod = "chatgptOAuth",
            Protocol = ModelProviderProtocols.OpenAIResponses
        };
        config.ProviderPreferences["subscription"] = new ModelPreference { Model = "gpt-test" };
        var provider = new FakeSearchProvider
        {
            Result = new("page text", [
                new JsonObject { ["ref_id"] = "turn0search0", ["url"] = "https://example.com/a", ["title"] = "A", ["snippet"] = "about A" },
                new JsonObject { ["ref_id"] = "turn0search1" }
            ])
        };
        var tools = new WebTools(config, ModelProviderResolver.ResolveMain(config), chatClients: new ChatClientRegistry(provider));

        var output = await tools.WebSearch("dotcraft");

        var request = Assert.Single(provider.Requests);
        Assert.Empty(request.Input);
        Assert.Equal("dotcraft", request.Commands["search_query"]![0]!["q"]!.GetValue<string>());
        Assert.Contains("- [A](https://example.com/a)\n  about A\n", output.Content);
        Assert.Single(output.StructuredContent!.Value.GetProperty("results").EnumerateArray());
    }

    private static AppConfig AnthropicConfig()
    {
        var config = new AppConfig
        {
            ProviderId = "claude",
            ProviderPreferences = new() { ["claude"] = new ModelPreference { Model = "claude-test" } }
        };
        config.Providers["claude"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.Anthropic,
            ApiKey = "sk-ant-test"
        };
        return config;
    }

    private sealed class FakeSearchProvider : IModelProvider, IProviderWebSearch
    {
        public List<ProviderWebSearchRequest> Requests { get; } = [];

        public ProviderWebSearchResult Result { get; init; } = new("ok", []);

        public Exception? Error { get; init; }

        public CancellationTokenSource? CancelWith { get; init; }

        public IReadOnlyCollection<string> Protocols { get; } =
            [ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.OpenAIResponses, ModelProviderProtocols.Anthropic];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

        public Task<ProviderWebSearchResult> SearchAsync(
            EffectiveModelRuntime runtime,
            ProviderWebSearchRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (CancelWith is not null)
            {
                CancelWith.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Error is null
                ? Task.FromResult(Result)
                : Task.FromException<ProviderWebSearchResult>(Error);
        }
    }
}
