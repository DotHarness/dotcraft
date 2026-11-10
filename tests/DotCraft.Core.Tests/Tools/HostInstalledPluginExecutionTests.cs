using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class HostInstalledPluginExecutionTests
{
    [Fact]
    public async Task UnsetDirectory_DoesNotLoadWorkspacePlugins()
    {
        using var plugins = new PluginRuntimeHarness();
        RemotePluginFixture.Write(plugins);
        using var home = new TemporaryDirectory();
        var storage = Setup(home.Path, plugins.Workspace);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = new RemoteExecutionClient();
        await using var session = await OpenAsync(client, server, "session");

        Assert.Empty(session.HostPlugins);
        Assert.DoesNotContain(await session.ListExportedToolsAsync("thread"), tool => tool.Name == "Probe.Run");
        Assert.False(File.Exists(Path.Combine(plugins.Workspace, "plugin-lifecycle.log")));
    }

    [Fact]
    public async Task InstalledTools_WorkWithoutPreparedDefinitions_AndKeepSessionStateIndependent()
    {
        using var plugins = new PluginRuntimeHarness();
        RemotePluginFixture.Write(plugins, "installed");
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path),
            hostPluginsDirectory: Path.GetDirectoryName(plugins.PluginRoot("probe")));
        await using var client = new RemoteExecutionClient();
        await using var first = await OpenAsync(client, server, "first");
        await using var second = await OpenAsync(client, server, "second");
        var firstTool = Assert.Single(await first.ListExportedToolsAsync("thread"), tool => tool.Name == "Probe.Run");
        var secondTool = Assert.Single(await second.ListExportedToolsAsync("thread"), tool => tool.Name == "Probe.Run");
        var installed = Assert.Single(first.HostPlugins, plugin => plugin.Id == "probe");
        Assert.Equal("1.0.0", installed.Version);
        Assert.Equal(plugins.PluginRoot("probe"), installed.RootPath);
        var read = Assert.Single(await first.ListExportedToolsAsync("thread"), tool => tool.Name == "ReadFile");
        var resource = await first.InvokeAsync(read, "thread", new JsonObject { ["path"] = Path.Combine(installed.RootPath, "resource.txt") });
        Assert.True(resource.Success, resource.Error?.Message);
        Assert.Contains("bundle-resource", resource.Content);

        var started = await InvokeAsync(first, firstTool, "start");
        var firstThread = started.GetProperty("thread").GetString()!;
        Assert.True(started.GetProperty("running").GetBoolean());
        Assert.False((await InvokeAsync(second, secondTool, "read")).GetProperty("running").GetBoolean());
        await InvokeAsync(second, secondTool, "start");
        var pending = first.InvokeAsync(firstTool, "thread", new JsonObject { ["operation"] = "block" }).AsTask();
        var log = Path.Combine(workspace.Path, "plugin-lifecycle.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "entered:" + firstThread);
        await first.DisposeAsync();
        try { Assert.False((await pending).Success); } catch (OperationCanceledException) { }
        Assert.True((await InvokeAsync(second, secondTool, "read")).GetProperty("running").GetBoolean());
        Assert.DoesNotContain("dispose:installed", PluginLogFile.ReadLines(log));
        await second.DisposeAsync();
        Assert.Contains("dispose:installed", PluginLogFile.ReadLines(log));
    }

    [Fact]
    public async Task InvalidAndNonRpcPlugins_DoNotHideIndependentTools()
    {
        using var plugins = new PluginRuntimeHarness();
        RemotePluginFixture.Write(plugins);
        var probeManifestPath = Path.Combine(plugins.PluginRoot("probe"), ".craft-plugin", "plugin.json");
        var probeManifest = JsonNode.Parse(File.ReadAllText(probeManifestPath))!;
        probeManifest["capabilities"] = null;
        probeManifest["interface"] = new JsonObject { ["capabilities"] = null };
        File.WriteAllText(probeManifestPath, probeManifest.ToJsonString());
        var malformedRoot = Path.Combine(plugins.PluginRoot("malformed"), ".craft-plugin");
        Directory.CreateDirectory(malformedRoot);
        File.WriteAllText(Path.Combine(malformedRoot, "plugin.json"), "{");
        plugins.WriteNoop("future");
        var manifestPath = Path.Combine(plugins.PluginRoot("future"), ".craft-plugin", "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["dotnet"]!["minHostVersion"] = "9999.0.0";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        DotNetPluginTestBundle.WritePlugin(plugins.PluginRoot("hidden"), "hidden", "Hidden.Plugin", NonRpcSource);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var diagnostics = new ConcurrentQueue<RemoteToolHostDiagnostic>();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path),
            diagnostic: diagnostics.Enqueue, hostPluginsDirectory: Path.GetDirectoryName(plugins.PluginRoot("probe")));
        await using var client = new RemoteExecutionClient();
        await using var session = await OpenAsync(client, server, "session");
        Assert.DoesNotContain(session.HostPlugins, plugin => plugin.Id == "future");
        Assert.Contains(session.HostPlugins, plugin => plugin.Id == "hidden");
        Assert.Contains(diagnostics, item => item.PluginId == "future" && item.EventName == PluginDotnetDiagnosticCodes.HostVersionUnsatisfied);
        Assert.Contains(diagnostics, item => item.EventName == "InvalidPluginManifestJson");
        var tools = await session.ListExportedToolsAsync("thread");
        Assert.DoesNotContain(tools, tool => tool.Name == "Hidden.Run");
        var probe = Assert.Single(tools, tool => tool.Name == "Probe.Run");
        Assert.Equal("first", (await InvokeAsync(session, probe, "read")).GetProperty("implementation").GetString());
    }

    [Fact]
    public async Task PreparedSource_DoesNotReplaceInstalledSource_WithTheSamePluginId()
    {
        using var installed = new PluginRuntimeHarness();
        RemotePluginFixture.Write(installed, "installed");
        using var prepared = new PluginRuntimeHarness();
        RemotePluginFixture.Write(prepared, "prepared");
        await using var manager = prepared.CreateManager();
        await manager.StartAsync(default);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1, "thread"));
        var snapshot = new EffectiveToolSnapshotBuilder().Build(registrations, 1);
        var local = Assert.Single(registrations);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path),
            hostPluginsDirectory: Path.GetDirectoryName(installed.PluginRoot("probe")));
        await using var client = new RemoteExecutionClient();
        await using var session = await OpenAsync(client, server, "session");
        var original = Assert.Single(await session.ListExportedToolsAsync("thread"), tool => tool.Name == "Probe.Run");
        await session.PrepareAsync("thread", snapshot, "agent");
        Assert.DoesNotContain(await session.ListExportedToolsAsync("thread"), tool => tool.Name == "Probe.Run");
        var result = await session.InvokeAsync(session.Route, local.Definition, RemoteToolContractHasher.Compute(local.Definition),
            new ToolInvocationContext("thread", "turn", "prepared-call", ToolInvocationAudience.Model,
                local.Definition.Name, local.Definition.Id, local.Binding.Id, 1, DateTimeOffset.UtcNow),
            new JsonObject { ["operation"] = "read" });
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("prepared", JsonSerializer.Deserialize<JsonElement>(result.Content!).GetProperty("implementation").GetString());
        var unavailable = await session.InvokeAsync(original, "thread", new JsonObject { ["operation"] = "read" });
        Assert.False(unavailable.Success);
        var other = Assert.Single(await session.ListExportedToolsAsync("other"), tool => tool.Name == "Probe.Run");
        var host = await session.InvokeAsync(other, "other", new JsonObject { ["operation"] = "read" });
        Assert.True(host.Success, host.Error?.Message);
        Assert.Equal("installed", JsonSerializer.Deserialize<JsonElement>(host.Content!).GetProperty("implementation").GetString());
    }

    [Fact]
    public async Task AgentMatching_UsesPreparedSource_WhenInstalledToolHasSameIdAndDifferentName()
    {
        using var installed = new PluginRuntimeHarness();
        RemotePluginFixture.Write(installed, "installed");
        using var prepared = new PluginRuntimeHarness();
        RemotePluginFixture.Write(prepared, "prepared", toolName: "Renamed");
        await using var manager = prepared.CreateManager();
        await manager.StartAsync(default);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1, "thread"));
        var snapshot = new EffectiveToolSnapshotBuilder().Build(registrations, 1);
        var local = Assert.Single(registrations);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path),
            hostPluginsDirectory: Path.GetDirectoryName(installed.PluginRoot("probe")));
        await using var client = new RemoteExecutionClient();
        await using var session = await OpenAsync(client, server, "session");
        await session.PrepareAsync("thread", snapshot, "agent");

        var tools = await session.ListExportedToolsAsync("thread");
        Assert.Contains(tools, tool => tool.Name == "Probe.Run");
        Assert.Contains(tools, tool => tool.Name == "Probe.Renamed");
        var summary = await session.DescribeAsync("thread");
        Assert.Contains("Probe.Renamed", summary.MatchedTools);
        var result = await session.InvokeAsync(session.Route, local.Definition, RemoteToolContractHasher.Compute(local.Definition),
            new ToolInvocationContext("thread", "turn", "prepared-call", ToolInvocationAudience.Model,
                local.Definition.Name, local.Definition.Id, local.Binding.Id, 1, DateTimeOffset.UtcNow),
            new JsonObject { ["operation"] = "read" });
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("prepared", JsonSerializer.Deserialize<JsonElement>(result.Content!).GetProperty("implementation").GetString());
    }

    [Fact]
    public async Task ThreadRelease_CancelsCatalogPlanning_AndPreventsItFromRestarting()
    {
        using var plugins = new PluginRuntimeHarness();
        RemotePluginFixture.Write(plugins);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path),
            hostPluginsDirectory: Path.GetDirectoryName(plugins.PluginRoot("probe")));
        await using var client = new RemoteExecutionClient();
        await using var session = await OpenAsync(client, server, "session");
        File.WriteAllText(Path.Combine(workspace.Path, "block-plugin-planning"), "");
        var planning = session.ListExportedToolsAsync("thread").AsTask();
        var log = Path.Combine(workspace.Path, "plugin-lifecycle.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "planning-entered");

        await session.ReleaseThreadAsync("thread").AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAnyAsync<Exception>(() => planning);
        Assert.Contains("planning-cancelled", PluginLogFile.ReadLines(log));
        await Assert.ThrowsAnyAsync<ModelContextProtocol.McpException>(() =>
            session.ListExportedToolsAsync("thread").AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Single(PluginLogFile.ReadLines(log), line => line == "planning-entered");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkspaceDrain_WithoutLease_ReleasesInstalledOwner(bool initializing)
    {
        using var plugins = new PluginRuntimeHarness();
        RemotePluginFixture.Write(plugins);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var leases = new WorkspaceLeaseManager();
        await using var host = new RemoteToolHostExecutionHost(Setup(home.Path, workspace.Path), leases,
            hostPluginsDirectory: Path.GetDirectoryName(plugins.PluginRoot("probe")));
        var marker = Path.Combine(workspace.Path, "block-plugin-activation");
        if (initializing) File.WriteAllText(marker, "");
        using var cancellation = new CancellationTokenSource();
        var loading = host.InstalledPluginsAsync("workspace", workspace.Path, cancellation.Token);
        if (initializing)
        {
            await PluginRuntimeHarness.WaitForLineAsync(Path.Combine(workspace.Path, "activation.log"), "entered");
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        }
        else
        {
            await loading;
        }

        await host.DrainWorkspaceAsync("workspace").WaitAsync(TimeSpan.FromSeconds(15));

        if (initializing)
        {
            Assert.Contains("cancelled", PluginLogFile.ReadLines(Path.Combine(workspace.Path, "activation.log")));
            File.Delete(marker);
        }
        else
        {
            Assert.Contains("dispose:first", PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        }
        var reloaded = await host.InstalledPluginsAsync("workspace", workspace.Path, default);
        Assert.Contains(reloaded!.Plugins, plugin => plugin.Id == "probe");
    }

    private static RemoteToolHostStorage Setup(string home, string workspace)
    {
        var storage = new RemoteToolHostStorage(home);
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["workspace"] = workspace });
        return storage;
    }

    private static async Task<RemoteExecutionSession> OpenAsync(RemoteExecutionClient client, RemoteToolHostTestServer server, string id) =>
        await client.OpenSessionAsync(id, server.PeerId, "workspace", await server.Directory.ConnectAsync(server.PeerId, default));

    private static async Task<JsonElement> InvokeAsync(RemoteExecutionSession session, RemoteExportedTool tool, string operation)
    {
        var result = await session.InvokeAsync(tool, "thread", new JsonObject { ["operation"] = operation });
        Assert.True(result.Success, result.Error?.Message);
        return JsonSerializer.Deserialize<JsonElement>(result.Content!);
    }

    private const string NonRpcSource = """
        using System.Collections.Generic;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using DotCraft.Tools;
        namespace Hidden;
        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken token)
            {
                context.Contributions.Add<IToolSource>(new Tool());
                return ValueTask.CompletedTask;
            }
        }
        internal sealed class Tool() : DotCraft.Tests.Bundle.TestTool("run", "Hidden", "Run", "Local only")
        {
            public override ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context,
                JsonObject arguments, CancellationToken token = default) => ValueTask.FromResult(ToolExecutionResult.Succeeded("local"));
        }
        """;
}
