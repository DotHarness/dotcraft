using DotCraft.Tests.Runtime.Plugins;

namespace DotCraft.Tests.Tools;

internal static class RemotePluginFixture
{
    internal static void Write(PluginRuntimeHarness harness, string implementation = "first", bool deferred = true)
    {
        harness.WriteNoop("dependency");
        var root = harness.PluginRoot("probe");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.schema.json"),
            """{"fields":[{"key":"label","type":"text","defaultValue":"default"}]}""");
        DotNetPluginTestBundle.WritePluginBundle(root, "probe", "Probe.Plugin", Source
            .Replace("IMPLEMENTATION", implementation, StringComparison.Ordinal)
            .Replace("EXPOSURE", deferred ? "ToolExposure.Deferred" : "ToolExposure.Direct", StringComparison.Ordinal),
            dependencies: new Dictionary<string, string> { ["dependency"] = "1.0.0" }, settings: "./settings.schema.json");
        File.WriteAllText(Path.Combine(root, "resource.txt"), "bundle-resource");
    }

    private const string Source = """
        using System;
        using System.IO;
        using System.Linq;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Contributions;
        using DotCraft.Plugins;
        using DotCraft.Tools;
        namespace Probe;
        public sealed class Plugin : IDotCraftPlugin
        {
            public async ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                if (File.Exists(Path.Combine(context.WorkspaceRoot, "block-plugin-activation")))
                {
                    var log = Path.Combine(context.WorkspaceRoot, "activation.log");
                    File.AppendAllText(log, "entered\n");
                    try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                    finally { File.AppendAllText(log, "cancelled\n"); }
                }
                context.Contributions.Add<IToolSource>(new Source(context), new ContributionOptions { OwnsContribution = true });
            }
        }
        internal sealed class Source : IToolSource, IThreadScopedToolSource, IAsyncDisposable
        {
            private readonly IPluginActivationContext _activation;
            private readonly ConcurrentDictionary<string, Job> _jobs = new();
            private readonly TaskCompletionSource _workerStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private string Log => Path.Combine(_activation.WorkspaceRoot, "plugin-lifecycle.log");
            public Source(IPluginActivationContext context)
            {
                _activation = context;
                File.AppendAllText(Log, "activate:IMPLEMENTATION\n");
                context.Lifetime.Run(async stopping =>
                {
                    try { await Task.Delay(Timeout.Infinite, stopping); }
                    finally { _workerStopped.TrySetResult(); }
                });
            }
            public string SourceId => "probe";
            public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(ToolPlanningContext context,
                CancellationToken cancellationToken = default)
            {
                var id = new ToolDefinitionId(ToolSourceKind.PluginNative, "probe", new SourceToolId("run"));
                var definition = new ToolDefinition(id, new ToolName("Probe", "Run"), "Run a stateful probe.",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object", properties = new { operation = new { type = "string" } },
                        required = new[] { "operation" }, additionalProperties = false
                    }), annotations: new Dictionary<string, JsonElement>
                    { [RemoteToolMetadata.RpcEligibleAnnotation] = JsonSerializer.SerializeToElement(true) });
                var binding = new ToolRuntimeBinding(new("probe:" + context.Revision), id,
                    new Runtime(this, context.Mode), ToolBindingLeases.AlwaysAvailable, "probe", context.Revision,
                    File.Exists(Path.Combine(_activation.WorkspaceRoot, "executor-unavailable"))
                        ? ToolBindingAvailability.Unavailable : ToolBindingAvailability.Available);
                return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([new ToolRegistration(definition, binding,
                    ToolProjectionShape.StandardPair, EXPOSURE, deferred: new DeferredToolDescriptor("Probe", "probe"))]);
            }
            private sealed class Runtime(Source source, string mode) : IToolRuntime
            {
                public async ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context, JsonObject arguments,
                    CancellationToken cancellationToken = default)
                {
                    var operation = arguments["operation"]!.GetValue<string>();
                    if (operation == "start") source._jobs.GetOrAdd(context.ThreadId, _ => new Job());
                    if (operation == "cancel" && source._jobs.TryGetValue(context.ThreadId, out var cancel))
                        await cancel.StopAsync();
                    if (operation == "block" || operation == "block-until-stop")
                    {
                        File.AppendAllText(source.Log, "entered:" + context.ThreadId + "\n");
                        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                        finally
                        {
                            if (operation == "block-until-stop") await source._workerStopped.Task;
                            File.AppendAllText(source.Log, "cancelled:" + context.ThreadId + "\n");
                        }
                    }
                    var data = new
                    {
                        implementation = "IMPLEMENTATION", mode, thread = context.ThreadId,
                        workspace = source._activation.WorkspaceRoot,
                        settings = source._activation.Settings,
                        resource = File.ReadAllText(Path.Combine(source._activation.ContentRoot, "resource.txt")),
                        running = source._jobs.TryGetValue(context.ThreadId, out var job) && !job.Work.IsCompleted
                    };
                    return ToolExecutionResult.Succeeded(JsonSerializer.Serialize(data), JsonSerializer.SerializeToElement(data));
                }
            }
            public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
            {
                if (_jobs.TryRemove(threadId, out var job)) await job.StopAsync();
                File.AppendAllText(Log, "release:" + threadId + "\n");
            }
            public async ValueTask DisposeAsync()
            {
                foreach (var thread in _jobs.Keys) await ReleaseThreadAsync(thread);
                File.AppendAllText(Log, "dispose:IMPLEMENTATION\n");
            }
            private sealed class Job
            {
                private readonly CancellationTokenSource _stop = new();
                public Task Work { get; }
                public Job() => Work = Task.Delay(Timeout.Infinite, _stop.Token);
                public async Task StopAsync()
                {
                    await _stop.CancelAsync();
                    try { await Work; } catch (OperationCanceledException) { }
                }
            }
        }
        """;
}
