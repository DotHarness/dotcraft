using DotCraft.Configuration;
using DotCraft.Mcp;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class AppConfigLayerMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-layer-merge-" + Guid.NewGuid().ToString("N"));

    public AppConfigLayerMergeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LoadWithGlobalFallback_WorkspaceNullReplacesUserValueOfSameMcpServer()
    {
        var userPath = Path.Combine(_root, "user.json");
        var workspacePath = Path.Combine(_root, "workspace.json");
        McpScopeStore.Upsert(userPath, new McpServerConfig { Name = "inventory", Command = "inventory-mcp-server", Cwd = "user-dir" });
        McpScopeStore.Upsert(workspacePath, new McpServerConfig { Name = "inventory", Command = "inventory-mcp-server" });

        var server = Assert.Single(AppConfig.LoadWithGlobalFallback(workspacePath, userPath).McpServers);

        Assert.Null(server.Cwd);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
