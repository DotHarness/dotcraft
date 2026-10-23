using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class DeferredLoadingSmokeTests
{
    private const string SmokeToolNamespace = "mcp__deferred_loading_smoke";
    private const string QualifiedEchoToolName = SmokeToolNamespace + "__" + DeferredLoadingSmokeTools.Echo;

    [Fact]
    public void BuildConfigJson_ConfiguresNativeDeferredMcpTools()
    {
        var json = DeferredLoadingSmokeWorkspace.BuildConfigJson(
            "anthropic",
            "claude-smoke",
            "dotnet",
            ["dotcraft-test-client.dll", "deferred-loading-smoke-mcp-server"]);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("anthropic", root.GetProperty("ProviderId").GetString());
        Assert.Equal("claude-smoke", root.GetProperty("ProviderPreferences").GetProperty("anthropic").GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("Providers", out _));
        Assert.True(root.GetProperty("Tracing").GetProperty("Enabled").GetBoolean());

        var mcpServer = root.GetProperty("McpServers").GetProperty(DeferredLoadingSmokeWorkspace.McpServerName);
        Assert.Equal("stdio", mcpServer.GetProperty("Transport").GetString());
        Assert.Equal("dotnet", mcpServer.GetProperty("Command").GetString());
        Assert.Equal("dotcraft-test-client.dll", mcpServer.GetProperty("Arguments")[0].GetString());
        Assert.Equal("deferred-loading-smoke-mcp-server", mcpServer.GetProperty("Arguments")[1].GetString());

        var deferredLoading = root.GetProperty("Tools").GetProperty("DeferredLoading");
        Assert.Equal("Native", deferredLoading.GetProperty("Strategy").GetString());
        Assert.Equal(1, deferredLoading.GetProperty("DeferThreshold").GetInt32());
        Assert.Equal(5, deferredLoading.GetProperty("MaxSearchResults").GetInt32());
        Assert.Equal(0, deferredLoading.GetProperty("AlwaysLoadedTools").GetArrayLength());

        var compaction = root.GetProperty("Compaction");
        Assert.False(compaction.GetProperty("AutoCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("ReactiveCompactEnabled").GetBoolean());
        Assert.False(compaction.GetProperty("MicrocompactEnabled").GetBoolean());
    }

    [Theory]
    [InlineData(ModelProviderProtocols.Anthropic, "anthropic_tool_reference")]
    [InlineData(ModelProviderProtocols.OpenAIResponses, "openai_responses_tool_search_output")]
    public void TraceValidator_PassesNativeDeferredLoadingForEachProtocol(string protocol, string wireShape)
    {
        var result = DeferredLoadingSmokeTraceValidator.Validate(
            ValidEvents(protocol, wireShape),
            protocol,
            DeferredLoadingSmokeTools.Echo);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DeferredToolLoadingObserved);
        Assert.Equal(wireShape, result.WireShape);
        Assert.Equal(QualifiedEchoToolName, result.TargetToolName);
    }

    [Fact]
    public void TraceValidator_RejectsMissingMismatchedOrCacheExtendingDeferredLoading()
    {
        var missing = DeferredLoadingSmokeTraceValidator.Validate([], ModelProviderProtocols.Anthropic, DeferredLoadingSmokeTools.Echo);
        Assert.False(missing.Success);
        Assert.Equal("deferred_loading_missing", missing.Message);

        var mismatched = DeferredLoadingSmokeTraceValidator.Validate(
            ValidEvents(ModelProviderProtocols.Anthropic, "openai_responses_tool_search_output"),
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);
        Assert.False(mismatched.Success);
        Assert.Equal("deferred_loading_wire_shape_mismatch", mismatched.Message);
        Assert.Equal("openai_responses_tool_search_output", mismatched.WireShape);

        var extension = DeferredLoadingSmokeTraceValidator.Validate(
            [
                Event(
                    nameof(TraceEventType.DeferredToolLoading),
                    metadataJson: DeferredMetadata(ModelProviderProtocols.Anthropic, "anthropic_tool_reference"),
                    promptCacheEventKind: PromptCacheEventKinds.ToolExtension,
                    promptCacheChangedFields: [PromptCacheChangedFields.Tools]),
                Event(nameof(TraceEventType.ToolCallStarted), toolName: QualifiedEchoToolName),
                Event(nameof(TraceEventType.ToolCallCompleted), toolName: QualifiedEchoToolName),
                Event(nameof(TraceEventType.Response), content: DeferredLoadingSmokeTools.SuccessToken)
            ],
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);
        Assert.False(extension.Success);
        Assert.Equal("deferred_loading_marked_prompt_cache_extension", extension.Message);
        Assert.True(extension.DeferredToolLoadingObserved);
    }

    private static DeferredLoadingSmokeTraceEvent[] ValidEvents(string protocol, string wireShape) =>
    [
        Event(nameof(TraceEventType.DeferredToolLoading), metadataJson: DeferredMetadata(protocol, wireShape)),
        Event(nameof(TraceEventType.ToolCallStarted), toolName: QualifiedEchoToolName),
        Event(nameof(TraceEventType.ToolCallCompleted), toolName: QualifiedEchoToolName),
        Event(nameof(TraceEventType.Response), content: DeferredLoadingSmokeTools.SuccessToken)
    ];

    private static string DeferredMetadata(string protocol, string wireShape) =>
        new JsonObject
        {
            ["strategy"] = "Native",
            ["effectiveMode"] = "Native",
            ["providerProtocol"] = protocol,
            ["trigger"] = "SearchTools",
            ["wireShape"] = wireShape,
            ["query"] = DeferredLoadingSmokeTools.Echo,
            ["deferredToolCount"] = 2,
            ["requestedMaxResults"] = 5,
            ["maxSearchResults"] = 5,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = DeferredLoadingSmokeTools.Echo,
                    ["source"] = DeferredLoadingSmokeWorkspace.McpServerName,
                    ["namespace"] = SmokeToolNamespace
                }
            }
        }.ToJsonString();

    private static DeferredLoadingSmokeTraceEvent Event(
        string type,
        string? toolName = null,
        string? content = null,
        string? metadataJson = null,
        string? promptCacheEventKind = null,
        string[]? promptCacheChangedFields = null)
    {
        var eventJson = JsonSerializer.Serialize(new
        {
            Type = type,
            ToolName = toolName,
            Content = content,
            MetadataJson = metadataJson,
            PromptCacheEventKind = promptCacheEventKind,
            PromptCacheChangedFields = promptCacheChangedFields
        });
        return new DeferredLoadingSmokeTraceEvent(1, "thread-1", type, eventJson);
    }
}
