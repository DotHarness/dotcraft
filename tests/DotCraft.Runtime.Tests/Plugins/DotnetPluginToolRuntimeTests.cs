using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers plugin Tools reaching the common dispatcher through the runtime manager.</summary>
public sealed class DotnetPluginToolRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task NativeTool_UsesCommonDispatcherAndHandlerFailureDoesNotFaultGeneration()
    {
        WritePluginBundle(
            _harness.PluginRoot("native.tools"),
            "native.tools",
            "NativeTools.Plugin",
            """
            using System;
            using System.Text.Json;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace NativeTools;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IToolSource>(new Echo());
                    return ValueTask.CompletedTask;
                }
                private sealed class Echo() : TestTool(
                    "echo-v1",
                    "sample",
                    "echo",
                    "Echoes a value.",
                    "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"]}",
                    new ToolPolicyHints(true, true, false, false))
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                    {
                        var value = arguments["value"]!.GetValue<string>();
                        if (value == "fail") throw new InvalidOperationException("handler exploded");
                        if (value == "invalid") return ValueTask.FromResult<ToolExecutionResult>(null!);
                        return ValueTask.FromResult(ToolExecutionResult.Succeeded(
                            "echo:" + value,
                            JsonSerializer.SerializeToElement(new { value })));
                    }
                }
            }
            """);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 7);
        var registration = Assert.Single(snapshot.Registrations).Value;
        Assert.Equal(new ToolName("sample", "echo"), registration.Definition.Name);
        Assert.True(registration.Definition.PolicyHints.RequiresApproval);
        Assert.True(registration.Definition.PolicyHints.ReadOnly);
        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        var events = new List<string>();
        var probe = new PipelineProbe(events);
        var dispatcher = new ToolDispatcher(probe, probe, probe, probe, probe, probe);

        var result = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["value"] = "hello" },
            Request("call-ok"));

        Assert.True(result.Success);
        Assert.Equal("echo:hello", result.Content);
        Assert.Equal("hello", result.StructuredContent?.GetProperty("value").GetString());
        Assert.Equal(
            ["started", "authority", "policy", "preHook", "approval", "normalize", "terminal", "postHook"],
            events);

        var failed = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["value"] = "fail" },
            Request("call-fail"));

        Assert.False(failed.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, failed.Error?.Code);
        var invalid = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["value"] = "invalid" },
            Request("call-invalid"));
        Assert.False(invalid.Success);
        Assert.Equal(ToolErrorCodes.ResultInvalid, invalid.Error?.Code);

        var plugin = Plugin(manager, "native.tools");
        AssertState(plugin, PluginDotnetRuntimeState.Active);
        Assert.Equal("echo-v1", Assert.Single(plugin.Tools!).Id);
    }

    [Fact]
    public async Task OldTurnSnapshotFailsUnavailableAndActiveCallDrainsBeforeUnload()
    {
        WritePluginBundle(
            _harness.PluginRoot("draining.tool"),
            "draining.tool",
            "DrainingTool.Plugin",
            """
            using System;
            using System.IO;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace DrainingTool;
            public sealed class Plugin : IDotCraftPlugin, IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    Directory.CreateDirectory(_workspace);
                    context.Contributions.Add<IToolSource>(new Waiter(_workspace));
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.WriteAllText(Path.Combine(_workspace, "disposed"), "yes");
                private sealed class Waiter(string workspace)
                    : TestTool("wait", null, "wait_for_release", "Waits for a test release marker.")
                {
                    public override async ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                    {
                        File.WriteAllText(Path.Combine(workspace, "call-started"), context.CallId);
                        while (!File.Exists(Path.Combine(workspace, "release")))
                            await Task.Delay(10, cancellationToken);
                        return ToolExecutionResult.Succeeded("released");
                    }
                }
            }
            """);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var oldSnapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 1);
        var registration = Assert.Single(oldSnapshot.Registrations).Value;
        var dispatch = new ToolDispatcher().DispatchAsync(
            oldSnapshot,
            registration.Definition.Name,
            [],
            Request("active-call")).AsTask();
        await WaitForFileAsync(Path.Combine(_harness.Workspace, "call-started"));

        var disable = manager.SetEnabledAsync("draining.tool", enabled: false);
        await Task.Delay(200);
        Assert.False(disable.IsCompleted);
        Assert.False(File.Exists(Path.Combine(_harness.Workspace, "disposed")));
        File.WriteAllText(Path.Combine(_harness.Workspace, "release"), "go");

        var result = await dispatch;
        await disable;
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_harness.Workspace, "disposed")));
        await WaitForStateAsync(manager, "draining.tool", PluginDotnetRuntimeState.Stopped);
        Assert.Empty(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(2)));

        var stale = await new ToolDispatcher().DispatchAsync(
            oldSnapshot,
            registration.Definition.Name,
            [],
            Request("stale-call"));
        Assert.False(stale.Success);
        Assert.Equal(ToolErrorCodes.Unavailable, stale.Error?.Code);
    }

    [Fact]
    public async Task DependencyBackedToolPinsProviderUntilInvocationCompletes()
    {
        var providerRoot = _harness.PluginRoot("tool.provider");
        var apiPath = Path.Combine(providerRoot, "dotnet", "Tool.Provider.Api.dll");
        Compile(apiPath, "namespace Tool.Provider.Api; public interface IValue { string Get(); }");
        WritePluginBundle(
            providerRoot,
            "tool.provider",
            "ToolProvider.Plugin",
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using Tool.Provider.Api;
            namespace ToolProvider;
            public sealed class Plugin : IDotCraftPlugin, System.IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    context.Exports.Add<IValue>(new Value());
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.WriteAllText(Path.Combine(_workspace, "provider-disposed"), "yes");
                private sealed class Value : IValue { public string Get() => "provider-value"; }
            }
            """,
            exportedApiAssemblies: ["./dotnet/Tool.Provider.Api.dll"],
            runtimeReferences: [apiPath]);
        var consumerRoot = _harness.PluginRoot("tool.consumer");
        var consumerApi = Path.Combine(consumerRoot, "dotnet", "Tool.Provider.Api.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(consumerApi)!);
        File.Copy(apiPath, consumerApi);
        WritePluginBundle(
            consumerRoot,
            "tool.consumer",
            "ToolConsumer.Plugin",
            """
            using System.IO;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            using Tool.Provider.Api;
            namespace ToolConsumer;
            public sealed class Plugin : IDotCraftPlugin, System.IDisposable
            {
                private string _workspace = "";
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    _workspace = context.WorkspaceRoot;
                    Directory.CreateDirectory(_workspace);
                    var service = context.Dependencies.GetRequired<IValue>("tool.provider");
                    context.Contributions.Add<IToolSource>(new Reader(_workspace, service));
                    return ValueTask.CompletedTask;
                }
                public void Dispose() => File.WriteAllText(Path.Combine(_workspace, "consumer-disposed"), "yes");
                private sealed class Reader(string workspace, IValue service)
                    : TestTool("provider-value", null, "provider_value", "Reads provider value after release.")
                {
                    public override async ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                    {
                        File.WriteAllText(Path.Combine(workspace, "dependency-call-started"), "yes");
                        while (!File.Exists(Path.Combine(workspace, "dependency-release")))
                            await Task.Delay(10, cancellationToken);
                        return ToolExecutionResult.Succeeded(service.Get());
                    }
                }
            }
            """,
            dependencies: new Dictionary<string, string> { ["tool.provider"] = "1.0.0" },
            runtimeReferences: [consumerApi]);
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 1);
        var registration = Assert.Single(snapshot.Registrations).Value;
        var dispatch = new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request("dependency-call")).AsTask();
        await WaitForFileAsync(Path.Combine(_harness.Workspace, "dependency-call-started"));

        var disable = manager.SetEnabledAsync("tool.provider", enabled: false);
        await Task.Delay(200);
        Assert.False(disable.IsCompleted);
        Assert.False(File.Exists(Path.Combine(_harness.Workspace, "provider-disposed")));
        Assert.False(File.Exists(Path.Combine(_harness.Workspace, "consumer-disposed")));
        File.WriteAllText(Path.Combine(_harness.Workspace, "dependency-release"), "go");

        // Deactivation runs consumers before providers, so the in-flight call keeps its provider.
        Assert.Equal("provider-value", (await dispatch).Content);
        await disable;
        Assert.True(File.Exists(Path.Combine(_harness.Workspace, "consumer-disposed")));
        Assert.True(File.Exists(Path.Combine(_harness.Workspace, "provider-disposed")));
    }

    [Fact]
    public async Task DuplicateToolRegistrationIsReportedAndSkippedRatherThanFatal()
    {
        WritePluginBundle(
            _harness.PluginRoot("invalid.tools"),
            "invalid.tools",
            "InvalidTools.Plugin",
            """
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace InvalidTools;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IToolSource>(new Sample("first", "same_name"));
                    context.Contributions.Add<IToolSource>(new Sample("first", "second_name"));
                    context.Contributions.Add<IToolSource>(new Sample("third", "not a safe name"));
                    return ValueTask.CompletedTask;
                }
                private sealed class Sample(string id, string name) : TestTool(id, null, name, "test")
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default) =>
                        ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
                }
            }
            """);
        await using var manager = _harness.CreateManager();

        await manager.StartAsync(CancellationToken.None);

        // A duplicate id and a malformed name each cost their own Tool and nothing else.
        var registration = Assert.Single(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(1)));
        Assert.Equal("same_name", registration.Definition.Name.Name);
        AssertState(Plugin(manager, "invalid.tools"), PluginDotnetRuntimeState.Active);
        Assert.Equal(2, manager.ToolSource.Diagnostics.Count);
        Assert.All(
            manager.ToolSource.Diagnostics,
            diagnostic => Assert.Equal("PluginToolContributionInvalid", diagnostic.Code));
    }

    private sealed class PipelineProbe(List<string> events) :
        IToolAuthorityEvaluator,
        IToolPolicyEvaluator,
        IToolDispatchHookRunner,
        IToolApprovalEvaluator,
        IToolInvocationRecorder,
        IToolResultNormalizer
    {
        public ValueTask<ToolDispatchDecision> CheckAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            CancellationToken cancellationToken = default)
        {
            events.Add("authority");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask<ToolDispatchDecision> EvaluateAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            events.Add("policy");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask<ToolDispatchDecision> RunPreToolUseAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            events.Add("preHook");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask RunTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default)
        {
            events.Add("postHook");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ToolDispatchDecision> RequestAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            events.Add("approval");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            events.Add("started");
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            events.Add("terminal");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ToolExecutionResult> NormalizeAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default)
        {
            events.Add("normalize");
            return ValueTask.FromResult(result);
        }
    }
}
