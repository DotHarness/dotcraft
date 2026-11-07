using System.Runtime.CompilerServices;
using System.Text;
using DotCraft.Sessions;
using DotCraft.Memory;
using DotCraft.Persistence;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceObservationTests : SessionServiceRuntimeSignalTestBase
{
    [Fact]
    public async Task RunningObservation_RestoresThePublishedPrefixAndDoesNotPinAfterRelease()
    {
        using var client = new ControlledStreamingClient();
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var published = service.SubscribeThreadAsync(thread.Id, ct: timeout.Token).GetAsyncEnumerator();
        var execution = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("run")], ct: timeout.Token));
        while (await published.MoveNextAsync())
            if (published.Current.DeltaPayload?.TextDelta == "prefix") break;

        using var observation = await service.ObserveThreadAsync(thread.Id, ThreadObservationView.Full, ct: timeout.Token);
        Assert.True(observation.Snapshot.Runtime.Running);
        Assert.Equal("prefix", observation.Snapshot.Turn!.Items.Single(item => item.Type == ItemType.AgentMessage).AsAgentMessage!.Text);
        client.Release.TrySetResult();
        await execution.WaitAsync(timeout.Token);

        var suffix = new StringBuilder();
        await foreach (var evt in observation.Events.WithCancellation(timeout.Token))
        {
            if (evt.DeltaPayload is { } delta) suffix.Append(delta.TextDelta);
            if (evt.EventType == SessionEventType.TurnCompleted) break;
        }
        Assert.Equal("suffix", suffix.ToString());
        Assert.Equal("prefix", observation.Snapshot.Turn.Items.Single(item => item.Type == ItemType.AgentMessage).AsAgentMessage!.Text);
        await published.DisposeAsync();
        observation.Dispose();
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(30)));
        Assert.Null(service.TryGetLoadedThread(thread.Id));
    }

    [Fact]
    public async Task Rollback_ReplacesThePublishedTerminalHeadWithoutReloadingForObservation()
    {
        using var client = new ControlledStreamingClient();
        client.Release.SetResult();
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        using var first = await service.ObserveThreadAsync(thread.Id);
        var firstId = first.Snapshot.Turn!.Id;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        await service.RollbackThreadAsync(thread.Id, 1);
        using var restored = await service.ObserveThreadAsync(thread.Id);
        Assert.Equal(firstId, restored.Snapshot.Turn!.Id);
        Assert.Equal(TurnStatus.Completed, restored.Snapshot.Turn.Status);
        first.Dispose();
        restored.Dispose();
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(30)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ObserveThreadAsync(thread.Id));
        Assert.Null(service.TryGetLoadedThread(thread.Id));
    }

    [Fact]
    public async Task MetadataObservation_RestoresPlanAndContextAfterUnloadAndRefreshesPublishedState()
    {
        using var database = new WorkspaceStateDatabase(_tempDir);
        var plans = new PlanStore(_tempDir, database);
        using var client = new ControlledStreamingClient();
        await using var factory = CreateAgentFactory(client, planStore: plans);
        using var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await plans.SaveStructuredPlanAsync(thread.Id, new StructuredPlan { Title = "saved plan", Content = "Plan content" });
        await using var store = new ThreadStore(_tempDir, database);
        await store.SaveContextUsageTokensAsync(thread.Id, 1234, source: "history_estimate", isEstimate: true);
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(30)));
        await service.ResumeThreadAsync(thread.Id);

        using var observation = await service.ObserveThreadAsync(thread.Id);
        Assert.Equal("saved plan", observation.Snapshot.Plan!.Title);
        Assert.Equal(1234, observation.Snapshot.ContextUsage!.Tokens);
        Assert.True(observation.Snapshot.ContextUsage.IsEstimate);

        await plans.SaveStructuredPlanAsync(thread.Id, new StructuredPlan { Title = "updated plan" });
        var pump = DrainAsync(observation.Events);
        ThreadObservationSnapshot? refreshed = null;
        await observation.RefreshAsync(ThreadObservationView.Metadata, [], snapshot =>
        {
            refreshed = snapshot;
            return Task.CompletedTask;
        });
        Assert.Equal("updated plan", refreshed!.Plan!.Title);
        Assert.Equal(1234, refreshed.ContextUsage!.Tokens);
        observation.Dispose();
        await pump;
    }

    private sealed class ControlledStreamingClient : IChatClient
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "prefix");
            await Release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "suffix");
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
