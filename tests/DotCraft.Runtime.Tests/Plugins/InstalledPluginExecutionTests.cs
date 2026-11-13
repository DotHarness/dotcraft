using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetExtensionTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class InstalledPluginExecutionTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExplicitDirectory_LoadsIndependentPluginsAndReportsBlockedAndFaultedPlugins()
    {
        var directory = Path.Combine(_harness.Root, "installed");
        var good = Path.Combine(directory, "good");
        WritePlugin(good, "installed.good", "Installed.Plugin", ToolPluginSource);
        WritePlugin(Path.Combine(directory, "independent"), "installed.independent", "Installed.Plugin", ToolPluginSource);
        var newer = Path.Combine(directory, "newer");
        WritePlugin(newer, "installed.newer", "Installed.Plugin", ToolPluginSource);
        var manifestPath = Path.Combine(newer, ".craft-plugin", "plugin.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace(
            "\"minHostVersion\": \"0.0.0\"", "\"minHostVersion\": \"9999.0.0\""));
        WritePlugin(Path.Combine(directory, "faulted"), "installed.faulted", "Installed.Plugin",
            ToolPluginSource.Replace("context.Contributions.Add<IToolSource>(new Echo());",
                "throw new System.InvalidOperationException(\"activation failed\");"));
        WritePluginBundle(Path.Combine(directory, "dependent"), "installed.dependent", "Installed.Plugin", ToolPluginSource,
            dependencies: new Dictionary<string, string> { ["installed.newer"] = "1.0.0" });
        WritePlugin(_harness.PluginRoot("workspace.ignored"), "workspace.ignored", "Installed.Plugin", ToolPluginSource);
        var explicitOther = Path.Combine(_harness.Root, "configured");
        WritePlugin(explicitOther, "configured.ignored", "Installed.Plugin", ToolPluginSource);
        _harness.Config.Plugins.PluginRoots.Add(explicitOther);

        await using (var host = CreateHost())
        {
            await host.LoadInstalledAsync(directory);

            Assert.Equal(["installed.good", "installed.independent"], host.InstalledPlugins.Select(plugin => plugin.Id));
            Assert.Equal(good, host.InstalledPlugins.Single(plugin => plugin.Id == "installed.good").RootPath);
            Assert.Contains(host.Diagnostics, diagnostic => diagnostic.PluginId == "installed.newer"
                && diagnostic.Code == DotNetExtensionDiagnosticCodes.HostVersionUnsatisfied);
            Assert.Contains(host.Diagnostics, diagnostic => diagnostic.PluginId == "installed.faulted"
                && diagnostic.Code == "PluginActivationFailed");
            Assert.Contains(host.Diagnostics, diagnostic => diagnostic.PluginId == "installed.dependent"
                && diagnostic.Code == "PluginDependencyUnsatisfied");

            var registrations = await host.GetRegistrationsAsync(PlanningContext(1));
            Assert.Equal(2, registrations.Count);
            foreach (var registration in registrations)
            {
                var result = await registration.Binding.Runtime.InvokeAsync(
                    new("thread-1", "turn-1", "echo", ToolInvocationAudience.Model,
                        registration.Definition.Name, registration.Definition.Id, registration.Binding.Id,
                        1, DateTimeOffset.UtcNow),
                    new JsonObject { ["value"] = "hello" });
                Assert.True(result.Success);
                Assert.Equal("echo:hello", result.Content);
            }
        }
        Assert.True(File.Exists(Path.Combine(good, "dotnet", "Plugin.dll")));
    }

    [Fact]
    public async Task NonToolContribution_IsRejectedWithoutBlockingIndependentToolPlugin()
    {
        var directory = Path.Combine(_harness.Root, "installed");
        WritePlugin(Path.Combine(directory, "good"), "installed.good", "Installed.Plugin", ToolPluginSource);
        WritePlugin(Path.Combine(directory, "prompt"), "installed.prompt", "Prompt.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace Prompt;
            public sealed class Plugin : IDotCraftPlugin, ISystemPromptSection
            {
                public string Name => "prompt";
                public string? GetContent(SystemPromptSectionContext context) => "must not be installed";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<ISystemPromptSection>(this);
                    return ValueTask.CompletedTask;
                }
            }
            """);
        await using var host = CreateHost();

        await host.LoadInstalledAsync(directory);

        Assert.Equal("installed.good", Assert.Single(host.InstalledPlugins).Id);
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.PluginId == "installed.prompt"
            && diagnostic.Code == "PluginActivationFailed");
        Assert.Equal("installed.good", Assert.Single(await host.GetRegistrationsAsync(PlanningContext(1))).Definition.Id.SourceId);
    }

    private PluginExecutionHost CreateHost() => new(
        DotCraftPathResolver.Resolve(new DotCraftRuntimeOptions
        {
            Config = _harness.Config,
            WorkspacePath = _harness.Workspace,
            DataPath = ".craft",
            UserDataPath = Path.Combine(_harness.Root, "user-data")
        }),
        EmptyServices,
        _harness.Config);

    private const string ToolPluginSource = """
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using DotCraft.Tests.Bundle;
        using DotCraft.Tools;
        namespace Installed;
        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<IToolSource>(new Echo());
                return ValueTask.CompletedTask;
            }
            private sealed class Echo() : TestTool("echo", "installed", "echo", "Echoes a value.")
            {
                public override ValueTask<ToolExecutionResult> InvokeAsync(
                    ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(ToolExecutionResult.Succeeded("echo:" + arguments["value"]!.GetValue<string>()));
            }
        }
        """;
}
