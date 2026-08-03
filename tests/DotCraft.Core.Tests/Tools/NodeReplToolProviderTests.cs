using DotCraft.Protocol.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Core.Tests.Tools;

public sealed class NodeReplToolProviderTests
{
    [Fact]
    public async Task Source_WithoutAvailableProxy_ReturnsNoRegistrations()
    {
        var fixture = CreateSource(new FakeNodeReplProxy(false));

        var registrations = await fixture.Source.GetRegistrationsAsync(fixture.Planning);

        Assert.Empty(registrations);
    }

    [Fact]
    public async Task Source_WithAvailableProxy_ReturnsQualifiedNodeReplDefinition()
    {
        var fixture = CreateSource(new FakeNodeReplProxy(true));

        var registration = Assert.Single(await fixture.Source.GetRegistrationsAsync(fixture.Planning));

        Assert.Equal(new ToolName("node_repl", "NodeReplJs"), registration.Definition.Name);
        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        Assert.Equal(PluginIds.Browser, registration.Definition.Id.SourceId);
        Assert.Equal("NodeReplJs", registration.Definition.Id.SourceToolId.Value);
    }

    [Fact]
    public async Task Source_WhenBrowserPluginDisabled_ReturnsNoRegistrations()
    {
        var fixture = CreateSource(new FakeNodeReplProxy(true));
        fixture.Config.Plugins.DisabledPlugins.Add(PluginIds.Browser);

        var registrations = await fixture.Source.GetRegistrationsAsync(fixture.Planning);

        Assert.Empty(registrations);
    }

    [Fact]
    public async Task Source_WithChromePluginAndBrowserDisabled_UsesChromeProvenance()
    {
        var fixture = CreateSource(new FakeNodeReplProxy(true), [PluginIds.Chrome]);
        fixture.Config.Plugins.DisabledPlugins.Add(PluginIds.Browser);

        var registration = Assert.Single(await fixture.Source.GetRegistrationsAsync(fixture.Planning));

        Assert.Equal(PluginIds.Chrome, registration.Definition.Id.SourceId);
    }

    [Fact]
    public async Task Dispatch_WhenProxyReturnsError_PreservesTurnMetadataAndStableFailure()
    {
        var proxy = new FakeNodeReplProxy(true, new NodeReplEvaluation
        {
            Error = "NodeReplJs timed out after 1000ms."
        });
        var fixture = CreateSource(proxy);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([fixture.Source], fixture.Planning);
        var definition = Assert.Single(snapshot.ModelVisibleDefinitions);

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            snapshot.ProviderFlatNames[definition.Name],
            new System.Text.Json.Nodes.JsonObject { ["code"] = "await agent.hang()" },
            new ToolInvocationRequest(
                "thread_test",
                "turn_001",
                "provider-call-node-1",
                ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.Error?.Code);
        Assert.Contains("Error: NodeReplJs timed out after 1000ms.", result.Content);
        Assert.NotNull(proxy.LastMetadata);
        Assert.Equal("thread_test", proxy.LastMetadata!.ThreadId);
        Assert.Equal("thread_test", proxy.LastMetadata.SessionId);
        Assert.Equal("turn_001", proxy.LastMetadata.TurnId);
        Assert.Equal(1, proxy.LastMetadata.ProtocolVersion);
    }

    private static SourceFixture CreateSource(
        INodeReplProxy proxy,
        IReadOnlyList<string>? pluginIds = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-node-repl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var botPath = Path.Combine(root, ".craft");
        var installedPluginIds = (pluginIds ?? [PluginIds.Browser]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var config = new AppConfig();
        return new SourceFixture(
            config,
            new NodeReplPluginToolSource(
                config,
                proxy,
                botPath,
                (_, pluginId) => installedPluginIds.Contains(pluginId)),
            new ToolPlanningContext(
                "thread_test",
                "turn_001",
                root,
                "default",
                null,
                [],
                1));
    }

    private sealed record SourceFixture(
        AppConfig Config,
        NodeReplPluginToolSource Source,
        ToolPlanningContext Planning);

    private sealed class FakeNodeReplProxy(
        bool available,
        NodeReplEvaluation? result = null) : INodeReplProxy
    {
        public bool IsAvailable => available;

        public NodeReplEvaluationMetadata? LastMetadata { get; private set; }

        public Task<NodeReplEvaluation?> EvaluateAsync(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default,
            NodeReplEvaluationMetadata? metadata = null)
        {
            LastMetadata = metadata;
            return Task.FromResult<NodeReplEvaluation?>(
                result ?? new NodeReplEvaluation { ResultText = "ok" });
        }
    }
}
