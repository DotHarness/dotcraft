using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Processes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeYieldTests
{
    [Fact]
    public async Task A_steer_yields_a_running_program_and_wait_returns_only_the_rest()
    {
        await using var fixture = await Fixture.CreateAsync();
        var steer = new TaskCompletionSource();
        var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (_, _) => Task.FromResult<IReadOnlyList<ChatMessage>>([]),
            WaitForInstantInterruptAsync = cancellationToken => steer.Task.WaitAsync(cancellationToken)
        });

        var exec = fixture.ExecAsync("text('before');\nawait tools.Gate({});\ntext('after');");
        await fixture.Gate.Started.WaitAsync(TimeSpan.FromSeconds(30));
        steer.SetResult();
        var running = await exec.WaitAsync(TimeSpan.FromSeconds(5));
        guidance.Dispose();
        Assert.StartsWith("Script running with cell ID ", running.Content);
        Assert.Contains("before", running.Content);
        var cellId = running.StructuredContent!.Value.GetProperty("cellId").GetString()!;

        fixture.Gate.Release();
        var done = await fixture.WaitAsync(new JsonObject { ["cell_id"] = cellId });

        Assert.StartsWith("Script completed", done.Content);
        Assert.Contains("after", done.Content);
        Assert.DoesNotContain("before", done.Content);
        Assert.Equal(ToolErrorCodes.NotFound, (await fixture.WaitAsync(new JsonObject { ["cell_id"] = cellId })).Error?.Code);
    }

    [Fact]
    public async Task The_yield_time_returns_a_running_cell_that_terminate_stops()
    {
        await using var fixture = await Fixture.CreateAsync();

        var running = await fixture.ExecAsync("// @exec: {\"yield_time_ms\": 100}\nawait tools.Gate({});");
        Assert.StartsWith("Script running", running.Content);
        await fixture.Gate.Started.WaitAsync(TimeSpan.FromSeconds(30));

        var terminated = await fixture.WaitAsync(new JsonObject
        {
            ["cell_id"] = running.StructuredContent!.Value.GetProperty("cellId").GetString(),
            ["terminate"] = true
        });

        Assert.StartsWith("Script terminated", terminated.Content);
        await fixture.Gate.Cancelled.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task The_end_of_its_turn_terminates_an_open_cell()
    {
        await using var fixture = await Fixture.CreateAsync();
        var running = await fixture.ExecAsync("// @exec: {\"yield_time_ms\": 0}\nawait tools.Gate({});");
        var cellId = running.StructuredContent!.Value.GetProperty("cellId").GetString()!;
        await fixture.Gate.Started.WaitAsync(TimeSpan.FromSeconds(30));

        await new CodeModeThreadObserver(fixture.Host, new CodeModeStore())
            .OnTurnEndedAsync(new TurnLifecycleContext("thread_1", "turn_1"));

        await fixture.Gate.Cancelled.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ToolErrorCodes.NotFound, (await fixture.WaitAsync(new JsonObject { ["cell_id"] = cellId })).Error?.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _workspace;
        private readonly ToolRegistration _exec;
        private readonly ToolRegistration _wait;

        private Fixture(string workspace, CodeModeWorkerHost host, GateRuntime gate, ToolRegistration exec, ToolRegistration wait)
        {
            _workspace = workspace;
            Host = host;
            Gate = gate;
            _exec = exec;
            _wait = wait;
        }

        public CodeModeWorkerHost Host { get; }
        public GateRuntime Gate { get; }

        public static async Task<Fixture> CreateAsync()
        {
            _ = typeof(CommandLineArgs).Assembly;
            var workspace = Directory.CreateTempSubdirectory("codemode-").FullName;
            var gate = new GateRuntime();
            var id = new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("Gate"));
            var registration = new ToolRegistration(
                new ToolDefinition(id, new ToolName(null, "Gate"), "Gate test tool", JsonSerializer.SerializeToElement(new { type = "object" })),
                new ToolRuntimeBinding(new RuntimeBindingId("test:Gate"), id, gate, ToolBindingLeases.AlwaysAvailable, "test", 1),
                ToolProjectionShape.StandardPair);
            var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
            var host = new CodeModeWorkerHost(new ManagedChildProcessFactory(), workspace, new CodeModeLimits());
            var config = new AppConfig();
            config.Tools.CodeMode.Mode = AppConfig.CodeModeSetting.On;
            var finalizer = new CodeModeToolFinalizer(() => config, host, new CodeModeStore(), new ToolDispatcher());
            var planning = new ToolPlanningContext("thread_1", null, workspace, Path.Combine(workspace, ".craft"), "agent", null, null, 1);
            var registrations = (await finalizer.FinalizeAsync(snapshot, planning)).Registrations;
            return new Fixture(
                workspace,
                host,
                gate,
                registrations.Single(static candidate => candidate.Definition.Name.Name == "CodeMode"),
                registrations.Single(static candidate => candidate.Definition.Name.Name == "CodeModeWait"));
        }

        public Task<ToolExecutionResult> ExecAsync(string code) =>
            InvokeAsync(_exec, "call_exec", new JsonObject { ["code"] = code });

        public Task<ToolExecutionResult> WaitAsync(JsonObject arguments) =>
            InvokeAsync(_wait, "call_wait", arguments);

        public ValueTask DisposeAsync() => Host.DisposeAsync();

        private Task<ToolExecutionResult> InvokeAsync(ToolRegistration registration, string callId, JsonObject arguments) =>
            registration.Binding.Runtime.InvokeAsync(
                new ToolInvocationContext(
                    "thread_1", "turn_1", callId, ToolInvocationAudience.Model, registration.Definition.Name,
                    registration.Definition.Id, registration.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: _workspace),
                arguments).AsTask();
    }

    private sealed class GateRuntime : IToolRuntime
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public Task Cancelled => _cancelled.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult();
                throw;
            }
            return ToolExecutionResult.Succeeded("released");
        }
    }
}
