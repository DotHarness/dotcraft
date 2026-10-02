using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeNestedDispatchTests
{
    [Fact]
    public async Task NestedCalls_GoThroughDispatcherAndRejectedApprovalRejectsThePromise()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var workspace = Directory.CreateTempSubdirectory("codemode-").FullName;
        var recorder = new RecordingRecorder();
        var approvals = new SlowRejectingApprovalService(TimeSpan.FromMilliseconds(3000));
        var approvalEvaluator = new CommonToolApprovalEvaluator();
        approvalEvaluator.Bind(new DotCraft.Sessions.SessionScopedApprovalService(approvals));
        var dispatcher = new ToolDispatcher(approvalEvaluator: approvalEvaluator, recorder: recorder);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [Registration("Echo", requiresApproval: false), Registration("Guarded", requiresApproval: true)],
            revision: 1);
        await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), workspace, new CodeModeLimits());
        var config = new AppConfig();
        config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.On;
        var finalizer = new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher);
        var planning = new ToolPlanningContext("thread_1", null, workspace, Path.Combine(workspace, ".craft"), "agent", null, null, 1);

        var exec = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations.Single(static registration => registration.Definition.Name.Name == "exec");
        var context = new ToolInvocationContext(
            "thread_1", "turn_1", "call_exec", ToolInvocationAudience.Model, exec.Definition.Name,
            exec.Definition.Id, exec.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: workspace);
        using var hostScope = ToolHostExecutionScope.Set(
            new ToolHostExecutionContext("thread_1", "turn_1", workspace, approvals, null!));

        var result = await exec.Binding.Runtime.InvokeAsync(context, new JsonObject
        {
            ["code"] = """
                // @exec: {"timeout_ms": 2000}
                const [echoed, guarded] = await Promise.allSettled([tools.Echo({ value: 'hi' }), tools.Guarded({ value: 'x' })]);
                text(echoed.value);
                text(guarded.reason.message);
                """
        });

        Assert.True(result.Success);
        Assert.StartsWith("Script completed", result.Content);
        Assert.Contains("echo:hi", result.Content);
        Assert.Contains(ToolErrorCodes.ApprovalRejected, result.Content);
        Assert.Equal(1, approvals.Requests);
        Assert.Equal(["Echo", "Guarded"], recorder.Started.Select(static started => started.ToolName.Name).Order());
        Assert.All(recorder.Started, started =>
        {
            Assert.Equal("codeMode", started.Origin?.Kind);
            Assert.Equal("call_exec", started.Origin?.SourceItemId);
            Assert.StartsWith("exec-", started.CallId);
            Assert.Equal(ToolInvocationAudience.Model, started.Audience);
            Assert.Equal("turn_1", started.TurnId);
        });
    }

    [Fact]
    public async Task WorkerStartup_DoesNotCountAgainstTheScriptTimeout()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var workspace = Directory.CreateTempSubdirectory("codemode-").FullName;
        var dispatcher = new ToolDispatcher();
        var snapshot = new EffectiveToolSnapshotBuilder().Build([Registration("Echo", requiresApproval: false)], revision: 1);
        await using var host = new CodeModeWorkerHost(new SlowStartProcessFactory(TimeSpan.FromMilliseconds(3000)), workspace, new CodeModeLimits());
        var config = new AppConfig();
        config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.On;
        var finalizer = new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher);
        var planning = new ToolPlanningContext("thread_1", null, workspace, Path.Combine(workspace, ".craft"), "agent", null, null, 1);
        var exec = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations.Single(static registration => registration.Definition.Name.Name == "exec");
        var context = new ToolInvocationContext(
            "thread_1", "turn_1", "call_exec", ToolInvocationAudience.Model, exec.Definition.Name,
            exec.Definition.Id, exec.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: workspace);

        var result = await exec.Binding.Runtime.InvokeAsync(context, new JsonObject
        {
            ["code"] = """
                // @exec: {"timeout_ms": 2000}
                text('ready');
                """
        });

        Assert.StartsWith("Script completed", result.Content);
        Assert.Contains("ready", result.Content);
    }

    private static ToolRegistration Registration(string name, bool requiresApproval)
    {
        var id = new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId(name));
        var definition = new ToolDefinition(
            id,
            new ToolName(null, name),
            $"{name} test tool",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { value = new { type = "string" } }
            }),
            policyHints: new ToolPolicyHints(RequiresApproval: requiresApproval));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"test:{name}"),
            id,
            new EchoRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            "test",
            1);
        return new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair);
    }

    private sealed class EchoRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded($"echo:{arguments["value"]}"));
    }

    private sealed class RecordingRecorder : IToolInvocationRecorder
    {
        private readonly ConcurrentQueue<ToolInvocationContext> _started = new();

        public IReadOnlyList<ToolInvocationContext> Started => _started.ToArray();

        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            _started.Enqueue(context);
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class SlowStartProcessFactory(TimeSpan delay) : IManagedChildProcessFactory
    {
        public ManagedChildProcess Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
            Thread.Sleep(delay);
            return ManagedChildProcess.Start(startInfo);
        }
    }

    private sealed class SlowRejectingApprovalService(TimeSpan delay) : IApprovalService
    {
        private int _requests;

        public int Requests => _requests;

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => RejectAsync();

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) => RejectAsync();

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) => RejectAsync();

        private async Task<bool> RejectAsync()
        {
            Interlocked.Increment(ref _requests);
            await Task.Delay(delay);
            return false;
        }
    }
}
