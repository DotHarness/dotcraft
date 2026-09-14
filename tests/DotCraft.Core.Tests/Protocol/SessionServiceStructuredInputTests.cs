using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("steer")]
    public async Task StructuredInput_PreservesIdentityAndSourcesAcrossDeliveryAndReload(string mode)
    {
        var snapshot = StructuredSnapshot();
        var content = await SessionInputPartResolver.ResolveStrictAsync(snapshot.MaterializedInputParts!, default);
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, _, ct) =>
        {
            if (mode == "steer" && call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, content, ct: ct, inputSnapshot: snapshot);
                return HistoryBoundaryClient.Tool();
            }
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, mode == "start" ? content : [new TextContent("start")], inputSnapshot: mode == "start" ? snapshot : null));
        await using var reader = new ThreadStore(_tempDir);
        var saved = await reader.LoadThreadAsync(thread.Id);
        var user = Assert.Single(saved!.Turns.SelectMany(turn => turn.Items).Select(item => item.Payload).OfType<UserMessagePayload>(), payload => payload.ClientUserMessageId == snapshot.ClientUserMessageId);
        Assert.Equal(snapshot.NativeInputParts, user.NativeInputParts);
        Assert.Equal(snapshot.MaterializedInputParts, user.MaterializedInputParts);
        Assert.Equal(4, user.NativeInputParts!.Count);
    }

    [Fact]
    public async Task StructuredQueue_StatusUpdatesAndReloadRetainSubmissionIdentity()
    {
        using var model = new HistoryBoundaryClient((_, _, _) => Task.FromResult<AIContent>(new TextContent("unused")));
        await using var factory = CreateAgentFactory(model);
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        thread.Turns.Add(new SessionTurn { Id = "active", ThreadId = thread.Id, Status = TurnStatus.Running });
        var snapshot = StructuredSnapshot();
        var content = await SessionInputPartResolver.ResolveStrictAsync(snapshot.MaterializedInputParts!, default);
        var queued = await service.EnqueueTurnInputAsync(thread.Id, content, inputSnapshot: snapshot);
        await service.UpdateQueuedTurnInputAsync(thread.Id, queued.Id, "active", "guidancePending");
        await using var reader = new ThreadStore(_tempDir);
        var saved = await reader.LoadThreadAsync(thread.Id);
        var restored = Assert.Single(saved!.QueuedInputs);
        Assert.Equal(snapshot.ClientUserMessageId, restored.ClientUserMessageId);
        Assert.Equal(snapshot.NativeInputParts, restored.NativeInputParts);
        Assert.Equal(snapshot.MaterializedInputParts, restored.MaterializedInputParts);
    }

    private static SessionInputSnapshot StructuredSnapshot()
    {
        SessionInputContext[] contexts = [
            new() { Id = "page", Kind = "pageReference", Url = "https://example.test", Title = "Page", SelectionKind = "text", Text = "Button", Comment = "Explain" },
            new() { Id = "reply", Kind = "responseAnnotation", ThreadId = "source", TurnId = "turn", ItemId = "item", SelectedText = "Quote", Comment = "Clarify" },
            new() { Id = "diff", Kind = "diffAnnotation", Path = "a.cs", Side = "left", StartLine = 3, EndLine = 5, SelectedText = "old code", Comment = "Keep" },
            new() { Id = "paste", Kind = "pastedText", Path = "/attachments/paste.txt", FileName = "paste.txt", Preview = "Preview", CharacterCount = 10000 }
        ];
        return new SessionInputSnapshot
        {
            ClientUserMessageId = Guid.NewGuid().ToString(),
            NativeInputParts = contexts.Select(context => new SessionInputPart { Type = "contextRef", Context = context }).ToArray(),
            MaterializedInputParts = contexts.SelectMany(SessionContextMaterializer.Materialize).ToArray(),
            DisplayText = ""
        };
    }
}
