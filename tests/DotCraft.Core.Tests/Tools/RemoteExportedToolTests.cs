using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteExportedToolTests
{
    [Fact]
    public async Task ExportedNativeDefinitionsCanExecuteWithoutLocalRegistrations()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var session = await fixture.OpenAsync("embedded");
        var tools = await session.ListExportedToolsAsync("thread");
        var write = tools.Single(tool => tool.Name == "WriteFile");
        var read = tools.Single(tool => tool.Name == "ReadFile");

        var written = await session.InvokeAsync(write, "thread",
            new() { ["path"] = "exported.txt", ["content"] = "remote content" }, "turn");
        var result = await session.InvokeAsync(read, "thread", new() { ["path"] = "exported.txt" }, "turn");

        Assert.True(written.Success, written.Error?.Message);
        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("remote content", result.Content);
    }

    [Fact]
    public async Task CapturedDefinitionCannotBeRedirectedAcrossThreadsOrSessions()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var first = await fixture.OpenAsync("first");
        await using var second = await fixture.OpenAsync("second");
        var tool = (await first.ListExportedToolsAsync("thread")).Single(tool => tool.Name == "WriteFile");
        var arguments = new JsonObject { ["path"] = "not-written.txt", ["content"] = "wrong owner" };

        var otherThread = await first.InvokeAsync(tool, "other-thread", arguments);
        var sibling = await second.InvokeAsync(tool, "thread", arguments);
        await first.DisposeAsync();
        var expired = await first.InvokeAsync(tool, "thread", arguments);

        Assert.Equal(RemoteToolErrorCodes.RemoteToolUnavailable, otherThread.Error?.Code);
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, sibling.Error?.Code);
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, expired.Error?.Code);
        Assert.False(File.Exists(Path.Combine(fixture.Workspace.Path, "not-written.txt")));
        var current = (await second.ListExportedToolsAsync("thread")).Single(item => item.Name == "WriteFile");
        Assert.True((await second.InvokeAsync(current, "thread", arguments)).Success);
    }

    [Fact]
    public async Task ExportedPreparedDefinitionKeepsItsBindingAndExpiresWhenReleased()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1, "thread"));
        var snapshot = new EffectiveToolSnapshotBuilder().Build(registrations, 1);
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var session = await fixture.OpenAsync("prepared");
        await session.PrepareAsync("thread", snapshot, "plan");
        var tool = (await session.ListExportedToolsAsync("thread")).Single(item => item.Name == "Probe.Run");

        var read = await session.InvokeAsync(tool, "thread", new() { ["operation"] = "read" });
        Assert.True(read.Success, read.Error?.Message);
        Assert.Contains("\"mode\":\"plan\"", read.Content);

        await session.ReleaseThreadAsync("thread");
        var expired = await session.InvokeAsync(tool, "thread", new() { ["operation"] = "read" });
        Assert.False(expired.Success);
    }
}
