using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tests;
using DotCraft.Tools;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Core.Tests.Protocol;

public sealed class SessionServiceToolInvocationRecorderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "RecorderTurnDiff_" + Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task RecordTerminalAsync_FileChangeResult_EmitsTurnDiffRightAfterToolResult()
    {
        var events = await RunToolTurnAsync([WriteFile("a.txt")]);

        var diffEvent = Assert.Single(events, evt => evt.EventType == SessionEventType.TurnDiffUpdated);
        Assert.Equal(IndexOfToolResultCompleted(events, "call-1") + 1, events.IndexOf(diffEvent));
        Assert.StartsWith(
            "diff --git a/a.txt b/a.txt\nnew file mode 100644\n",
            diffEvent.TurnDiffUpdatedPayload?.Diff,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordTerminalAsync_RemoteFileChangeAfterEmittedDiff_EmitsOneEmptyTurnDiff()
    {
        var events = await RunToolTurnAsync([WriteFile("a.txt"), WriteFile("b.txt")], remoteCallId: "call-2");

        var diffEvents = events.Where(evt => evt.EventType == SessionEventType.TurnDiffUpdated).ToList();
        Assert.Equal(2, diffEvents.Count);
        Assert.Equal(IndexOfToolResultCompleted(events, "call-1") + 1, events.IndexOf(diffEvents[0]));
        Assert.StartsWith("diff --git a/a.txt b/a.txt\n", diffEvents[0].TurnDiffUpdatedPayload?.Diff, StringComparison.Ordinal);
        Assert.Equal(IndexOfToolResultCompleted(events, "call-2") + 1, events.IndexOf(diffEvents[1]));
        Assert.Equal("", diffEvents[1].TurnDiffUpdatedPayload?.Diff);
    }

    [Fact]
    public async Task RecordTerminalAsync_ResultWithoutStructuredContent_EmitsNoTurnDiff()
    {
        var events = await RunToolTurnAsync([("PlainRead", new Dictionary<string, object?>())]);

        Assert.True(IndexOfToolResultCompleted(events, "call-1") >= 0);
        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnDiffUpdated);
    }

    [Theory]
    [InlineData("core-native", "host", null)]
    public void RegisterCommandExecutionForInvocation_PreregistersExecOnceWithProviderCallId(
        string sourceId,
        string expectedSource,
        string? expectedDefaultWorkingDirectory)
    {
        const string callId = "call-exec-v2";
        const string command = "echo hello";
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dotcraft-exec-v2"));
        var turn = new SessionTurn
        {
            Id = "turn-1",
            ThreadId = "thread-1",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var turnRuntime = new TurnExecutionState { NextToolItemSequence = () => turn.Items.Count + 1 };
        var commandRuntime = new CommandExecutionRuntimeContext
        {
            ThreadId = turn.ThreadId,
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemDelta = (_, _) => { },
            EmitItemCompleted = _ => { },
            SupportsCommandExecutionStreaming = true
        };
        var registration = Registration(sourceId);
        var context = new ToolInvocationContext(
            turn.ThreadId,
            turn.Id,
            callId,
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            1,
            DateTimeOffset.UtcNow,
            WorkspacePath: workspace);

        using var scope = CommandExecutionRuntimeScope.Set(commandRuntime);
        var first = SessionService.RegisterCommandExecutionForInvocation(
            context,
            registration,
            new JsonObject { ["command"] = command },
            turn,
            turnRuntime);
        var duplicate = SessionService.RegisterCommandExecutionForInvocation(
            context,
            registration,
            new JsonObject { ["command"] = command },
            turn,
            turnRuntime);

        var commandItem = Assert.IsType<SessionItem>(first);
        Assert.Null(duplicate);
        Assert.Single(turn.Items);
        var payload = Assert.IsType<CommandExecutionPayload>(commandItem.Payload);
        Assert.Equal(callId, payload.CallId);
        Assert.Equal(command, payload.Command);
        Assert.Equal(expectedSource, payload.Source);
        Assert.Equal(expectedDefaultWorkingDirectory ?? workspace, payload.WorkingDirectory);
        var pendingShell = commandRuntime.TryClaimPendingShellExecution(command, payload.WorkingDirectory);
        Assert.Equal(callId, pendingShell?.CallId);
        var pendingCommand = commandRuntime.TryClaimPending(command, payload.WorkingDirectory);
        Assert.Same(commandItem, pendingCommand?.Item);
    }

    private static ToolRegistration Registration(string sourceId)
    {
        var id = new ToolDefinitionId(
            ToolSourceKind.CoreNative,
            sourceId,
            new SourceToolId("Exec"));
        var definition = new ToolDefinition(
            id,
            new ToolName(null, "Exec"),
            "Execute a command.",
            JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone());
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{sourceId}"),
            id,
            new NoopRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            "authority:test",
            revision: 1);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            ToolExposure.Direct,
            ToolInvocationAudience.Model);
    }

    private sealed class NoopRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private async Task<List<SessionEvent>> RunToolTurnAsync(
        IReadOnlyList<(string Tool, IDictionary<string, object?> Arguments)> calls,
        string? remoteCallId = null)
    {
        var chatClient = new ScriptedToolCallChatClient(calls);
        var router = new ToolInvocationRecorderRouter();
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolDispatcher: new ToolDispatcher(recorder: new RemoteLocationRecorder(router, remoteCallId)),
            toolSources: [new FileToolSource(_tempDir)]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        router.Bind(service);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);

        var events = new List<SessionEvent>();
        await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("go")]))
            events.Add(evt);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        return events;
    }

    private static (string Tool, IDictionary<string, object?> Arguments) WriteFile(string path) =>
        ("WriteFile", new Dictionary<string, object?> { ["path"] = path, ["content"] = "hello\n" });

    private static int IndexOfToolResultCompleted(List<SessionEvent> events, string callId) =>
        events.FindIndex(evt =>
            evt.EventType == SessionEventType.ItemCompleted
            && evt.ItemPayload?.Payload is ToolResultPayload { CallId: var id }
            && id == callId);

    private sealed class FileToolSource(string workspace) : AIFunctionToolSource
    {
        public override string SourceId => "core-native";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return GeneratedToolFunctions.FileTools_WriteFile(new FileTools(workspace));
            yield return AIFunctionFactory.Create(() => "file contents", name: "PlainRead", description: "Read a file.");
        }
    }

    private sealed class RemoteLocationRecorder(IToolInvocationRecorder inner, string? remoteCallId) : IToolInvocationRecorder
    {
        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            inner.RecordStartedAsync(context, registration, arguments, cancellationToken);

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default) =>
            inner.RecordTerminalAsync(
                context.CallId == remoteCallId
                    ? context with { ExecutionLocation = new ToolExecutionLocation("remote", context.WorkspacePath) }
                    : context,
                registration,
                result,
                duration,
                cancellationToken);
    }

    private sealed class ScriptedToolCallChatClient(
        IReadOnlyList<(string Tool, IDictionary<string, object?> Arguments)> calls) : IChatClient
    {
        private int _requestCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("done")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = Interlocked.Increment(ref _requestCount);
            yield return request <= calls.Count
                ? new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent($"call-{request}", calls[request - 1].Tool, calls[request - 1].Arguments)])
                : new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
