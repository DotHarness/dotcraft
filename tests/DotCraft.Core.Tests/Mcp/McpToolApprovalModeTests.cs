using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpToolApprovalModeTests
{
    private static readonly ToolPlanningContext Context = new("thread-1", null, "workspace", ".craft", "agent", null, null, 1);

    [Theory]
    [InlineData("workspace", McpToolApprovalMode.Auto, true)]
    [InlineData("workspace", McpToolApprovalMode.Approve, false)]
    [InlineData("plugin", McpToolApprovalMode.Approve, false)]
    [InlineData("thread", McpToolApprovalMode.Approve, true)]
    public async Task ServerMode_DecidesApprovalForUnannotatedTools(string origin, McpToolApprovalMode mode, bool requiresApproval)
    {
        await using var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(new FakeClient(), [new FakeFunction("files")])));
        await manager.ConnectAsync(
        [
            new McpServerConfig
            {
                Name = "inventory",
                Command = "inventory-mcp-server",
                ToolApprovalMode = mode,
                Origin = new McpServerOrigin { Kind = origin, DeclaredName = "inventory" }
            }
        ]);
        await manager.WaitForStartupCompletionAsync();

        var registration = Assert.Single(await new McpToolSource(manager, new AppConfig()).GetRegistrationsAsync(Context));

        Assert.Equal(requiresApproval, registration.Definition.PolicyHints.RequiresApproval);
    }

    [Theory]
    [InlineData(true, null, null, false)]
    [InlineData(true, true, null, true)]
    [InlineData(null, false, false, false)]
    [InlineData(null, false, null, true)]
    [InlineData(false, null, false, true)]
    public void AutoMode_FollowsStandardAnnotations(bool? readOnly, bool? destructive, bool? openWorld, bool requiresApproval)
    {
        var hints = new ToolAnnotations { ReadOnlyHint = readOnly, DestructiveHint = destructive, OpenWorldHint = openWorld };

        Assert.Equal(requiresApproval, McpLiveToolCatalogReader.RequiresApproval(McpToolApprovalMode.Auto, hints));
        Assert.False(McpLiveToolCatalogReader.RequiresApproval(McpToolApprovalMode.Approve, hints));
        Assert.True(McpLiveToolCatalogReader.RequiresApproval(McpToolApprovalMode.Prompt, hints));
    }

    [Fact]
    public void WorkspaceAuto_OverridesUserApproveAfterMergedLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp_approval_merge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var userPath = Path.Combine(root, "user.json");
            var workspacePath = Path.Combine(root, "workspace.json");
            File.WriteAllText(userPath, """{ "McpServers": { "inventory": { "Command": "inventory-mcp-server", "ToolApprovalMode": "approve" } } }""");
            McpScopeStore.Upsert(workspacePath, new McpServerConfig { Name = "inventory", Command = "inventory-mcp-server" });

            var server = Assert.Single(AppConfig.LoadWithGlobalFallback(workspacePath, userPath).McpServers);

            Assert.Equal(McpToolApprovalMode.Auto, server.ToolApprovalMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeClient : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFunction(string name) : AIFunction
    {
        public override string Name { get; } = name;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>(null);
    }
}
