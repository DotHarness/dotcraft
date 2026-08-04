using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Core.Tests.Protocol;

public sealed class SessionServiceToolInvocationRecorderTests
{
    [Theory]
    [InlineData("core-native", "host", null)]
    [InlineData("sandbox-native", "sandbox", "/workspace")]
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
        var turnRuntime = new TurnRuntime { NextToolItemSequence = () => turn.Items.Count + 1 };
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
}
