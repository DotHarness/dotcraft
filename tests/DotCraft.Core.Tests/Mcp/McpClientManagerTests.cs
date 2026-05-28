using DotCraft.Mcp;
using System.Diagnostics;

namespace DotCraft.Tests.Mcp;

public sealed class McpClientManagerTests
{
    private static McpServerConfig DisabledStdio(string name) =>
        new()
        {
            Name = name,
            Enabled = false,
            Transport = "stdio",
            Command = "mock-mcp-cli",
            Arguments = ["serve", "--stdio"]
        };

    private static McpServerConfig HangingStdio(string name, double startupTimeoutSec = 0.2)
    {
        if (OperatingSystem.IsWindows())
        {
            return new McpServerConfig
            {
                Name = name,
                Enabled = true,
                Transport = "stdio",
                Command = "cmd.exe",
                Arguments = ["/c", "ping -n 60 127.0.0.1 > nul"],
                StartupTimeoutSec = startupTimeoutSec
            };
        }

        return new McpServerConfig
        {
            Name = name,
            Enabled = true,
            Transport = "stdio",
            Command = "/bin/sh",
            Arguments = ["-c", "sleep 60"],
            StartupTimeoutSec = startupTimeoutSec
        };
    }

    [Fact]
    public async Task ConnectAsync_DisabledServers_LeavesToolIndexesEmpty()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([DisabledStdio("demo-server")]);

        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);

        var statuses = await manager.ListStatusesAsync();
        var status = Assert.Single(statuses);
        Assert.Equal("disabled", status.StartupState);
        Assert.Equal(0, status.ToolCount);
    }

    [Fact]
    public async Task UpsertAndRemove_DisabledServer_KeepsToolIndexesEmpty()
    {
        await using var manager = new McpClientManager();
        await manager.ConnectAsync([]);

        var upserted = await manager.UpsertAsync(DisabledStdio("demo-server"));
        Assert.Equal("disabled", upserted.StartupState);
        Assert.Equal(0, upserted.ToolCount);
        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);

        var removed = await manager.RemoveAsync("demo-server");
        Assert.True(removed);
        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);
    }

    [Fact]
    public async Task ConnectAsync_HangingServer_ReturnsQuickly_AndStatusRequestsDoNotBlock()
    {
        await using var manager = new McpClientManager();
        var elapsed = Stopwatch.StartNew();

        await manager.ConnectAsync([HangingStdio("hung-server")]);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"ConnectAsync took {elapsed.Elapsed}.");

        using var listCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var statuses = await manager.ListStatusesAsync(listCts.Token);
        var status = Assert.Single(statuses);
        Assert.Equal("hung-server", status.Name);
        Assert.Contains(status.StartupState, new[] { "starting", "error" });

        status = await WaitForStatusAsync(
            manager,
            "hung-server",
            candidate => candidate.StartupState == "error",
            TimeSpan.FromSeconds(5));
        Assert.Contains("startup timed out", status.LastError);
    }

    [Fact]
    public async Task WaitForStartupCompletionAsync_HangingServer_ReturnsAfterTimeout()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([HangingStdio("hung-server")]);
        await manager.WaitForStartupCompletionAsync();

        var status = Assert.Single(await manager.ListStatusesAsync());
        Assert.Equal("error", status.StartupState);
        Assert.Contains("startup timed out", status.LastError);
    }

    [Fact]
    public async Task WaitForStartupCompletionAsync_DisabledServer_ReturnsWithoutStarting()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([DisabledStdio("disabled-server")]);
        await manager.WaitForStartupCompletionAsync();

        var status = Assert.Single(await manager.ListStatusesAsync());
        Assert.Equal("disabled", status.StartupState);
    }

    [Fact]
    public async Task ConnectAsync_StaleBackgroundResult_DoesNotOverrideNewGeneration()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([HangingStdio("same-server")]);
        await manager.ConnectAsync([DisabledStdio("same-server")]);

        await Task.Delay(TimeSpan.FromSeconds(1));

        var status = Assert.Single(await manager.ListStatusesAsync());
        Assert.Equal("same-server", status.Name);
        Assert.Equal("disabled", status.StartupState);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Source_DoesNotUseSyncOverAsync_ForToolRebuild()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "DotCraft.Core", "Mcp", "McpClientManager.cs");
        sourcePath = Path.GetFullPath(sourcePath);

        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain(".GetAwaiter().GetResult()", source, StringComparison.Ordinal);

        var rebuildStart = source.IndexOf("private void RebuildToolIndexUnsafe()", StringComparison.Ordinal);
        Assert.True(rebuildStart >= 0, "Could not locate RebuildToolIndexUnsafe.");
        var rebuildBody = source[rebuildStart..];
        Assert.DoesNotContain("ListToolsAsync", rebuildBody, StringComparison.Ordinal);
        Assert.DoesNotContain("public IReadOnlyList<McpClientTool> Tools => _tools;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public IReadOnlyDictionary<string, string> ToolServerMap => _toolServerMap;", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _toolsSnapshot)", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _toolServerMapSnapshot)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_DoesNotGateMcpContextOnCurrentToolCount()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src"));
        var sourceFiles = new[]
        {
            Path.Combine(sourceRoot, "DotCraft.Core", "Hosting", "WorkspaceRuntime.cs"),
            Path.Combine(sourceRoot, "DotCraft.Core", "Protocol", "SessionService.cs"),
            Path.Combine(sourceRoot, "DotCraft.App", "Gateway", "GatewayHost.cs")
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("McpClientManager.Tools.Count > 0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mcpClientManager.Tools.Count > 0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddRange(mcpManager.Tools)", source, StringComparison.Ordinal);
        }
    }

    private static async Task<McpServerStatusSnapshot> WaitForStatusAsync(
        McpClientManager manager,
        string name,
        Func<McpServerStatusSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = (await manager.ListStatusesAsync())
                .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (status != null && predicate(status))
                return status;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for MCP server status '{name}'.");
    }
}
