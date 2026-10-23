using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

namespace DotCraft.Tests.Tools;

public sealed class ToolSchemaSanitizerTests
{
    [Fact]
    public void SanitizeTool_RewritesNullablePrimitivesAndRemovesNullDefaultsIdempotently()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var optional = GetPropertySchema(sanitized.JsonSchema, "optional");
        Assert.Equal("string", optional.GetProperty("type").GetString());
        Assert.False(optional.TryGetProperty("default", out _));
        Assert.Equal("Optional nullable string.", optional.GetProperty("description").GetString());
        foreach (var (name, type) in new[] { ("count", "integer"), ("enabled", "boolean"), ("ratio", "number") })
        {
            var property = GetPropertySchema(sanitized.JsonSchema, name);
            Assert.Equal(type, property.GetProperty("type").GetString());
            Assert.False(property.TryGetProperty("default", out _));
        }

        Assert.Equal("string", GetPropertySchema(sanitized.JsonSchema, "required").GetProperty("type").GetString());
        var nonNullableDefault = GetPropertySchema(sanitized.JsonSchema, "nonNullableDefault");
        Assert.Equal("string", nonNullableDefault.GetProperty("type").GetString());
        Assert.Equal("", nonNullableDefault.GetProperty("default").GetString());
        Assert.IsNotAssignableFrom<IDeferredToolSearchMarker>(sanitized);
        Assert.Same(sanitized, ToolSchemaSanitizer.SanitizeTool(sanitized));
    }

    [Fact]
    public void SanitizeTool_DoesNotRewriteNullableObjectOrArrayTypes()
    {
        var function = AIFunctionFactory.Create(NullableObjectAndArrayTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var metadata = GetPropertySchema(sanitized.JsonSchema, "metadata");
        var tags = GetPropertySchema(sanitized.JsonSchema, "tags");

        Assert.Equal(JsonValueKind.Array, metadata.GetProperty("type").ValueKind);
        Assert.Contains("object", EnumerateTypeValues(metadata));
        Assert.Contains("null", EnumerateTypeValues(metadata));
        Assert.True(metadata.TryGetProperty("default", out var metadataDefault));
        Assert.Equal(JsonValueKind.Null, metadataDefault.ValueKind);

        Assert.Equal(JsonValueKind.Array, tags.GetProperty("type").ValueKind);
        Assert.Contains("array", EnumerateTypeValues(tags));
        Assert.Contains("null", EnumerateTypeValues(tags));
        Assert.True(tags.TryGetProperty("default", out var tagsDefault));
        Assert.Equal(JsonValueKind.Null, tagsDefault.ValueKind);
    }

    [Fact]
    public async Task SanitizedFunction_InvokesInnerFunction()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var result = await sanitized.InvokeAsync(new AIFunctionArguments
        {
            ["required"] = "hello",
            ["optional"] = "world"
        });

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("hello:world", json.GetString());
    }

    [Fact]
    public void SanitizeTool_PreservesDeferredToolSearchRegistryMarker()
    {
        var registry = new DeferredToolRegistry(
            [new DeferredToolEntry(AIFunctionFactory.Create(NullableStringTool, name: "LookupRecords"), "fixture", "fixture")],
            DeferredToolLoadingMode.Native);
        var sanitized = Assert.IsType<DeferredToolSearchSchemaSanitizingFunction>(
            ToolSchemaSanitizer.SanitizeTool(new AnthropicToolSearchTool(registry)));

        var marker = Assert.IsAssignableFrom<IDeferredToolSearchMarker>(sanitized);
        Assert.Same(registry, marker.Registry);
        Assert.Equal(AnthropicToolSearchTool.ToolName, sanitized.Name);
        Assert.Same(sanitized, ToolSchemaSanitizer.SanitizeTool(sanitized));
    }

    [Fact]
    public void DeferredRegistry_ActivatesToolsInDeterministicNameOrder()
    {
        var beta = AIFunctionFactory.Create(NullableStringTool, name: "BetaTool");
        var alpha = AIFunctionFactory.Create(NullableStringTool, name: "AlphaTool");
        var registry = new DeferredToolRegistry([beta, alpha]);

        var results = registry.SearchAndActivate("Tool", maxResults: 10);

        Assert.Equal(["AlphaTool", "BetaTool"], results.Select(r => r.Name).ToArray());
        Assert.Equal(["AlphaTool", "BetaTool"], registry.GetActivatedToolNames().ToArray());
        Assert.Equal(["AlphaTool", "BetaTool"], registry.ActivatedToolsList.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task DynamicToolInjection_InjectsSanitizedActivatedTools()
    {
        var rawTool = AIFunctionFactory.Create(NullableStringTool, name: "NullableStringTool");
        var registry = new DeferredToolRegistry(ToolSchemaSanitizer.SanitizeTools([rawTool]));
        registry.SearchAndActivate("NullableStringTool");

        var inner = new CapturingChatClient();
        var client = new DynamicToolInjectionChatClient(inner, registry);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")],
                           new ChatOptions()))
        {
        }

        var options = Assert.Single(inner.Options);
        var tool = Assert.IsType<ToolSchemaSanitizingFunction>(Assert.Single(options?.Tools ?? []));
        Assert.Equal("string", GetPropertySchema(tool.JsonSchema, "optional").GetProperty("type").GetString());
    }

    [Fact]
    public async Task DynamicToolInjection_RecordsToolExtensionDiagnostic()
    {
        const string sessionKey = "dynamic-tool-injection-session";
        var rawTool = AIFunctionFactory.Create(NullableStringTool, name: "NullableStringTool");
        var registry = new DeferredToolRegistry(ToolSchemaSanitizer.SanitizeTools([rawTool]));
        registry.SearchAndActivate("NullableStringTool");

        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var inner = new CapturingChatClient();
        var client = new DynamicToolInjectionChatClient(inner, registry, traceCollector: collector);

        TracingChatClient.CurrentSessionKey = sessionKey;
        try
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "hello")],
                               new ChatOptions()))
            {
            }
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = null;
        }

        var evt = Assert.Single(store.GetEvents(sessionKey), e => e.Type == TraceEventType.ToolInjection);
        Assert.Equal(PromptCacheEventKinds.ToolExtension, evt.PromptCacheEventKind);
        Assert.NotNull(evt.PromptCacheChangedFields);
        Assert.NotNull(evt.ChangedToolNames);
        Assert.Equal([PromptCacheChangedFields.Tools], evt.PromptCacheChangedFields);
        Assert.Equal(["NullableStringTool"], evt.ChangedToolNames);

        var session = store.GetSession(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(0, session.PromptDriftCount);
        Assert.Equal(PromptCacheEventKinds.ToolExtension, session.LastPromptCacheChangeKind);
        Assert.Equal([PromptCacheChangedFields.Tools], session.LastPromptCacheChangedFields);
    }

    private static string NullableStringTool(
        [System.ComponentModel.Description("Required string.")] string required,
        [System.ComponentModel.Description("Optional nullable string.")] string? optional = null,
        [System.ComponentModel.Description("Non-nullable string with default.")] string nonNullableDefault = "",
        int? count = null,
        bool? enabled = null,
        double? ratio = null) =>
        $"{required}:{optional ?? "null"}";

    private static string NullableObjectAndArrayTool(
        Dictionary<string, string>? metadata = null,
        string[]? tags = null) =>
        $"{metadata?.Count ?? 0}:{tags?.Length ?? 0}";

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);

    private static string[] EnumerateTypeValues(JsonElement schema) =>
        [.. schema.GetProperty("type").EnumerateArray().Select(value => value.GetString() ?? string.Empty)];

    private sealed class CapturingChatClient : IChatClient
    {
        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
