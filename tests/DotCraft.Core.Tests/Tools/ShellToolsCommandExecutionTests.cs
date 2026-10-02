using System.Text.Json.Nodes;
using DotCraft.GeneratedTools.Core;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ShellToolsCommandExecutionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "ShellToolsCommandExecution_" + Guid.NewGuid().ToString("N"));

    public ShellToolsCommandExecutionTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Exec_ForbiddenRule_CompletesPendingCommandExecution()
    {
        const string callId = "call_exec_guard";
        const string command = "echo ../outside";
        var turn = CreateTurn();
        var pending = CreatePendingCommandExecution(turn, callId, command, _tempDir);
        var completed = new List<SessionItem>();
        var context = CreateRuntimeContext(turn, completed);
        context.RegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = callId,
            Command = command,
            WorkingDirectory = _tempDir,
            Source = "host",
            Item = pending
        });

        using var _ = CommandExecutionRuntimeScope.Set(context);
        var tools = new ShellTools(
            _tempDir,
            new StubBackgroundTerminalService(),
            policy: new ShellPolicySource([new ShellPrefixRule(["echo"], ShellDecision.Forbidden)], null));

        var result = await tools.Exec(command);

        Assert.StartsWith("Error: Policy forbids", result.Content, StringComparison.Ordinal);
        Assert.Same(pending, Assert.Single(completed));
        Assert.Single(turn.Items);
        Assert.Equal(ItemStatus.Completed, pending.Status);
        Assert.NotNull(pending.CompletedAt);
        var payload = Assert.IsType<CommandExecutionPayload>(pending.Payload);
        Assert.Equal(callId, payload.CallId);
        Assert.Equal("failed", payload.Status);
        Assert.Null(payload.ExitCode);
        Assert.Contains("Policy forbids", payload.AggregatedOutput);
    }

    [Theory]
    [InlineData("shutdown /s /t 60")]
    [InlineData("shutdown.exe /s /t 60")]
    [InlineData("ipconfig /all")]
    [InlineData("cmd /c echo probe")]
    public async Task Exec_WindowsSlashOptions_ReachOnlyTheFakeTerminalUnchanged(string command)
    {
        var terminals = new FakeBackgroundTerminalService("fake-output");
        var tools = new ShellTools(_tempDir, terminals, requireApprovalOutsideWorkspace: false);

        var result = await tools.Exec(command);

        Assert.Contains("fake-output", result.Content);
        Assert.Equal(command, Assert.Single(terminals.StartRequests).Command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mcpApp")]
    [InlineData(ToolInvocationOrigin.CodeModeKind)]
    public async Task Exec_ReturnsTheCommandObjectOnlyToCodeModeCalls(string? originKind)
    {
        var tools = new ShellTools(_tempDir, new FakeBackgroundTerminalService("fake-output", exitCode: 3));
        var runtime = new AIFunctionToolRuntime(GeneratedToolFunctions.ShellTools_Exec(tools));
        var context = new ToolInvocationContext(
            "thread", "turn", "call", ToolInvocationAudience.Model, new ToolName(null, "Exec"),
            new ToolDefinitionId(ToolSourceKind.CoreNative, "core-native", new SourceToolId("Exec")),
            new RuntimeBindingId("exec"), 1, DateTimeOffset.UtcNow,
            originKind is null ? null : new ToolInvocationOrigin(originKind, "call_exec"));

        var result = await runtime.InvokeAsync(context, new JsonObject { ["command"] = "echo ok" });

        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("fake-output", result.Content);
        if (originKind != ToolInvocationOrigin.CodeModeKind)
        {
            Assert.Null(result.StructuredContent);
            return;
        }
        var command = JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;
        Assert.Equal("fake-output", command["output"]!.GetValue<string>());
        Assert.Equal(3, command["exitCode"]!.GetValue<int>());
        Assert.False(command["truncated"]!.GetValue<bool>());
        Assert.Null(command["outputPath"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Exec_BackgroundTerminalService_RecordsExitSeparatelyFromToolSuccess(int exitCode)
    {
        var turn = CreateTurn();
        var completed = new List<SessionItem>();
        var context = CreateRuntimeContext(turn, completed);
        var backgroundTerminals = new FakeBackgroundTerminalService("background-ok", exitCode: exitCode);
        using var _ = CommandExecutionRuntimeScope.Set(context);
        var tools = new ShellTools(_tempDir, backgroundTerminals);

        var result = await tools.Exec("echo ok");

        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("background-ok", result.Content);
        Assert.Single(backgroundTerminals.StartRequests);
        var item = Assert.Single(turn.Items);
        Assert.Same(item, Assert.Single(completed));
        Assert.Equal(ItemType.CommandExecution, item.Type);
        Assert.Equal(ItemStatus.Completed, item.Status);
        var payload = Assert.IsType<CommandExecutionPayload>(item.Payload);
        Assert.Equal("echo ok", payload.Command);
        Assert.Equal(exitCode == 0 ? "completed" : "failed", payload.Status);
        Assert.Equal(exitCode, payload.ExitCode);
        Assert.Contains("background-ok", payload.AggregatedOutput);
    }

    [Fact]
    public async Task Exec_BackgroundTerminalService_ForwardsForegroundOutputDeltaToCommandExecution()
    {
        const string callId = "call_exec_stream";
        const string command = "echo stream";
        var turn = CreateTurn();
        var completed = new List<SessionItem>();
        var deltas = new List<object>();
        var pending = CreatePendingCommandExecution(turn, callId, command, _tempDir);
        var context = CreateRuntimeContext(turn, completed, deltas: deltas);
        context.RegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = callId,
            Command = command,
            WorkingDirectory = _tempDir,
            Source = "host",
            Item = pending
        });
        var backgroundTerminals = new FakeBackgroundTerminalService(
            "stream-final",
            outputDelta: "stream-live" + Environment.NewLine);
        using var _ = CommandExecutionRuntimeScope.Set(context);
        var tools = new ShellTools(_tempDir, backgroundTerminals);

        var result = await tools.Exec(command);

        Assert.Contains("stream-final", result.Content);
        var delta = Assert.IsType<CommandExecutionOutputDelta>(Assert.Single(deltas));
        Assert.Equal("stream-live" + Environment.NewLine, delta.TextDelta);
        Assert.True(delta.MirrorsTerminalOutput);
        Assert.Same(pending, Assert.Single(completed));
    }

    [Fact]
    public async Task Exec_BackgroundTerminalService_PassesPendingShellCallIdWithoutCommandExecutionStreaming()
    {
        const string callId = "call_exec_terminal";
        const string command = "echo terminal";
        var turn = CreateTurn();
        var completed = new List<SessionItem>();
        var context = CreateRuntimeContext(turn, completed, supportsCommandExecutionStreaming: false);
        context.RegisterPendingShellExecution(new PendingShellExecutionRegistration
        {
            CallId = callId,
            Command = command,
            WorkingDirectory = _tempDir,
            Source = "host"
        });
        var backgroundTerminals = new FakeBackgroundTerminalService("terminal-ok");
        using var _ = CommandExecutionRuntimeScope.Set(context);
        var tools = new ShellTools(_tempDir, backgroundTerminals);

        var result = await tools.Exec(command);

        Assert.Contains("terminal-ok", result.Content);
        var request = Assert.Single(backgroundTerminals.StartRequests);
        Assert.Equal(callId, request.CallId);
        Assert.Equal(turn.ThreadId, request.ThreadId);
        Assert.Equal(turn.Id, request.TurnId);
        Assert.Empty(turn.Items);
        Assert.Empty(completed);
    }

    [Fact]
    public async Task Exec_CallerCancellation_PropagatesTokenAndCompletesCommandExecutionAsCancelled()
    {
        var turn = CreateTurn();
        var completed = new List<SessionItem>();
        var context = CreateRuntimeContext(turn, completed);
        CancellationToken observedToken = default;
        var terminals = new StubBackgroundTerminalService
        {
            StartHandler = async (_, token) =>
            {
                observedToken = token;
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Unreachable");
            }
        };
        using var runtimeScope = CommandExecutionRuntimeScope.Set(context);
        using var cts = new CancellationTokenSource();
        var tools = new ShellTools(_tempDir, terminals);

        var execution = tools.Exec("wait forever", cancellationToken: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(cts.Token, observedToken);
        var item = Assert.Single(turn.Items);
        Assert.Same(item, Assert.Single(completed));
        Assert.Equal("cancelled", Assert.IsType<CommandExecutionPayload>(item.Payload).Status);
    }

    private SessionTurn CreateTurn() => new()
    {
        Id = "turn_001",
        ThreadId = "thread_test",
        Status = TurnStatus.Running,
        StartedAt = DateTimeOffset.UtcNow
    };

    private static SessionItem CreatePendingCommandExecution(
        SessionTurn turn,
        string callId,
        string command,
        string workingDirectory)
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = turn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionPayload
            {
                CallId = callId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Source = "host",
                Status = "inProgress",
                AggregatedOutput = string.Empty
            }
        };
        turn.Items.Add(item);
        return item;
    }

    private static CommandExecutionRuntimeContext CreateRuntimeContext(
        SessionTurn turn,
        List<SessionItem> completed,
        bool supportsCommandExecutionStreaming = true,
        List<object>? deltas = null)
    {
        var nextItemSequence = 1;
        return new CommandExecutionRuntimeContext
        {
            ThreadId = turn.ThreadId,
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => nextItemSequence++,
            EmitItemStarted = _ => { },
            EmitItemDelta = (_, delta) => deltas?.Add(delta),
            EmitItemCompleted = completed.Add,
            SupportsCommandExecutionStreaming = supportsCommandExecutionStreaming
        };
    }

    private sealed class FakeBackgroundTerminalService(string output, string? outputDelta = null, int exitCode = 0) : IBackgroundTerminalService
    {
        public event Action<BackgroundTerminalEvent>? TerminalEvent;

        public List<BackgroundTerminalStartRequest> StartRequests { get; } = [];

        public Task<BackgroundTerminalSnapshot> StartAsync(
            BackgroundTerminalStartRequest request,
            CancellationToken ct = default)
        {
            StartRequests.Add(request);
            if (outputDelta != null)
            {
                TerminalEvent?.Invoke(new BackgroundTerminalEvent
                {
                    EventType = "outputDelta",
                    Terminal = new BackgroundTerminalSnapshot
                    {
                        SessionId = "term_test",
                        ThreadId = request.ThreadId,
                        TurnId = request.TurnId,
                        CallId = request.CallId,
                        Command = request.Command,
                        WorkingDirectory = request.WorkingDirectory,
                        Source = request.Source,
                        Status = BackgroundTerminalStatus.Running,
                        Output = outputDelta,
                        OutputPath = Path.Combine(request.WorkingDirectory, "term_test.log"),
                        StartedAt = DateTimeOffset.UtcNow,
                        WallTimeMs = 1,
                        OriginalOutputChars = outputDelta.Length,
                        Truncated = false
                    },
                    Delta = outputDelta
                });
            }
            return Task.FromResult(new BackgroundTerminalSnapshot
            {
                SessionId = "term_test",
                ThreadId = request.ThreadId,
                TurnId = request.TurnId,
                CallId = request.CallId,
                Command = request.Command,
                WorkingDirectory = request.WorkingDirectory,
                Source = request.Source,
                Status = exitCode == 0 ? BackgroundTerminalStatus.Completed : BackgroundTerminalStatus.Failed,
                Output = output,
                OutputPath = Path.Combine(request.WorkingDirectory, "term_test.log"),
                ExitCode = exitCode,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                WallTimeMs = 1,
                OriginalOutputChars = output.Length,
                Truncated = false
            });
        }

        public ShellStdinSession? GetStdinSession(string sessionId) => null;

        public Task<BackgroundTerminalSnapshot> ReadAsync(
            string sessionId,
            int waitMs = 0,
            int? maxOutputChars = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BackgroundTerminalSnapshot> WriteStdinAsync(
            string sessionId,
            string input,
            int yieldTimeMs = 1000,
            int? maxOutputChars = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(
            string? threadId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(
            string threadId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(
            string threadId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
