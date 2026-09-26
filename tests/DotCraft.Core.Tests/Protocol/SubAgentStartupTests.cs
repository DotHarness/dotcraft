using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;
using TestService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentStartupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "startup_" + Guid.NewGuid().ToString("N"));
    private readonly ThreadStore _store;
    private readonly TestService _service;

    public SubAgentStartupTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new ThreadStore(_directory);
        _service = new TestService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("created")]
    [InlineData("fork")]
    [InlineData("persist")]
    [InlineData("edge")]
    [InlineData("submit")]
    public async Task PreparationFailure_RemovesChildAndEdgeAndPreservesOriginalError(string stage)
    {
        var context = await ContextAsync();
        var failure = new IOException(stage);
        string? childId = null;
        var released = false;
        var started = false;
        var options = new SubAgentSpawnOptions
        {
            AgentPrompt = "work", TaskName = "worker", ForkTurns = "all",
            ChildCreated = (child, _) => { childId = child.Id; if (stage == "created") throw failure; return Task.CompletedTask; },
            ChildStarted = (_, _) => { started = true; return Task.CompletedTask; },
            StartupFailed = (_, ct) => { Assert.False(ct.IsCancellationRequested); released = true; return Task.CompletedTask; }
        };
        if (stage == "fork") _service.NativeSubAgentForkMaterializationHandler = (_, _, _, _) => throw failure;
        if (stage == "persist") _service.PersistPreparationHandler = (_, _) => throw failure;
        if (stage == "edge") _service.SpawnEdgeHandler = (_, _) => throw failure;
        if (stage == "submit") _service.SubmitInputHandler = (_, _, _) => throw failure;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => SpawnAsync(context, options)));
        Assert.True(released);
        Assert.False(started);
        Assert.Null(await _store.LoadThreadAsync(childId!));
        Assert.Empty(await _service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        Assert.Single(await _store.LoadIndexAsync());
    }

    [Fact]
    public async Task CancelledPreparation_CleansWithIndependentToken()
    {
        var context = await ContextAsync();
        using var cancellation = new CancellationTokenSource();
        string? childId = null;
        var options = new SubAgentSpawnOptions
        {
            AgentPrompt = "work", TaskName = "worker",
            ChildCreated = (child, ct) => { childId = child.Id; cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            StartupFailed = (_, ct) => { Assert.False(ct.IsCancellationRequested); return Task.CompletedTask; }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SpawnAsync(context, options, cancellation.Token));
        Assert.Null(await _store.LoadThreadAsync(childId!));
        Assert.Empty(await _service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Fact]
    public async Task EmptyInputStream_IsNotSuccessfulAdmission()
    {
        var context = await ContextAsync();
        _service.SubmitInputHandler = (_, _, _) => [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => SpawnAsync(context, new() { AgentPrompt = "work", TaskName = "worker" }));
        Assert.Single(await _store.LoadIndexAsync());
        Assert.Empty(await _service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Fact]
    public async Task ErrorAfterAdmission_KeepsChildAndDoesNotRunStartupCleanup()
    {
        var context = await ContextAsync();
        string? childId = null;
        _service.SubmitEventsHandler = FailAfterAdmission;
        await Assert.ThrowsAsync<IOException>(() => SpawnAsync(context, new()
        {
            AgentPrompt = "work", TaskName = "worker",
            ChildStarted = (child, _) => { childId = child.Id; return Task.CompletedTask; },
            StartupFailed = (_, _) => throw new InvalidOperationException("Must not compensate an admitted child")
        }));
        Assert.NotNull(await _store.LoadThreadAsync(childId!));
        Assert.Single(await _service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Fact]
    public async Task CleanupCallbackFailure_IsDiagnosticAndDoesNotPreventDeletion()
    {
        var context = await ContextAsync();
        var failure = new IOException("start");
        var result = await Assert.ThrowsAsync<IOException>(() => SpawnAsync(context, new()
        {
            AgentPrompt = "work", TaskName = "worker",
            ChildCreated = (_, _) => throw failure,
            StartupFailed = (_, _) => throw new IOException("release failed")
        }));
        Assert.Same(failure, result);
        Assert.Contains("release failed", result.Data["SubAgentStartupCleanup"]?.ToString());
        Assert.Single(await _store.LoadIndexAsync());
    }

    [Fact]
    public async Task ExternalSyntheticPersistenceFailure_DoesNotStartRuntimeOrRetainChild()
    {
        var context = await ContextAsync();
        var runtime = new ExternalRuntime();
        var coordinator = Coordinator(runtime);
        _service.SyntheticStartHandler = (_, _) => throw new IOException("synthetic persistence");
        await Assert.ThrowsAsync<IOException>(() => SubAgentSessionControl.SpawnAgentAsync(context,
            new() { AgentPrompt = "work", TaskName = "worker", ProfileName = "external" }, true, coordinator, CancellationToken.None));
        Assert.False(runtime.Started);
        Assert.Single(await _store.LoadIndexAsync());
        Assert.Empty(await _service.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Fact]
    public async Task ExternalRuntimeFailure_AfterSyntheticAdmissionRetainsHistory()
    {
        var context = await ContextAsync();
        var runtime = new ExternalRuntime();
        var result = await SubAgentSessionControl.SpawnAgentAsync(context,
            new() { AgentPrompt = "work", TaskName = "worker", ProfileName = "external" }, true, Coordinator(runtime), CancellationToken.None);
        Assert.Equal("failed", result.Status);
        Assert.True(runtime.Started);
        var child = await _store.LoadThreadAsync(result.ChildThreadId);
        Assert.Equal(TurnStatus.Failed, Assert.Single(child!.Turns).Status);
    }

    private async Task<SubAgentSessionContext> ContextAsync()
    {
        var parent = await _service.CreateThreadAsync(new SessionIdentity { WorkspacePath = _directory, UserId = "user", ChannelName = "test" });
        return new() { SessionService = _service, ParentThread = parent, ParentTurnId = "turn", RootThreadId = parent.Id };
    }

    private static Task<SubAgentControlResult> SpawnAsync(SubAgentSessionContext context, SubAgentSpawnOptions options, CancellationToken ct = default) =>
        SubAgentSessionControl.SpawnAgentAsync(context, options, true, null, ct);

    private static async IAsyncEnumerable<SessionEvent> FailAfterAdmission(string id, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new() { ThreadId = id, EventType = SessionEventType.TurnStarted };
        await Task.Yield();
        throw new IOException("after admission");
    }

    private SubAgentCoordinator Coordinator(ExternalRuntime runtime) => new(_directory, [runtime],
        [new SubAgentProfile { Name = "external", Runtime = CliOneshotRuntime.RuntimeTypeName, WorkingDirectoryMode = "workspace", Bin = "test", InputMode = "arg", OutputFormat = "text" }]);

    private sealed class ExternalRuntime : ISubAgentRuntime
    {
        public string RuntimeType => CliOneshotRuntime.RuntimeTypeName;
        public bool Started { get; private set; }
        public Task<SubAgentSessionHandle> CreateSessionAsync(SubAgentProfile profile, SubAgentLaunchContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));
        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(SubAgentSessionHandle session, SubAgentTaskRequest request, ISubAgentEventSink sink, CancellationToken cancellationToken)
        { Started = true; throw new IOException("runtime failed"); }
        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
