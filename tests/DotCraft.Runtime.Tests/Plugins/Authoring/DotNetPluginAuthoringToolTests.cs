using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginAuthoringToolTests :
    IClassFixture<AuthoringReferencePackFixture>,
    IDisposable
{
    private const string PluginId = "acme.agent-tool";

    private readonly AuthoringReferencePackFixture _fixture;
    private readonly PluginRuntimeHarness _harness = new();

    public DotNetPluginAuthoringToolTests(AuthoringReferencePackFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Source_UsesOneStableNamespace_AndSupportsDirectLoading()
    {
        await using var manager = _harness.CreateManager(trustInstalled: false);
        var source = new DotNetPluginAuthoringToolSource(_harness.Config, manager);

        var first = await source.GetRegistrationsAsync(Planning(DataRoot, revision: 1));
        var second = await source.GetRegistrationsAsync(Planning(DataRoot, revision: 2));

        Assert.Equal(["Build", "Inspect"], first.Select(static item => item.Definition.Name.Name));
        Assert.All(first, registration =>
        {
            Assert.Equal("DotNetPlugin", registration.Definition.Name.Namespace);
            Assert.Equal(ToolExposure.Deferred, registration.Exposure);
            Assert.Equal("DotNetPlugin", registration.Deferred?.Namespace);
            Assert.Equal(ToolSourceKind.CoreNative, registration.Definition.Id.Kind);
            Assert.Equal(ToolInvocationAudience.Model, registration.InvocationAudiences);
            Assert.Null(registration.ProviderFlatNameOverride);
        });
        var snapshot = new EffectiveToolSnapshotBuilder().Build(first, revision: 1);
        Assert.Equal(
            "DotNetPlugin__Build",
            snapshot.ProviderFlatNames[new ToolName("DotNetPlugin", "Build")]);
        Assert.Equal(
            "DotNetPlugin__Inspect",
            snapshot.ProviderFlatNames[new ToolName("DotNetPlugin", "Inspect")]);
        Assert.Equal(Surface(first), Surface(second));

        _harness.Config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
        var direct = await source.GetRegistrationsAsync(Planning(DataRoot, revision: 3));

        Assert.Equal(Surface(first), Surface(direct));
        Assert.All(direct, registration =>
        {
            Assert.Equal(ToolExposure.Direct, registration.Exposure);
            Assert.Null(registration.Deferred);
        });
    }

    [Fact]
    public async Task Methods_InspectCurrentApi_AndBuildForTheNextToolSnapshot()
    {
        WriteProject(ToolPlugin("v1"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        var references = new Lazy<DotNetPluginReferenceSet>(_fixture.Load);
        var methods = new DotNetPluginAuthoringToolMethods(
            DataRoot,
            manager,
            references,
            new Lazy<DotNetPluginApiInspector>(() => new DotNetPluginApiInspector(references.Value)));
        var authoringSource = new DotNetPluginAuthoringToolSource(_harness.Config, manager);
        var authoringSurface = Surface(
            await authoringSource.GetRegistrationsAsync(Planning(DataRoot, revision: 1)));
        var currentTurn = await BuildSnapshotAsync(manager.ToolSource, revision: 1);

        var inspected = methods.Inspect("IDotCraftPlugin");
        var built = await methods.BuildAsync(PluginId);

        Assert.Contains(
            inspected,
            static symbol => symbol.Signature.Contains("IDotCraftPlugin", StringComparison.Ordinal));
        Assert.Equal("built", built.Outcome);
        Assert.Equal("active", built.State);
        Assert.Empty(currentTurn.Registrations);
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());

        var nextTurn = await BuildSnapshotAsync(manager.ToolSource, revision: 2);
        var initialPluginSurface = Surface(nextTurn.Registrations.Values);
        var firstCall = await new ToolDispatcher().DispatchAsync(
            nextTurn,
            new ToolName("sample", "echo"),
            [],
            Request("first-call"));
        Assert.True(firstCall.Success);
        Assert.Equal("v1", firstCall.Content);

        var repeated = await methods.BuildAsync(PluginId);
        Assert.Equal("noChange", repeated.Outcome);
        Assert.Equal(
            authoringSurface,
            Surface(await authoringSource.GetRegistrationsAsync(Planning(DataRoot, revision: 1))));

        File.WriteAllText(SourcePath, ToolPlugin("v2"));
        var changed = await methods.BuildAsync(PluginId);
        var replacementTurn = await BuildSnapshotAsync(manager.ToolSource, revision: 3);
        var changedCall = await new ToolDispatcher().DispatchAsync(
            replacementTurn,
            new ToolName("sample", "echo"),
            [],
            Request("changed-call"));
        var staleCall = await new ToolDispatcher().DispatchAsync(
            nextTurn,
            new ToolName("sample", "echo"),
            [],
            Request("stale-call"));

        Assert.Equal("built", changed.Outcome);
        Assert.Equal(initialPluginSurface, Surface(replacementTurn.Registrations.Values));
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());
        Assert.True(changedCall.Success);
        Assert.Equal("v2", changedCall.Content);
        Assert.False(staleCall.Success);
        Assert.Equal(ToolErrorCodes.Unavailable, staleCall.Error?.Code);
    }

    [Fact]
    public async Task Source_BuildsTheProjectInTheEffectiveWorkspace()
    {
        var effectiveWorkspace = Path.Combine(_harness.Root, "worktree");
        var effectiveDataRoot = Path.Combine(effectiveWorkspace, ".craft");
        WriteProjectAt(effectiveDataRoot, ToolPlugin("worktree"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        var source = new DotNetPluginAuthoringToolSource(_harness.Config, manager);
        var planning = new ToolPlanningContext(
            "thread-1",
            "turn-1",
            effectiveWorkspace,
            DataRoot,
            "default",
            null,
            [],
            revision: 1);
        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync([source], planning);

        var build = await new ToolDispatcher().DispatchAsync(
            snapshot,
            new ToolName("DotNetPlugin", "Build"),
            new JsonObject { ["pluginId"] = PluginId },
            Request("worktree-build"));

        Assert.True(build.Success, build.Error?.Message);
        Assert.NotNull(build.Content);
        using (var buildJson = JsonDocument.Parse(build.Content))
            Assert.Equal("built", buildJson.RootElement.GetProperty("outcome").GetString());
        Assert.False(Directory.Exists(ProjectRoot));
        Assert.True(File.Exists(Path.Combine(
            effectiveDataRoot,
            "plugin-projects",
            PluginId,
            "plugin",
            "lib",
            "Acme.AgentTool.dll")));

        var nextTurn = await BuildSnapshotAsync(manager.ToolSource, revision: 2);
        var invocation = await new ToolDispatcher().DispatchAsync(
            nextTurn,
            new ToolName("sample", "echo"),
            [],
            Request("worktree-call"));
        Assert.True(invocation.Success);
        Assert.Equal("worktree", invocation.Content);
    }

    private void WriteProject(string source) => WriteProjectAt(DataRoot, source);

    private static void WriteProjectAt(string dataRoot, string source)
    {
        var projectRoot = Path.Combine(dataRoot, "plugin-projects", PluginId);
        var pluginRoot = Path.Combine(projectRoot, "plugin");
        var sourcePath = Path.Combine(projectRoot, "src", "Plugin.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "lib"));
        File.WriteAllText(sourcePath, source);
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{PluginId}}",
              "version": "1.0.0",
              "displayName": "Agent Tool",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.0.0",
                "entryAssembly": "./lib/Acme.AgentTool.dll",
                "entryType": "Acme.AgentTool.Plugin"
              }
            }
            """);
    }

    private string DataRoot => Path.Combine(_harness.Workspace, ".craft");

    private string ProjectRoot => Path.Combine(DataRoot, "plugin-projects", PluginId);

    private string PluginRoot => Path.Combine(ProjectRoot, "plugin");

    private string SourcePath => Path.Combine(ProjectRoot, "src", "Plugin.cs");

    private static ToolPlanningContext Planning(string dataPath, long revision) =>
        new("thread-1", "turn-1", Path.GetDirectoryName(dataPath)!, dataPath, "default", null, [], revision);

    private static string Surface(IEnumerable<ToolRegistration> registrations) =>
        string.Join(
            "\n",
            registrations.Select(static registration => string.Join(
                "|",
                registration.Definition.Name.Namespace,
                registration.Definition.Name.Name,
                registration.Definition.Description,
                registration.Definition.InputSchema.GetRawText(),
                registration.Definition.OutputSchema?.GetRawText())));

    private static string ToolPlugin(string result) => $$"""
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;
        using DotCraft.Tools;

        namespace Acme.AgentTool;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<IToolSource>(new EchoTool());
                return ValueTask.CompletedTask;
            }

            private sealed class EchoTool : IToolSource, IToolRuntime
            {
                public string SourceId => "agent-tool";

                public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
                    ToolPlanningContext context,
                    CancellationToken cancellationToken = default)
                {
                    var id = new ToolDefinitionId(
                        ToolSourceKind.PluginNative,
                        SourceId,
                        new SourceToolId("echo"));
                    var definition = new ToolDefinition(
                        id,
                        new ToolName("sample", "echo"),
                        "Returns the current plugin version.",
                        JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone(),
                        policyHints: new ToolPolicyHints(ReadOnly: true));
                    var binding = new ToolRuntimeBinding(
                        new RuntimeBindingId($"{SourceId}:{context.Revision}"),
                        id,
                        this,
                        ToolBindingLeases.AlwaysAvailable,
                        SourceId,
                        context.Revision);
                    return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                        [new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair)]);
                }

                public ValueTask<ToolExecutionResult> InvokeAsync(
                    ToolInvocationContext context,
                    JsonObject arguments,
                    CancellationToken cancellationToken = default) =>
                    ValueTask.FromResult(ToolExecutionResult.Succeeded("{{result}}"));
            }
        }
        """;
}
