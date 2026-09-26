using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceSubAgentStartupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "session_startup_" + Guid.NewGuid().ToString("N"));
    private readonly ThreadStore _store;

    public SessionServiceSubAgentStartupTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new ThreadStore(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationOrPreparationFailure_DeletesPersistentAndLoadedChild(bool duringCreation)
    {
        await using var factory = Factory();
        var service = Service(factory);
        var context = await ContextAsync(service);
        string? childId = null;
        var failure = new IOException("failed preparation");
        service.ThreadCreatedForBroadcast = child =>
        {
            childId = child.Id;
            if (duringCreation) throw failure;
        };
        var result = await Assert.ThrowsAsync<IOException>(() => SubAgentSessionControl.SpawnAgentAsync(context,
            new() { AgentPrompt = "work", TaskName = "worker", ChildCreated = (_, _) => throw failure }, false, null, CancellationToken.None));
        Assert.Same(failure, result);
        Assert.False(result.Data.Contains("SubAgentStartupCleanup"));
        Assert.Null(await _store.LoadThreadAsync(childId!));
        Assert.Single(await _store.LoadIndexAsync());
        Assert.Empty(await service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetThreadAsync(childId!));
    }

    [Fact]
    public async Task CancellationAtPersistedTurnStarted_RetainsAdmittedChild()
    {
        await using var factory = Factory();
        var service = Service(factory);
        var context = await ContextAsync(service);
        using var cancellation = new CancellationTokenSource();
        string? childId = null;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (id, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnStarted) cancellation.Cancel();
            if (signal is SessionThreadRuntimeSignal.TurnCancelled or SessionThreadRuntimeSignal.TurnFailed or SessionThreadRuntimeSignal.TurnCompleted)
                completed.TrySetResult();
        };
        try
        {
            await SubAgentSessionControl.SpawnAgentAsync(context, new()
            {
                AgentPrompt = "work", TaskName = "worker", ForkTurns = "none",
                ChildCreated = (child, _) => { childId = child.Id; return Task.CompletedTask; }
            }, true, null, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var child = await service.GetThreadAsync(childId!);
        Assert.Single(child.Turns);
        Assert.NotNull(await _store.LoadThreadAsync(childId!));
        Assert.Single(await service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteThreadPermanentlyAsync(childId!));
    }

    [Fact]
    public async Task InternalCleanup_RejectsUnrelatedParent()
    {
        await using var factory = Factory();
        var service = Service(factory);
        var context = await ContextAsync(service);
        var child = await service.CreateThreadAsync(new SessionIdentity { WorkspacePath = _directory, UserId = "user", ChannelName = "subagent" },
            source: ThreadSource.ForSubAgent(new() { ParentThreadId = context.ParentThread.Id, RootThreadId = context.ParentThread.Id }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((ISubAgentStartupLifecycleService)service)
            .DiscardFailedSubAgentAsync("unrelated", child.Id, CancellationToken.None));
        Assert.NotNull(await _store.LoadThreadAsync(child.Id));
    }

    private SessionService Service(AgentFactory factory) => new(factory, factory.CreateAgentForMode(AgentMode.Agent), new SessionPersistenceService(_store), new SessionGate());

    private AgentFactory Factory() => new(dotcraftPath: _directory, workspacePath: _directory,
        config: AppConfigTestFactory.CreateOpenAI(), memoryStore: new MemoryStore(_directory), skillsLoader: new SkillsLoader(_directory),
        approvalService: new AutoApproveApprovalService(), blacklist: null,
        chatClientRegistry: TestModelProviderRegistry.Create(), toolSources: Array.Empty<IToolSource>());

    private async Task<SubAgentSessionContext> ContextAsync(SessionService service)
    {
        var parent = await service.CreateThreadAsync(new SessionIdentity { WorkspacePath = _directory, UserId = "user", ChannelName = "test" });
        return new() { SessionService = service, ParentThread = parent, ParentTurnId = "turn", RootThreadId = parent.Id };
    }
}
