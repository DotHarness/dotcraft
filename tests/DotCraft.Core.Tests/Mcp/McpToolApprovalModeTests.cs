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

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(null, false, true)]
    public void WritesMode_RequiresApprovalUnlessReadOnly(bool? readOnly, bool? destructive, bool requiresApproval)
    {
        var hints = new ToolAnnotations { ReadOnlyHint = readOnly, DestructiveHint = destructive, OpenWorldHint = false };

        Assert.Equal(requiresApproval, McpLiveToolCatalogReader.RequiresApproval(McpToolApprovalMode.Writes, hints));
        Assert.True(McpLiveToolCatalogReader.RequiresApproval(McpToolApprovalMode.Writes, null));
    }

    [Theory]
    [InlineData("workspace", McpToolApprovalMode.Approve, "write", McpToolApprovalMode.Prompt, false, true)]
    [InlineData("plugin", McpToolApprovalMode.Prompt, "read", McpToolApprovalMode.Approve, false, true)]
    [InlineData("thread", McpToolApprovalMode.Approve, "read", McpToolApprovalMode.Approve, true, true)]
    public async Task ToolMode_OverridesServerModeExceptForThreadOrigin(
        string origin,
        McpToolApprovalMode serverMode,
        string tool,
        McpToolApprovalMode toolMode,
        bool readRequiresApproval,
        bool writeRequiresApproval)
    {
        await using var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(new FakeClient(), [new FakeFunction("read"), new FakeFunction("write")])));
        await manager.ConnectAsync(
        [
            new McpServerConfig
            {
                Name = "inventory",
                Command = "inventory-mcp-server",
                ToolApprovalMode = serverMode,
                Tools = new() { [tool] = new McpToolConfig { ApprovalMode = toolMode } },
                Origin = new McpServerOrigin { Kind = origin, DeclaredName = "inventory" }
            }
        ]);
        await manager.WaitForStartupCompletionAsync();

        var approvals = (await new McpToolSource(manager, new AppConfig()).GetRegistrationsAsync(Context))
            .ToDictionary(r => r.Definition.Id.SourceToolId.Value, r => r.Definition.PolicyHints.RequiresApproval);

        Assert.Equal(readRequiresApproval, approvals["read"]);
        Assert.Equal(writeRequiresApproval, approvals["write"]);
    }

    [Fact]
    public async Task NullToolEntry_InheritsServerMode()
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<McpServerConfig>(
            """{ "Name": "inventory", "Command": "inventory-mcp-server", "ToolApprovalMode": "prompt", "Tools": { "read": null } }""",
            AppConfig.SerializerOptions)!;
        await using var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(new FakeClient(), [new FakeFunction("read")])));
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();

        var registration = Assert.Single(await new McpToolSource(manager, new AppConfig()).GetRegistrationsAsync(Context));

        Assert.True(registration.Definition.PolicyHints.RequiresApproval);
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

    [Fact]
    public void HandWrittenWorkspaceServer_ReplacesSameNamedUserServerAtLoadAndReconnect()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp_scope_replace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var userPath = Path.Combine(root, "user.json");
            var workspacePath = Path.Combine(root, "workspace.json");
            File.WriteAllText(userPath, """
                { "McpServers": { "inventory": {
                    "Command": "inventory-mcp-server",
                    "ToolApprovalMode": "approve",
                    "Tools": { "delete_item": { "ApprovalMode": "approve" } },
                    "DisabledTools": ["export"] } } }
                """);
            File.WriteAllText(workspacePath, """
                { "McpServers": { "Inventory": {
                    "Command": "inventory-mcp-server",
                    "Tools": { "read_item": { "ApprovalMode": "prompt" } } } } }
                """);

            var loaded = Assert.Single(AppConfig.LoadWithGlobalFallback(workspacePath, userPath).McpServers);
            var reconnected = Assert.Single(McpScopeStore.Effective(workspacePath, userPath));

            foreach (var server in new[] { loaded, reconnected })
            {
                Assert.Equal(McpToolApprovalMode.Auto, server.ToolApprovalMode);
                Assert.Equal(["read_item"], server.Tools!.Keys);
                Assert.Null(server.DisabledTools);
            }
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
