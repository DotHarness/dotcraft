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

        Assert.Contains(status.StartupState, new[] { "starting", "error" });
    }

    [Fact]
    public async Task WaitForStartupCompletionAsync_HangingServer_DoesNotBlockIndefinitely()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([HangingStdio("hung-server")]);
        var elapsed = Stopwatch.StartNew();
        await manager.WaitForStartupCompletionAsync();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Wait took {elapsed.Elapsed}.");
        var status = Assert.Single(await manager.ListStatusesAsync());
        Assert.Contains(status.StartupState, new[] { "starting", "error" });
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

}
