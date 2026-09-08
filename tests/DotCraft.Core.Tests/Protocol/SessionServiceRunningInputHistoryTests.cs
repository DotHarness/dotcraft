using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task RunningInput_IsDurableBeforeSampling_AndSurvivesNextTurnRestartAndRecovery()
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, messages, ct) =>
        {
            if (call == 1)
            {
                using (TurnTriggerScope.Set(new TurnTriggerInfo { Kind = "test", RefId = "source-1" }))
                    await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("stable guidance")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            await using var reader = new ThreadStore(_tempDir);
            var persisted = await reader.LoadModelHistoryAsync(thread.Id, ct);
            Assert.Single(persisted, message => message.Text.Contains("stable guidance", StringComparison.Ordinal));
            Assert.Single(messages, message => message.Text.Contains("stable guidance", StringComparison.Ordinal));
            var saved = await reader.LoadThreadAsync(thread.Id, ct);
            Assert.Empty(saved!.QueuedInputs);
            return new TextContent("done");
        });
        await using var factory = CreateAgentFactory(model, workspacePath: Path.GetDirectoryName(_tempDir));
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", WorkspacePath = Path.GetDirectoryName(_tempDir)!
        });
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        Assert.Equal(TurnStatus.Completed, Assert.Single(thread.Turns).Status);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        var package = await service.ExportThreadRecoveryAsync(thread.Id);
        await using var coldFactory = CreateAgentFactory(model, workspacePath: Path.GetDirectoryName(_tempDir));
        var cold = CreateService(coldFactory, model, useStreamingFunctionInvoker: true);
        await cold.ResumeThreadAsync(thread.Id);
        await DrainAsync(cold.SubmitInputAsync(thread.Id, [new TextContent("after restart")]));
        Assert.Equal(4, model.Calls);
        await cold.DeleteThreadPermanentlyAsync(thread.Id);
        await cold.RestoreThreadRecoveryAsync(package.PackagePath, thread.Id);
        await cold.ResumeThreadAsync(thread.Id);
        // The exported snapshot deliberately predates the last ordinary Turn.
        await DrainAsync(cold.SubmitInputAsync(thread.Id, [new TextContent("after restore")]));
        Assert.Equal(5, model.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningInput_FailureOrCancellationKeepsOnlyTheExactPersistedSequence(bool cancel)
    {
        SessionService service = null!;
        SessionThread thread = null!;
        using var model = new HistoryBoundaryClient(async (call, _, ct) =>
        {
            if (call == 1)
            {
                await service.SteerTurnAsync(thread.Id, thread.Turns[0].Id, [new TextContent("keep this input")], ct: ct);
                return HistoryBoundaryClient.Tool();
            }
            if (cancel) throw new OperationCanceledException("cancel the probe");
            throw new InvalidOperationException("fail the probe");
        });
        await using var factory = CreateAgentFactory(model);
        service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        Assert.Equal(cancel ? TurnStatus.Cancelled : TurnStatus.Failed, thread.Turns[0].Status);
        await using var reader = new ThreadStore(_tempDir);
        var history = await reader.LoadModelHistoryAsync(thread.Id);
        Assert.Single(history, message => message.Text.Contains("keep this input", StringComparison.Ordinal));
        Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task RunningInput_ColdLoadingReconcilesHistoryWrittenBeforeQueueConfirmation()
    {
        using var model = new HistoryBoundaryClient((_, _, _) => Task.FromResult<AIContent>(new TextContent("unused")));
        await using var factory = CreateAgentFactory(model);
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var queued = await service.EnqueueTurnInputAsync(thread.Id, [new TextContent("already incorporated")]);
        thread.Turns.Add(new SessionTurn { Id = "interrupted-turn", Status = TurnStatus.Running });
        thread.QueuedInputs = [queued with
        {
            Status = "guidancePending", ReadyAfterTurnId = "interrupted-turn",
            MaterializedInputParts = [new() { Type = "image", Url = "file:///unavailable-original-image.png" }]
        }];
        await using var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        await persistence.SaveThreadAsync(thread);
        var incorporatedImage = new DataContent(new byte[] { 1, 2, 3 }, "image/png")
        {
            AdditionalProperties = new() { [SessionInputMetadataKeys.LocalImagePath] = "unavailable-original-image.png" }
        };
        await persistence.AppendModelHistoryAsync(thread.Id,
            [new ChatMessage(ChatRole.User, [new TextContent("already incorporated"), incorporatedImage])
            {
                MessageId = "admitted-item",
                AdditionalProperties = new()
                {
                    ["dotcraft.history.inputs"] = JsonSerializer.SerializeToElement(new[]
                    {
                        new { InputId = queued.Id, ItemId = "admitted-item", TurnId = "interrupted-turn" }
                    })
                }
            }], "interrupted-turn");

        await using var coldFactory = CreateAgentFactory(model);
        var cold = CreateService(coldFactory, model, useStreamingFunctionInvoker: true);
        var restored = await cold.ResumeThreadAsync(thread.Id);
        Assert.Empty(restored.QueuedInputs);
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(restored.Turns).Status);
        Assert.Contains(restored.Turns[0].Items, item => item.Id == "admitted-item" && item.AsUserMessage?.QueuedInputId == queued.Id);
        Assert.Single(Assert.Single(restored.Turns[0].Items, item => item.Id == "admitted-item").AsUserMessage!.Images!);
        Assert.Single(await persistence.LoadModelHistoryAsync(thread.Id));
        Assert.Equal(0, model.Calls);
        await cold.ResumeThreadAsync(thread.Id);
        Assert.Single(await persistence.LoadModelHistoryAsync(thread.Id));
    }

    private sealed class HistoryBoundaryClient(
        Func<int, IReadOnlyList<ChatMessage>, CancellationToken, Task<AIContent>> respond) : IChatClient
    {
        public int Calls { get; private set; }
        public static FunctionCallContent Tool() => new("sleep-call", "clock__Sleep",
            new Dictionary<string, object?> { ["durationMs"] = 1 });
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [await respond(++Calls, messages.ToArray(), cancellationToken)]);
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
