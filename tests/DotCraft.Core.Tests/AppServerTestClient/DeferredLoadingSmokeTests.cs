using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using DotCraft.Tracing;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class DeferredLoadingSmokeTests
{
    [Fact]
    public void MatrixLoad_ParsesProviderMappings()
    {
        var path = Path.Combine(Path.GetTempPath(), "deferred-loading-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "providers": [
                    {
                      "protocol": "anthropic",
                      "providerId": "anthropic-smoke",
                      "model": "claude-smoke"
                    }
                  ]
                }
                """);

            var matrix = DeferredLoadingSmokeMatrix.Load(path);

            var provider = Assert.Single(matrix.Providers);
            Assert.Equal(ModelProviderProtocols.Anthropic, provider.Protocol);
            Assert.Equal("anthropic-smoke", provider.ProviderId);
            Assert.Equal("claude-smoke", provider.Model);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

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

    [Fact]
    public void TraceValidator_PassesAnthropicDeferredLoading()
    {
        var events = ValidEvents(
            ModelProviderProtocols.Anthropic,
            "anthropic_tool_reference");

        var result = DeferredLoadingSmokeTraceValidator.Validate(
            events,
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DeferredToolLoadingObserved);
        Assert.Equal("anthropic_tool_reference", result.WireShape);
        Assert.Equal(DeferredLoadingSmokeTools.Echo, result.TargetToolName);
    }

    [Fact]
    public void TraceValidator_PassesOpenAIResponsesDeferredLoading()
    {
        var events = ValidEvents(
            ModelProviderProtocols.OpenAIResponses,
            "openai_responses_tool_search_output");

        var result = DeferredLoadingSmokeTraceValidator.Validate(
            events,
            ModelProviderProtocols.OpenAIResponses,
            DeferredLoadingSmokeTools.Echo);

        Assert.True(result.Success, result.Message);
        Assert.True(result.DeferredToolLoadingObserved);
        Assert.Equal("openai_responses_tool_search_output", result.WireShape);
    }

    [Fact]
    public void TraceValidator_RejectsMissingDeferredLoading()
    {
        var result = DeferredLoadingSmokeTraceValidator.Validate(
            [],
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);

        Assert.False(result.Success);
        Assert.Equal("deferred_loading_missing", result.Message);
    }

    [Fact]
    public void TraceValidator_RejectsWrongAnthropicWireShape()
    {
        var events = ValidEvents(
            ModelProviderProtocols.Anthropic,
            "openai_responses_tool_search_output");

        var result = DeferredLoadingSmokeTraceValidator.Validate(
            events,
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);

        Assert.False(result.Success);
        Assert.Equal("deferred_loading_wire_shape_mismatch", result.Message);
        Assert.Equal("openai_responses_tool_search_output", result.WireShape);
    }

    [Fact]
    public void TraceValidator_RejectsPromptCacheToolExtension()
    {
        var events = new[]
        {
            Event(
                nameof(TraceEventType.DeferredToolLoading),
                metadataJson: DeferredMetadata(ModelProviderProtocols.Anthropic, "anthropic_tool_reference"),
                promptCacheEventKind: PromptCacheEventKinds.ToolExtension,
                promptCacheChangedFields: [PromptCacheChangedFields.Tools]),
            Event(nameof(TraceEventType.ToolCallStarted), toolName: DeferredLoadingSmokeTools.Echo),
            Event(nameof(TraceEventType.ToolCallCompleted), toolName: DeferredLoadingSmokeTools.Echo),
            Event(nameof(TraceEventType.Response), content: DeferredLoadingSmokeTools.SuccessToken)
        };

        var result = DeferredLoadingSmokeTraceValidator.Validate(
            events,
            ModelProviderProtocols.Anthropic,
            DeferredLoadingSmokeTools.Echo);

        Assert.False(result.Success);
        Assert.Equal("deferred_loading_marked_prompt_cache_extension", result.Message);
        Assert.True(result.DeferredToolLoadingObserved);
    }

    [Fact]
    public void CliOptions_DefaultsToUserSmokeRunRootAndReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "deferred-loading-smoke-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"providers":[]}""");

            var ok = DeferredLoadingSmokeCliOptions.TryParse(
                ["--matrix", path],
                out var options,
                out var error);

            Assert.True(ok, error);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
                              ?? Environment.GetEnvironmentVariable("HOME")
                              ?? Directory.GetCurrentDirectory();

            var expectedPrefix = Path.Combine(
                userProfile,
                ".craft",
                "smoke-tests",
                "runs") + Path.DirectorySeparatorChar;
            Assert.StartsWith(
                expectedPrefix,
                options.WorkRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.Matches(@"^\d{8}-\d{6}-[0-9a-f]{8}$", Path.GetFileName(options.WorkRoot));
            Assert.Equal(Path.Combine(options.WorkRoot, "report.json"), options.ReportPath);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static DeferredLoadingSmokeTraceEvent[] ValidEvents(string protocol, string wireShape) =>
    [
        Event(nameof(TraceEventType.DeferredToolLoading), metadataJson: DeferredMetadata(protocol, wireShape)),
        Event(nameof(TraceEventType.ToolCallStarted), toolName: DeferredLoadingSmokeTools.Echo),
        Event(nameof(TraceEventType.ToolCallCompleted), toolName: DeferredLoadingSmokeTools.Echo),
        Event(nameof(TraceEventType.Response), content: DeferredLoadingSmokeTools.SuccessToken)
    ];

    private static string DeferredMetadata(string protocol, string wireShape) =>
        new JsonObject
        {
            ["strategy"] = "Native",
            ["effectiveMode"] = "Native",
            ["providerProtocol"] = protocol,
            ["trigger"] = "tool_search",
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
                    ["namespace"] = null
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
