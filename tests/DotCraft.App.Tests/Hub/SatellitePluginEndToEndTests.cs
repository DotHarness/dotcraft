using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Hub;
using DotCraft.Plugins;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tests.Tools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatellitePluginEndToEndTests
{
    [Fact]
    public async Task Bridge_SynchronizesAcceptedPluginAndEnforcesOwnerPolicy_ThenReleasesExecution()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        harness.CreatePluginConfigStore().Mutate(
            PluginManifestParser.Load(harness.PluginRoot("probe")).Manifest!,
            "personal", [new PluginConfigMutation("set", "label", JsonSerializer.SerializeToElement("agent-setting"))]);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        await File.WriteAllTextAsync(Path.Combine(harness.PluginRoot("probe"), "resource.txt"), "changed-after-acceptance");

        await using var scenario = await SatelliteScenario.StartAsync(Path.Combine(harness.Root, "satellite"));
        await using var client = new RemoteToolHostClient(scenario.Directory);
        var planning = new ToolPlanningContext("plugin-thread", null, harness.Workspace,
            Path.Combine(harness.Workspace, ".craft"), "agent", null, [], 1);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(planning);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(RemoteToolRegistrationRouter.Wrap(registrations, client), 1);
        client.UpdateRemoteToolSnapshot("plugin-thread", snapshot, "agent");
        var log = Path.Combine(scenario.WorkspacePath, "plugin-lifecycle.log");
        Assert.False(File.Exists(log));

        var connected = await client.ConnectAsync("plugin-thread", scenario.PeerId, scenario.WorkspaceId);

        Assert.Contains("Probe.Run", connected.MatchedTools);
        var result = await InvokeAsync(snapshot, "start");
        Assert.True(result.Success, result.Error?.Message);
        using var document = JsonDocument.Parse(result.Content!);
        var data = document.RootElement;
        Assert.Equal(scenario.WorkspacePath, data.GetProperty("workspace").GetString());
        Assert.Equal("bundle-resource", data.GetProperty("resource").GetString());
        Assert.Equal("agent-setting", data.GetProperty("settings").GetProperty("label").GetString());
        var remoteThread = data.GetProperty("thread").GetString();
        Assert.False(string.IsNullOrWhiteSpace(remoteThread));
        Assert.Equal("agent", data.GetProperty("mode").GetString());
        Assert.True(data.GetProperty("running").GetBoolean());

        scenario.DenyTool("Probe.Run");
        var denied = await InvokeAsync(snapshot, "cancel");
        Assert.False(denied.Success);
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, denied.Error?.Code);

        Assert.True((await client.DisconnectAsync("plugin-thread")).Disconnected);
        await PluginRuntimeHarness.WaitForLineAsync(log, "release:" + remoteThread);
        await PluginRuntimeHarness.WaitForLineAsync(log, "dispose:first");
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(async () =>
        {
            var peer = Assert.Single(await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites"));
            return peer.Online && peer.Workspaces.All(workspace => !workspace.Busy);
        });
        Assert.Equal(RemoteToolHostStatus.Standby, scenario.Runtime.Status);
    }

    private static ValueTask<ToolExecutionResult> InvokeAsync(EffectiveToolSnapshot snapshot, string operation) =>
        new ToolDispatcher().DispatchAsync(snapshot, new("Probe", "Run"),
            new JsonObject { ["operation"] = operation, ["target"] = "remote" },
            new("plugin-thread", "turn", Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model));
}
