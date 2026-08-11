using System.Text.Json;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Configuration;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ImageGenerationPayload = DotCraft.Sessions.ImageGenerationPayload;
using ToolExecutionPayload = DotCraft.Sessions.ToolExecutionPayload;
using UserInputRequestPayload = DotCraft.Sessions.UserInputRequestPayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionWireModelsTests
{
    [Fact]
    public void ToWire_CompletedThread_IncludesStoppedRuntime()
    {
        var thread = BuildThread(TurnStatus.Completed);

        var wire = thread.ToWire();

        Assert.False(wire.Runtime.Running);
        Assert.False(wire.Runtime.WaitingOnApproval);
        Assert.False(wire.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void ToWire_RunningThread_IncludesRunningRuntime()
    {
        var thread = BuildThread(TurnStatus.Running);

        var wire = thread.ToWire();

        Assert.True(wire.Runtime.Running);
        Assert.False(wire.Runtime.WaitingOnApproval);
    }

    [Fact]
    public void ToWire_WaitingApprovalThread_IncludesApprovalRuntime()
    {
        var thread = BuildThread(TurnStatus.WaitingApproval);

        var wire = thread.ToWire();

        Assert.True(wire.Runtime.Running);
        Assert.True(wire.Runtime.WaitingOnApproval);
    }

    [Fact]
    public void ToWire_WaitingInputThread_IncludesUserInputRuntime()
    {
        var thread = BuildThread(TurnStatus.WaitingInput);

        var wire = thread.ToWire();

        Assert.True(wire.Runtime.Running);
        Assert.False(wire.Runtime.WaitingOnApproval);
        Assert.True(wire.Runtime.WaitingOnInput);
    }

    [Fact]
    public void ToWireMethodName_UserInputRequested_ReturnsExpectedMethod()
    {
        var evt = new SessionEvent
        {
            EventId = "e1",
            EventType = SessionEventType.UserInputRequested,
            ThreadId = "thread_1",
            TurnId = "turn_1",
            ItemId = "item_1",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new SessionItem
            {
                Id = "item_1",
                TurnId = "turn_1",
                Type = ItemType.UserInputRequest,
                Status = ItemStatus.Completed,
                Payload = new UserInputRequestPayload { RequestId = "req_1" }
            }
        };

        Assert.Equal("item/tool/requestUserInput", evt.ToWireMethodName());
    }

    [Fact]
    public void ToWireMethodName_ToolCallArgumentsDelta_ReturnsExpectedMethod()
    {
        var evt = new SessionEvent
        {
            EventId = "e1",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_1",
            TurnId = "turn_1",
            ItemId = "item_1",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new ToolCallArgumentsDelta
            {
                ToolName = "WriteFile",
                CallId = "call_1",
                Delta = "{\"content\":\"hello\""
            }
        };

        Assert.Equal("item/toolCall/argumentsDelta", evt.ToWireMethodName());
    }

    [Fact]
    public void ToWire_ToolCallArgumentsDelta_SerializesPayloadShape()
    {
        var evt = new SessionEvent
        {
            EventId = "e1",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_1",
            TurnId = "turn_1",
            ItemId = "item_1",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new ToolCallArgumentsDelta
            {
                ToolName = "WriteFile",
                CallId = "call_1",
                Delta = "{\"content\":\"hello\""
            }
        };

        var wire = evt.ToWire();
        Assert.Equal("toolCallArgumentsDelta", wire.PayloadKind);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(
            wire.Payload, SessionWireJsonOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        Assert.Equal("toolCallArguments", root.GetProperty("deltaKind").GetString());
        Assert.Equal("WriteFile", root.GetProperty("toolName").GetString());
        Assert.Equal("call_1", root.GetProperty("callId").GetString());
        Assert.Equal("{\"content\":\"hello\"", root.GetProperty("delta").GetString());
    }

    [Fact]
    public void SessionWireJsonOptions_ReadsReasoningConfigurationStrings()
    {
        const string json = """
            {
              "id": "thread_1",
              "configuration": {
                "reasoning": {
                  "enabled": true,
                  "effort": "medium",
                  "output": "full"
                }
              }
            }
            """;

        var thread = JsonSerializer.Deserialize<SessionWireThread>(json, SessionWireJsonOptions.Default);

        Assert.NotNull(thread?.Configuration?.Reasoning);
        Assert.True(thread!.Configuration!.Reasoning!.Enabled);
        Assert.Equal(ModelReasoningEffort.Medium, thread.Configuration.Reasoning.Effort);
        Assert.Equal(ReasoningOutput.Full, thread.Configuration.Reasoning.Output);
    }

    [Fact]
    public void ToWire_ToolExecutionItem_SerializesPayloadKindAndShape()
    {
        var item = new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.ToolExecution,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 1, TimeSpan.Zero),
            Payload = new ToolExecutionPayload
            {
                CallId = "call_1",
                ToolName = "WaitAgent",
                Status = "completed",
                Success = true,
                DurationMs = 1000,
                ResultPreview = "agent done"
            }
        };

        var wire = item.ToWire();
        Assert.Equal("toolExecution", wire.PayloadKind);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(
            wire.Payload, SessionWireJsonOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        Assert.Equal("call_1", root.GetProperty("callId").GetString());
        Assert.Equal("WaitAgent", root.GetProperty("toolName").GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1000, root.GetProperty("durationMs").GetInt64());
        Assert.Equal("agent done", root.GetProperty("resultPreview").GetString());
    }

    [Fact]
    public void ToWire_ImageGenerationItem_SerializesPayloadKindAndShape()
    {
        var item = new SessionItem
        {
            Id = "item_1",
            TurnId = "turn_1",
            Type = ItemType.ImageGeneration,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 1, TimeSpan.Zero),
            Payload = new ImageGenerationPayload
            {
                CallId = "ig_1",
                Status = "completed",
                RevisedPrompt = "A red square",
                Result = "AQID",
                MediaType = "image/png",
                SavedPath = "<workspace>/.craft/generated_images/thread_1/ig_1.png"
            }
        };

        var wire = item.ToWire();
        Assert.Equal("imageGeneration", wire.PayloadKind);
        var wireJson = JsonSerializer.Serialize(wire, SessionWireJsonOptions.Default);
        Assert.Contains("\"type\":\"imageGeneration\"", wireJson, StringComparison.Ordinal);
        Assert.Contains("\"payloadKind\":\"imageGeneration\"", wireJson, StringComparison.Ordinal);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(
            wire.Payload, SessionWireJsonOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        Assert.Equal("ig_1", root.GetProperty("callId").GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal("A red square", root.GetProperty("revisedPrompt").GetString());
        Assert.Equal("AQID", root.GetProperty("result").GetString());
        Assert.Equal("image/png", root.GetProperty("mediaType").GetString());
        Assert.Equal("<workspace>/.craft/generated_images/thread_1/ig_1.png", root.GetProperty("savedPath").GetString());
    }

    private static SessionThread BuildThread(TurnStatus turnStatus)
    {
        var completedAt = turnStatus is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled
            ? new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        return new SessionThread
        {
            Id = "thread_1",
            WorkspacePath = "/workspace",
            OriginChannel = "dotcraft-desktop",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 5, 4, 10, 1, 0, TimeSpan.Zero),
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_1",
                    ThreadId = "thread_1",
                    Status = turnStatus,
                    StartedAt = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero),
                    CompletedAt = completedAt
                }
            ]
        };
    }
}
