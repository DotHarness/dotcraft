using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteExecutionPluginTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameThreadIdInTwoSessionsHasIndependentPluginState_AndClosingOneDrainsItsCall(bool loseConnection)
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1, "thread"));
        var snapshot = new EffectiveToolSnapshotBuilder().Build(registrations, 1);
        var tool = Assert.Single(registrations);
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var connection = await fixture.ConnectionAsync();
        await using var first = await fixture.Client.OpenSessionAsync("first", fixture.Server.PeerId, "repo", connection);
        await using var second = await fixture.OpenAsync("second");
        await first.PrepareAsync("thread", snapshot, "agent");
        await second.PrepareAsync("thread", snapshot, "plan");
        async Task<ToolExecutionResult> Invoke(RemoteExecutionSession session, string operation) =>
            await session.InvokeAsync(session.Route, tool.Definition, RemoteToolContractHasher.Compute(tool.Definition),
                fixture.Context(tool), new() { ["operation"] = operation });
        var started = await Invoke(first, "start");
        Assert.True(started.Success, started.Error?.Message);
        var aThread = JsonDocument.Parse(started.Content!).RootElement.GetProperty("thread").GetString();
        var untouched = await Invoke(second, "read");
        Assert.Contains("\"running\":false", untouched.Content);
        var bThread = JsonDocument.Parse(untouched.Content!).RootElement.GetProperty("thread").GetString();
        Assert.NotEqual(aThread, bThread);
        Assert.Contains("\"mode\":\"plan\"", untouched.Content);
        Assert.True((await Invoke(second, "start")).Success);
        var running = Invoke(first, "block");
        var log = Path.Combine(fixture.Workspace.Path, "plugin-lifecycle.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "entered:" + aThread);

        if (loseConnection)
        {
            await connection.DisposeAsync();
            await fixture.WaitAsync(() => PluginLogFile.ReadLines(log).Contains("release:" + aThread), TimeSpan.FromSeconds(10));
            Assert.False(first.IsAvailable);
        }
        else await first.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False((await running).Success); } catch (OperationCanceledException) { }
        var lines = PluginLogFile.ReadLines(log);
        Assert.Contains("cancelled:" + aThread, lines);
        Assert.Contains("release:" + aThread, lines);
        Assert.DoesNotContain("release:" + bThread, lines);
        Assert.DoesNotContain("dispose:first", lines);
        Assert.Contains("\"running\":true", (await Invoke(second, "read")).Content);
        await second.DisposeAsync();
        Assert.Contains("release:" + bThread, PluginLogFile.ReadLines(log));
        Assert.Contains("dispose:first", PluginLogFile.ReadLines(log));
    }
}
