using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpMetaAndRuntimeRegistryTests
{
    [Fact]
    public void MetaBuilder_InjectsCallContextHookEventAndScopedPluginsWithoutLeakingWorkspacePath()
    {
        var context = new ToolInvocationContext(
            ThreadId: "thread-1",
            TurnId: "turn-2",
            CallId: "call-3",
            Audience: ToolInvocationAudience.Model,
            ToolName: new ToolName("NodeReplJs", "node_repl"),
            DefinitionId: new ToolDefinitionId(ToolSourceKind.Mcp, "node_repl", new SourceToolId("NodeReplJs")),
            RuntimeBindingId: new RuntimeBindingId("mcp:node_repl:NodeReplJs:1"),
            SnapshotRevision: 1,
            StartedAt: DateTimeOffset.UtcNow,
            WorkspacePath: "/tmp/workspace",
            IsSubagent: true,
            ParentThreadId: "parent-0",
            EnabledPlugins: ["browser", "secret-plugin", "computer"]);

        var customMeta = new JsonObject { ["custom/key"] = "keep-me" };
        var built = McpToolCallMetaBuilder.Build(
            context,
            additionalMeta: customMeta,
            hookEvent: "Stop",
            requiredPlugins: ["browser", "chrome", "computer"]);

        Assert.NotNull(built);
        Assert.Equal("keep-me", built!["custom/key"]?.GetValue<string>());
        Assert.Equal("Stop", built[McpToolCallMetaBuilder.HookEventKey]?.GetValue<string>());

        var callContext = built[McpToolCallMetaBuilder.CallContextKey] as JsonObject;
        Assert.NotNull(callContext);
        Assert.Equal(1, callContext!["protocolVersion"]?.GetValue<int>());
        Assert.Equal("thread-1", callContext["threadId"]?.GetValue<string>());
        Assert.Equal("turn-2", callContext["turnId"]?.GetValue<string>());
        Assert.Equal("thread-1", callContext["sessionId"]?.GetValue<string>());
        Assert.Equal("call-3", callContext["callId"]?.GetValue<string>());
        Assert.True(callContext["isSubagent"]?.GetValue<bool>());
        Assert.Equal("parent-0", callContext["parentThreadId"]?.GetValue<string>());
        Assert.DoesNotContain("workspacePath", callContext.Select(p => p.Key));

        var enabledPlugins = callContext["enabledPlugins"] as JsonArray;
        Assert.NotNull(enabledPlugins);
        Assert.Equal(["browser", "computer"], enabledPlugins!.Select(n => n!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void RuntimeMcpRegistry_ValidatesLoopbackGatesByRequiredPluginsAndCleansUpOwner()
    {
        var registry = new RuntimeMcpRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register("conn-1", new McpServerConfig
        {
            Name = "remote",
            Transport = "streamableHttp",
            Url = "http://192.168.1.10:8080/mcp"
        }));
        Assert.Throws<ArgumentException>(() => registry.Register("conn-1", new McpServerConfig
        {
            Name = "https-loopback",
            Transport = "streamableHttp",
            Url = "https://127.0.0.1:8080/mcp"
        }));

        var registered = registry.Register("conn-1", new McpServerConfig
        {
            Name = "node_repl",
            Transport = "streamableHttp",
            Url = "http://127.0.0.1:43210/mcp",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret-token" },
            RequiredPlugins = ["browser", "chrome", "computer"]
        });

        Assert.True(registered.Origin.IsHostRuntime);
        Assert.True(registered.HasSensitiveHeaders);
        Assert.True(registered.ReadOnly);

        Assert.Empty(registry.ListActive(new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var active = registry.ListActive(new HashSet<string>(["browser"], StringComparer.OrdinalIgnoreCase));
        Assert.Single(active, s => s.Name == "node_repl");

        Assert.True(registry.UnregisterOwner("conn-1"));
        Assert.Empty(registry.ListActive(new HashSet<string>(["browser"], StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Compose_AppendsHostRuntimeServersWithoutOverwritingWorkspaceServers()
    {
        var workspace = new McpServerConfig { Name = "shared", Enabled = true, Origin = McpServerOrigin.Workspace() };
        var hostConflict = new McpServerConfig { Name = "shared", Enabled = true, Origin = McpServerOrigin.HostRuntime("c1", "shared") };
        var hostRepl = new McpServerConfig { Name = "node_repl", Enabled = true, Origin = McpServerOrigin.HostRuntime("c1", "node_repl") };

        var effective = McpServerComposition.Compose(
            "thread-1",
            null,
            [workspace],
            [],
            [hostConflict, hostRepl]);

        Assert.Equal(2, effective.Count);
        Assert.Contains(effective, s => s.Name == "shared" && s.Origin.IsWorkspace);
        Assert.Contains(effective, s => s.Name == "node_repl" && s.Origin.IsHostRuntime);
    }
}
