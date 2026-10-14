using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeNestedDispatchTests
{
    [Fact]
    public async Task NestedCalls_GoThroughDispatcherAndRejectedApprovalRejectsThePromise()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var workspace = Directory.CreateTempSubdirectory("codemode-").FullName;
        var recorder = new RecordingRecorder();
        var approvals = new SlowRejectingApprovalService(TimeSpan.FromMilliseconds(50));
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

        var exec = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations.Single(static registration => registration.Definition.Name.Name == "CodeMode");
        var context = new ToolInvocationContext(
            "thread_1", "turn_1", "call_exec", ToolInvocationAudience.Model, exec.Definition.Name,
            exec.Definition.Id, exec.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: workspace);
        using var hostScope = ToolHostExecutionScope.Set(
            new ToolHostExecutionContext("thread_1", "turn_1", workspace, approvals, null!));

        var result = await exec.Binding.Runtime.InvokeAsync(context, new JsonObject
        {
            ["code"] = """
                const [echoed, guarded] = await Promise.allSettled([tools.Echo({ value: 'hi' }), tools.Guarded({ value: 'x' })]);
                text(echoed.value);
                text(guarded.reason.message);
                """
        });

        Assert.True(result.Success, result.Content);
        Assert.True(result.Content?.StartsWith("Script completed") == true, result.Content);
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
    public async Task ANestedResultWithImagesReachesTheScriptAsContentBlocksItCanShow()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var workspace = Directory.CreateTempSubdirectory("codemode-").FullName;
        var dispatcher = new ToolDispatcher();
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [Registration("Echo", requiresApproval: false), Registration("Snapshot", requiresApproval: false, new ImageRuntime(), returnsContent: true)],
            revision: 1);
        await using var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), workspace, new CodeModeLimits());
        var config = new AppConfig();
        config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.Only;
        var finalizer = new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), dispatcher);
        var planning = new ToolPlanningContext("thread_1", null, workspace, Path.Combine(workspace, ".craft"), "agent", null, null, 1);
        var exec = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations.Single(static registration => registration.Definition.Name.Name == "CodeMode");
        var context = new ToolInvocationContext(
            "thread_1", "turn_1", "call_exec", ToolInvocationAudience.Model, exec.Definition.Name,
            exec.Definition.Id, exec.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: workspace);

        var result = await exec.Binding.Runtime.InvokeAsync(context, new JsonObject
        {
            ["code"] = """
                const echoed = await tools.Echo({ value: 'hi' });
                const shot = await tools.Snapshot({});
                text(typeof echoed);
                text(shot.content[0].text);
                image(shot.content.find(block => block.type === 'image'));
                """
        });

        Assert.True(result.Success, result.Content);
        Assert.Contains("string", result.Content);
        Assert.Contains("captured", result.Content);
        var image = Assert.Single(result.ContentItems!.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(ImageRuntime.Png, image.Data.ToArray());
        Assert.Equal("completed", result.StructuredContent?.GetProperty("status").GetString());
        Assert.Equal(2, result.StructuredContent?.GetProperty("nestedCalls").GetInt32());
    }

    [Fact]
    public void An_oversized_MCP_error_keeps_its_error_flag()
    {
        var id = new ToolDefinitionId(ToolSourceKind.Mcp, "server", new SourceToolId("Lookup"));
        var registration = new ToolRegistration(
            new ToolDefinition(id, new ToolName(null, "Lookup"), "Lookup", JsonSerializer.SerializeToElement(new { type = "object" })),
            new ToolRuntimeBinding(new RuntimeBindingId("mcp:Lookup"), id, new EchoRuntime(), ToolBindingLeases.AlwaysAvailable, "server", 1),
            ToolProjectionShape.StandardPair);
        var raw = JsonSerializer.SerializeToElement(new
        {
            content = new[] { new { type = "text", text = new string('x', 2048) } },
            isError = true
        });
        var result = new ToolExecutionResult(false, "failed", rawSourceResult: raw, error: new ToolError(ToolErrorCodes.ExecutionFailed, "failed"));

        var (value, error) = CodeModeResults.ToScriptValue(registration, result, maxBytes: 1024);

        Assert.Null(error);
        Assert.True(value!["isError"]!.GetValue<bool>());
    }

    private static ToolRegistration Registration(string name, bool requiresApproval, IToolRuntime? runtime = null, bool returnsContent = false)
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
            annotations: returnsContent
                ? new Dictionary<string, JsonElement> { [ToolContentMetadata.ReturnsContentAnnotation] = JsonSerializer.SerializeToElement(true) }
                : null,
            policyHints: new ToolPolicyHints(RequiresApproval: requiresApproval));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"test:{name}"),
            id,
            runtime ?? new EchoRuntime(),
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

    private sealed class ImageRuntime : IToolRuntime
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded(
                "captured",
                contentItems: [new TextContent("captured"), new DataContent(Png, "image/png")]));
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
