using System.Text.Json;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpStdioEnvironmentTests
{
    [Fact]
    public async Task Launch_ExplicitValuesOverrideForwarding_WithoutUnnecessaryReconnect()
    {
        var upper = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
        var lower = upper.ToLowerInvariant();
        var missing = "DOTCRAFT_TEST_MCP_" + Guid.NewGuid().ToString("N");
        var absent = missing + "_ABSENT";
        var config = new McpServerConfig
        {
            Name = "environment",
            Command = "dotnet",
            Arguments = [McpFixtureProcess.AssemblyPath, "environment"],
            EnvironmentVariables = new() { [upper] = "explicit-value", [missing] = "explicit-missing-value" },
            EnvVars = [upper, lower, missing, absent],
            StartupTimeoutSec = 15
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var manager = new McpClientManager();
        await manager.ConnectAsync([config], timeout.Token);
        await manager.WaitForStartupCompletionAsync(timeout.Token);
        var status = Assert.Single(await manager.ListStatusesAsync(timeout.Token));
        Assert.True(status.StartupState == "ready", status.LastError);

        var result = await manager.CallToolAsync(config.Name, "environment",
            new Dictionary<string, object?> { ["names"] = new[] { upper, lower, missing, absent } },
            cancellationToken: timeout.Token);
        var values = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal("explicit-value", values.GetProperty(upper).GetString());
        Assert.Equal("explicit-missing-value", values.GetProperty(missing).GetString());
        Assert.Equal(JsonValueKind.Null, values.GetProperty(absent).ValueKind);
        Assert.Equal(OperatingSystem.IsWindows() ? "explicit-value" : Environment.GetEnvironmentVariable(lower),
            values.GetProperty(lower).GetString());

        var generation = await manager.GetGenerationAsync(config.Name, timeout.Token);
        config.EnvVars.Clear();
        await manager.ConnectAsync([config], timeout.Token);
        await manager.WaitForStartupCompletionAsync(timeout.Token);

        Assert.Equal(generation, await manager.GetGenerationAsync(config.Name, timeout.Token));
    }
}
