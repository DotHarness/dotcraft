using System.Reflection;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using DeferredToolRegistry = DotCraft.Tools.DeferredToolActivationIndex;

namespace DotCraft.Tests.Tools;

public sealed class DeferredToolLoadingPlannerTests
{
    [Fact]
    public void Apply_DefaultAutoUsesSimulatedForChatCompletions()
    {
        var config = new AppConfig();
        var context = CreateContext(config, ModelProviderProtocols.OpenAIChatCompletions);
        var tools = new List<AITool> { new MetadataFunction("DeferredRuntimeTool", deferLoading: true) };

        DeferredToolLoadingPlanner.Apply(tools, context);

        Assert.Contains(tools, tool => tool.Name == NativeToolSearchTool.ToolName);
        Assert.NotNull(context.DeferredToolActivationIndex);
        Assert.Equal("Simulated", context.DeferredToolActivationIndex!.Mode.ToString());
    }

    [Fact]
    public void Apply_DefersRuntimeToolsOnlyWhenMetadataRequestsIt()
    {
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Simulated;
        var context = CreateContext(config, ModelProviderProtocols.OpenAIChatCompletions);
        var deferred = new MetadataFunction("DeferredRuntimeTool", deferLoading: true);
        var immediate = new MetadataFunction("ImmediateRuntimeTool", deferLoading: false);
        var tools = new List<AITool> { deferred, immediate };

        DeferredToolLoadingPlanner.Apply(tools, context);

        Assert.DoesNotContain(tools, tool => tool.Name == "DeferredRuntimeTool");
        Assert.Contains(tools, tool => tool.Name == "ImmediateRuntimeTool");
        Assert.Contains(tools, tool => tool.Name == NativeToolSearchTool.ToolName);
        Assert.NotNull(context.DeferredToolActivationIndex);
        Assert.Contains("tests__DeferredRuntimeTool", context.DeferredToolActivationIndex!.DeferredTools.Keys);
    }

    [Fact]
    public void Apply_NativeAddsNativeToolSearchMarker()
    {
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Native;
        var context = CreateContext(config, ModelProviderProtocols.OpenAIResponses);
        var tools = new List<AITool> { new MetadataFunction("DeferredRuntimeTool", deferLoading: true) };

        DeferredToolLoadingPlanner.Apply(tools, context);

        var marker = Assert.Single(tools);
        Assert.Equal(NativeToolSearchTool.ToolName, marker.Name);
        Assert.Equal("Native", context.DeferredToolActivationIndex!.Mode.ToString());
    }

    [Fact]
    public void Apply_AnthropicNativeAddsAnthropicToolSearchMarker()
    {
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Native;
        var context = CreateContext(config, ModelProviderProtocols.Anthropic);
        var tools = new List<AITool> { new MetadataFunction("DeferredRuntimeTool", deferLoading: true) };

        DeferredToolLoadingPlanner.Apply(tools, context);

        var marker = Assert.IsType<AnthropicToolSearchTool>(Assert.Single(tools));
        Assert.Equal(AnthropicToolSearchTool.ToolName, marker.Name);
        Assert.Equal("Native", context.DeferredToolActivationIndex!.Mode.ToString());
    }

    [Fact]
    public void Apply_AnthropicExplicitSimulatedAddsLegacySearchTool()
    {
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Simulated;
        var context = CreateContext(config, ModelProviderProtocols.Anthropic);
        var tools = new List<AITool> { new MetadataFunction("DeferredRuntimeTool", deferLoading: true) };

        DeferredToolLoadingPlanner.Apply(tools, context);

        Assert.Contains(tools, tool => tool.Name == NativeToolSearchTool.ToolName);
        Assert.DoesNotContain(tools, tool => tool is AnthropicToolSearchTool);
        Assert.Equal("Simulated", context.DeferredToolActivationIndex!.Mode.ToString());
    }

    [Fact]
    public void Registry_Bm25SearchUsesSchemaText()
    {
        var tool = new MetadataFunction(
            "TicketLookup",
            deferLoading: true,
            schema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    jiraProject = new { type = "string" }
                }
            }));
        var registry = new DeferredToolRegistry([new DeferredToolEntry(tool, "issue-tracker")]);

        var results = registry.SearchAndActivate("jira");

        Assert.Equal("TicketLookup", Assert.Single(results).Name);
    }

    [Fact]
    public void Registry_SameLocalNameInDifferentNamespaces_RemainsIndependentlyAddressable()
    {
        var alpha = new MetadataFunction("Lookup", deferLoading: true);
        var beta = new MetadataFunction("Lookup", deferLoading: true);
        var registry = new DeferredToolRegistry([
            new DeferredToolEntry(alpha, "alpha-source", "alpha"),
            new DeferredToolEntry(beta, "beta-source", "beta")
        ]);

        Assert.Equal(2, registry.DeferredTools.Count);
        Assert.Contains("alpha__Lookup", registry.DeferredTools.Keys);
        Assert.Contains("beta__Lookup", registry.DeferredTools.Keys);

        var activated = registry.SearchAndActivate("Lookup", maxResults: 2);
        Assert.Equal(["alpha__Lookup", "beta__Lookup"],
            activated.Select(static result => result.Name).Order(StringComparer.Ordinal).ToArray());
    }

    private static AgentRuntimeContext CreateContext(AppConfig config, string protocol)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-deferred-tools-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var botPath = Path.Combine(root, ".craft");
        Directory.CreateDirectory(botPath);
        return new AgentRuntimeContext
        {
            Config = config,
            ChatClient = null!,
            EffectiveProviderProtocol = protocol,
            WorkspacePath = root,
            BotPath = botPath,
            MemoryStore = new MemoryStore(botPath),
            SkillsLoader = new SkillsLoader(botPath),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([])
        };
    }

    private sealed class MetadataFunction : AIFunction, IDeferredToolMetadata
    {
        private readonly JsonElement _schema;

        public MetadataFunction(string name, bool deferLoading, JsonElement? schema = null)
        {
            Name = name;
            DeferLoading = deferLoading;
            _schema = schema ?? JsonSerializer.SerializeToElement(new { type = "object" });
        }

        public override string Name { get; }

        public override string Description => "Runtime metadata test function.";

        public override JsonElement JsonSchema => _schema;

        public override JsonElement? ReturnJsonSchema => null;

        public override MethodInfo? UnderlyingMethod => null;

        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Default;

        public bool DeferLoading { get; }

        public string? DeferredToolSource => "runtime";

        public string? DeferredToolNamespace => "tests";

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>("ok");
    }
}
