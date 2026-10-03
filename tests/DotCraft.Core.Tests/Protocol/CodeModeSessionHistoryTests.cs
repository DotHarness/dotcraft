using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;

namespace DotCraft.Core.Tests.Protocol;

public sealed class CodeModeSessionHistoryTests
{
    [Fact]
    public void RebuiltHistory_ReplaysTheExecCallButNotItsNestedCalls()
    {
        var turn = Turn(
            Call("item_1", "call_exec", "CodeMode", null),
            Call("item_2", "exec-1", "ReadFile", ToolInvocationOrigin.CodeModeKind),
            Result("item_3", "exec-1", "file text", ToolInvocationOrigin.CodeModeKind),
            Result("item_4", "call_exec", "Script completed", null));

        var contents = ThreadStore.BuildModelVisibleHistoryFromTurn(turn)
            .SelectMany(static message => message.Contents)
            .ToArray();

        Assert.Equal("call_exec", Assert.Single(contents.OfType<FunctionCallContent>()).CallId);
        var result = Assert.Single(contents.OfType<FunctionResultContent>());
        Assert.Equal("call_exec", result.CallId);
        Assert.Equal("Script completed", result.Result);
    }

    [Fact]
    public void InvocationOriginAndFreeformCall_ArePersistedButNotProjectedToTheWire()
    {
        var item = Call("item_2", "exec-1", "ReadFile", ToolInvocationOrigin.CodeModeKind);
        item.FreeformCall = true;

        var persisted = JsonSerializer.Serialize(item, SessionJsonOptions.Default);
        var restored = JsonSerializer.Deserialize<SessionItem>(persisted, SessionJsonOptions.Default)!;
        var wire = JsonSerializer.Serialize(item.ToWire(), SessionWireJsonOptions.Default);
        var plain = JsonSerializer.Serialize(Call("item_1", "call_read", "ReadFile", null), SessionJsonOptions.Default);

        Assert.Equal(ToolInvocationOrigin.CodeModeKind, restored.InvocationOrigin);
        Assert.True(restored.FreeformCall);
        Assert.DoesNotContain("invocationOrigin", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("freeformCall", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invocationOrigin", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("freeformCall", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void RebuiltHistory_MarksOnlyCallsRecordedAsFreeform()
    {
        var freeform = Call("item_1", "call_exec", "CodeMode", null);
        freeform.FreeformCall = true;
        var turn = Turn(
            freeform,
            Result("item_2", "call_exec", "Script completed", null),
            Call("item_3", "call_read", "ReadFile", null),
            Result("item_4", "call_read", "file text", null));

        var calls = ThreadStore.BuildModelVisibleHistoryFromTurn(turn)
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ToDictionary(static call => call.CallId);

        Assert.True(ProviderFunctionCallMetadata.IsCustomToolCall(calls["call_exec"]));
        Assert.False(ProviderFunctionCallMetadata.IsCustomToolCall(calls["call_read"]));
    }

    private static SessionTurn Turn(params SessionItem[] items) => new()
    {
        Id = "turn_1",
        ThreadId = "thread_1",
        Status = TurnStatus.Completed,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Items = [.. items]
    };

    private static SessionItem Call(string id, string callId, string toolName, string? origin) => new()
    {
        Id = id,
        TurnId = "turn_1",
        Type = ItemType.ToolCall,
        Status = ItemStatus.Completed,
        InvocationOrigin = origin,
        Payload = new ToolCallPayload
        {
            ToolName = toolName,
            ProviderFlatName = toolName,
            ToolDefinitionId = $"CoreNative:test:{toolName}",
            CallId = callId,
            Arguments = new JsonObject()
        }
    };

    private static SessionItem Result(string id, string callId, string text, string? origin) => new()
    {
        Id = id,
        TurnId = "turn_1",
        Type = ItemType.ToolResult,
        Status = ItemStatus.Completed,
        InvocationOrigin = origin,
        Payload = new ToolResultPayload
        {
            CallId = callId,
            Success = true,
            Result = text
        }
    };
}
