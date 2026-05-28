using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Tools;

public sealed class NodeReplToolProviderTests
{
    private sealed class FakeNodeReplProxy(bool available, NodeReplEvaluateResult? result = null) : INodeReplProxy
    {
        public bool IsAvailable => available;

        public Task<NodeReplEvaluateResult?> EvaluateAsync(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default,
            NodeReplEvaluationMetadata? metadata = null)
        {
            LastMetadata = metadata;
            return Task.FromResult<NodeReplEvaluateResult?>(result ?? new NodeReplEvaluateResult { ResultText = "ok" });
        }

        public NodeReplEvaluationMetadata? LastMetadata { get; private set; }

    }

    [Fact]
    public void CreateTools_WithoutAvailableProxy_ReturnsNoTools()
    {
        var provider = new NodeReplPluginFunctionProvider();
        var tools = provider.CreateTools(CreateContext(new FakeNodeReplProxy(false))).ToList();
        Assert.Empty(tools);
    }

    [Fact]
    public void CreateTools_WithAvailableProxy_ReturnsNodeReplTools()
    {
        var provider = new NodeReplPluginFunctionProvider();
        var tools = provider.CreateTools(CreateContext(new FakeNodeReplProxy(true))).ToList();
        Assert.Contains(tools, t => t.Name == "NodeReplJs");
        Assert.Single(tools);
    }

    [Fact]
    public void CreateTools_WhenPluginDisabled_ReturnsNoTools()
    {
        var provider = new NodeReplPluginFunctionProvider();
        var context = CreateContext(new FakeNodeReplProxy(true));
        context.Config.Plugins.DisabledPlugins.Add(NodeReplPluginFunctionProvider.PluginId);

        var tools = provider.CreateTools(context).ToList();

        Assert.Empty(tools);
    }

    [Fact]
    public void CreateTools_WithChromePluginInstalledAndBrowserDisabled_ReturnsNodeReplTool()
    {
        var provider = new NodeReplPluginFunctionProvider();
        var context = CreateContext(new FakeNodeReplProxy(true), [PluginIds.Chrome]);
        context.Config.Plugins.DisabledPlugins.Add(NodeReplPluginFunctionProvider.PluginId);

        var tools = provider.CreateTools(context).ToList();

        Assert.Contains(tools, t => t.Name == "NodeReplJs");
        Assert.Single(tools);
    }

    [Fact]
    public async Task NodeReplJs_WhenProxyReturnsError_ReturnsToolResultText()
    {
        var provider = new NodeReplPluginFunctionProvider();
        var proxy = new FakeNodeReplProxy(true, new NodeReplEvaluateResult
        {
            Error = "NodeReplJs timed out after 1000ms."
        });
        var context = CreateContext(proxy);
        var turn = new SessionTurn { Id = "turn_001", ThreadId = "thread_test" };
        var completed = new List<SessionItem>();
        var tool = Assert.IsAssignableFrom<AIFunction>(
            provider.CreateTools(context).Single(t => t.Name == "NodeReplJs"));

        using var scope = PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = "thread_test",
            TurnId = "turn_001",
            OriginChannel = "desktop",
            WorkspacePath = context.WorkspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = context.ApprovalService,
            PathBlacklist = context.PathBlacklist,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = completed.Add
        });
        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["code"] = "await agent.hang()"
        }));

        var contents = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result);
        var text = Assert.Single(contents.OfType<TextContent>());
        Assert.Contains("Error: NodeReplJs timed out after 1000ms.", text.Text);
        var item = Assert.Single(completed);
        Assert.Equal(ItemType.PluginFunctionCall, item.Type);
        Assert.NotNull(proxy.LastMetadata);
        Assert.Equal("thread_test", proxy.LastMetadata!.ThreadId);
        Assert.Equal("thread_test", proxy.LastMetadata.SessionId);
        Assert.Equal("turn_001", proxy.LastMetadata.TurnId);
        Assert.Equal(1, proxy.LastMetadata.ProtocolVersion);
    }

    private static ToolProviderContext CreateContext(INodeReplProxy proxy, IReadOnlyList<string>? pluginIds = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-node-repl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var botPath = Path.Combine(root, ".craft");
        var deployer = new BuiltInPluginDeployer(Path.Combine(botPath, "plugins"), [BundledPluginSourceRoot()]);
        foreach (var pluginId in pluginIds ?? [NodeReplPluginFunctionProvider.PluginId])
            deployer.DeployPlugin(pluginId);
        return new ToolProviderContext
        {
            Config = new AppConfig(),
            ChatClient = null!,
            WorkspacePath = root,
            BotPath = botPath,
            MemoryStore = new MemoryStore(botPath),
            SkillsLoader = new SkillsLoader(botPath),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([]),
            NodeReplProxy = proxy
        };
    }

    private static string BundledPluginSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "dotcraft.sln")))
                return Path.Combine(dir, "desktop", "resources", "plugins", "dotcraft-bundled", "plugins");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
