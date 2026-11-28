using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpToolFilterTests
{
    private static readonly ToolPlanningContext Context = new("thread-1", null, "workspace", ".craft", "agent", null, null, 1);

    [Theory]
    [InlineData("workspace", null, null, "a,b,c")]
    [InlineData("workspace", "a,b", null, "a,b")]
    [InlineData("plugin", null, "b", "a,c")]
    [InlineData("workspace", "a,b", "b", "a")]
    [InlineData("workspace", "", null, "")]
    [InlineData("thread", "c", null, "c")]
    public async Task Filters_NarrowAgentToolsStatusAndInvocation(
        string origin,
        string? enabled,
        string? disabled,
        string expected)
    {
        await using var manager = new McpClientManager((_, _) => Task.FromResult(new McpConnectionResult(
            new FakeClient(), [new FakeFunction("a"), new FakeFunction("b"), new FakeFunction("c")])));
        var config = Server(origin);
        config.EnabledTools = Names(enabled);
        config.DisabledTools = Names(disabled);
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var expectedNames = Names(expected)!;

        Assert.Equal(expectedNames, await RegisteredToolsAsync(manager));
        Assert.Equal(expectedNames, manager.Tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Equal(expectedNames, (await manager.GetInventoryAsync("inventory"))!.Tools.Select(static tool => tool.Name));
        Assert.Equal(expectedNames.Count, Assert.Single(await manager.ListStatusesAsync()).ToolCount);
        foreach (var name in new[] { "a", "b", "c" })
            Assert.Equal(expectedNames.Contains(name), await manager.TryGetInvocationTargetAsync("inventory", name) is not null);
    }

    [Fact]
    public async Task ChangingFiltersOrModes_KeepsTheReadyConnection()
    {
        var connects = 0;
        await using var manager = new McpClientManager((_, _) =>
        {
            Interlocked.Increment(ref connects);
            return Task.FromResult(new McpConnectionResult(new FakeClient(), [new FakeFunction("a"), new FakeFunction("b")]));
        });
        await manager.ConnectAsync([Server("workspace")]);
        await manager.WaitForStartupCompletionAsync();
        var generation = await manager.GetGenerationAsync("inventory");

        var changed = Server("workspace");
        changed.DisabledTools = ["b"];
        changed.ToolApprovalMode = McpToolApprovalMode.Approve;
        await manager.ConnectAsync([changed]);

        Assert.Equal(1, connects);
        Assert.Equal(generation, await manager.GetGenerationAsync("inventory"));
        var registration = Assert.Single(await new McpToolSource(manager, new AppConfig()).GetRegistrationsAsync(Context));
        Assert.Equal("a", registration.Definition.Id.SourceToolId.Value);
        Assert.False(registration.Definition.PolicyHints.RequiresApproval);
    }

    [Fact]
    public async Task CallingAFilteredTool_FailsAsUnknown()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var manager = new McpClientManager();
        await manager.ConnectAsync(
        [
            new McpServerConfig
            {
                Name = "fixture",
                Command = "dotnet",
                Arguments = [McpFixtureProcess.AssemblyPath, "environment"],
                DisabledTools = ["environment"]
            }
        ], timeout.Token);
        await manager.WaitForStartupCompletionAsync(timeout.Token);
        var status = Assert.Single(await manager.ListStatusesAsync(timeout.Token));
        Assert.True(status.StartupState == "ready", status.LastError);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.CallToolAsync("fixture", "environment", cancellationToken: timeout.Token));
    }

    private static McpServerConfig Server(string origin) => new()
    {
        Name = "inventory",
        Command = "inventory-mcp-server",
        Origin = new McpServerOrigin { Kind = origin, DeclaredName = "inventory" }
    };

    private static List<string>? Names(string? csv) =>
        csv is null ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static async Task<IEnumerable<string>> RegisteredToolsAsync(McpClientManager manager) =>
        (await new McpToolSource(manager, new AppConfig()).GetRegistrationsAsync(Context))
        .Select(static registration => registration.Definition.Id.SourceToolId.Value)
        .Order(StringComparer.Ordinal);

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
