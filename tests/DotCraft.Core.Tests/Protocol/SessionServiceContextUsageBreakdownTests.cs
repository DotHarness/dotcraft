using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceContextUsageBreakdownTests : SessionServiceRuntimeSignalTestBase
{
    [Fact]
    public async Task TryGetContextUsageBreakdown_SplitsTheSampledRequestIntoCategories()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "AGENTS.md"), new string('a', 20_000));
        var chatClient = UsageReportingChatClient();
        await using var factory = CreateAgentFactory(chatClient, [new BreakdownToolSource()], ConfigureBreakdown);
        var service = CreateService(factory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var breakdown = Assert.IsType<ContextUsageBreakdown>(service.TryGetContextUsageBreakdown(thread.Id));
        Assert.Equal(service.TryGetContextUsageSnapshot(thread.Id)!.Tokens, breakdown.ContextUsage.Tokens);
        Assert.Equal(
            breakdown.ContextUsage.Tokens,
            breakdown.Categories.Where(static category => !category.OnDemand).Sum(static category => category.Tokens));
        Assert.All(breakdown.Categories, static category => Assert.True(category.Tokens > 0));

        Assert.True(Loaded(breakdown, ContextUsageCategoryIds.SystemPrompt).Tokens > 0);
        var memoryFiles = Loaded(breakdown, ContextUsageCategoryIds.MemoryFiles);
        Assert.True(memoryFiles.Tokens >= 5_000);
        Assert.Contains(memoryFiles.Items!, static item => Path.GetFileName(item.Name) == "AGENTS.md");
        Assert.True(Loaded(breakdown, ContextUsageCategoryIds.Messages).Tokens > 0);
        Assert.Null(Loaded(breakdown, ContextUsageCategoryIds.BuiltInTools).Items);

        var mcp = Loaded(breakdown, ContextUsageCategoryIds.McpTools);
        var github = Assert.Single(mcp.Items!);
        Assert.Equal("github", github.Name);
        Assert.Equal(mcp.Tokens, github.Tokens);

        var onDemandMcp = OnDemand(breakdown, ContextUsageCategoryIds.McpTools);
        Assert.Equal("linear", Assert.Single(onDemandMcp.Items!).Name);
        Assert.True(OnDemand(breakdown, ContextUsageCategoryIds.BuiltInTools).Tokens > 0);
    }

    [Fact]
    public async Task TryGetContextUsageBreakdown_KeepsFixedPartsAfterManualCompaction()
    {
        var chatClient = UsageReportingChatClient();
        await using var factory = CreateAgentFactory(
            chatClient,
            [new BreakdownToolSource()],
            ConfigureBreakdown,
            compactionChatClient: new SummaryChatClient("<summary>older context summary</summary>"));
        var service = CreateService(factory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        for (var i = 0; i < 4; i++)
        {
            await DrainAsync(service.SubmitInputAsync(
                thread.Id,
                [new TextContent($"turn {i} " + new string('u', 1200))]));
        }

        var before = Assert.IsType<ContextUsageBreakdown>(service.TryGetContextUsageBreakdown(thread.Id));
        Assert.Equal("partial", (await service.CompactThreadAsync(thread.Id)).Outcome);

        var after = Assert.IsType<ContextUsageBreakdown>(service.TryGetContextUsageBreakdown(thread.Id));
        Assert.Equal(service.TryGetContextUsageSnapshot(thread.Id)!.Tokens, after.ContextUsage.Tokens);
        Assert.Equal(FixedParts(before), FixedParts(after));
    }

    private static IChatClient UsageReportingChatClient() => new FakeChatClient(
    [
        new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]),
        UsageUpdate(requestIndex: 1, input: 120_000, output: 10, cachedInput: 0)
    ]);

    private void ConfigureBreakdown(AppConfig config)
    {
        config.GlobalConfigPath = Path.Combine(_tempDir, "global", "config.json");
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Simulated;
        config.Compaction.ContextWindow = 200_000;
        config.Compaction.SummaryReserveTokens = 20_000;
        config.Compaction.AutoCompactBufferTokens = 13_000;
        config.Compaction.WarningBufferTokens = 20_000;
        config.Compaction.ErrorBufferTokens = 10_000;
        config.Compaction.KeepRecentMinTokens = 1;
        config.Compaction.KeepRecentMinGroups = 1;
        config.Compaction.KeepRecentMaxTokens = 1_000;
        config.Compaction.MicrocompactEnabled = false;
    }

    private static ContextUsageCategory Loaded(ContextUsageBreakdown breakdown, string id) =>
        Assert.Single(breakdown.Categories, category => category.Id == id && !category.OnDemand);

    private static ContextUsageCategory OnDemand(ContextUsageBreakdown breakdown, string id) =>
        Assert.Single(breakdown.Categories, category => category.Id == id && category.OnDemand);

    private static string[] FixedParts(ContextUsageBreakdown breakdown) =>
        breakdown.Categories
            .Where(static category => category.Id != ContextUsageCategoryIds.Messages)
            .Select(static category => $"{category.Id}:{category.OnDemand}:{category.Tokens}:"
                + string.Join(",", category.Items?.Select(static item => $"{item.Name}={item.Tokens}") ?? []))
            .ToArray();

    private sealed class BreakdownToolSource : IToolSource
    {
        public string SourceId => "context-usage-test";

        public int Priority => 10;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
            [
                Registration(ToolSourceKind.CoreNative, SourceId, null, "LocalLookup", deferred: false),
                Registration(ToolSourceKind.CoreNative, SourceId, "fixture", "DeferredLookup", deferred: true),
                Registration(ToolSourceKind.Mcp, "github", "mcp__github", "get_me", deferred: false),
                Registration(ToolSourceKind.Mcp, "github", "mcp__github", "list_repositories", deferred: false),
                Registration(ToolSourceKind.Mcp, "linear", "mcp__linear", "list_issues", deferred: true)
            ]);

        private static ToolRegistration Registration(
            ToolSourceKind kind,
            string sourceId,
            string? toolNamespace,
            string name,
            bool deferred)
        {
            var definitionId = new ToolDefinitionId(kind, sourceId, new SourceToolId(name));
            var definition = new ToolDefinition(
                definitionId,
                new ToolName(toolNamespace, name),
                $"Runs {name} against the fixture.",
                JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { query = new { type = "string", description = "What to look up." } }
                }),
                provenance: new ToolProvenance(kind, sourceId));
            var binding = new ToolRuntimeBinding(
                new RuntimeBindingId($"{sourceId}:{name}"),
                definitionId,
                new FixtureRuntime(sourceId),
                ToolBindingLeases.AlwaysAvailable,
                $"{sourceId}:{name}",
                1);
            return new ToolRegistration(
                definition,
                binding,
                kind == ToolSourceKind.Mcp ? ToolProjectionShape.McpLifecycle : ToolProjectionShape.StandardPair,
                deferred ? ToolExposure.Deferred : ToolExposure.Direct,
                ToolInvocationAudience.Model,
                deferred ? new DeferredToolDescriptor(toolNamespace!, $"{name} fixture") : null);
        }
    }

    private sealed class FixtureRuntime(string serverName) : IToolRuntime, IMcpToolRuntimeMetadata
    {
        public string ServerName => serverName;

        public long Generation => 1;

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }
}
