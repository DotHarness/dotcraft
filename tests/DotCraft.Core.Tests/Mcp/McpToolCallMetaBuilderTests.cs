using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Mcp;

public sealed class McpToolCallMetaBuilderTests
{
    [Fact]
    public void Build_InjectsCallContextAndHookEventWithoutLeakingWorkspacePath()
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
            ParentThreadId: "parent-0");

        var customMeta = new JsonObject { ["custom/key"] = "keep-me" };
        var built = McpToolCallMetaBuilder.Build(context, additionalMeta: customMeta, hookEvent: "Stop");

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
    }
}
