using DotCraft.Contributions;
using DotCraft.Context;
using DotCraft.Runtime;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginContributionRegistrarTests
{
    [Fact]
    public void Commit_PublishesAStagedAdapterAndRevokeRemovesIt()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out _);
        registrar.Add<IChatContextProvider>(new StaticChatContext("plugin"));

        Assert.Empty(registry.Resolve<IChatContextProvider>());

        registrar.Commit();
        Assert.Equal("plugin", Assert.Single(registry.Resolve<IChatContextProvider>()).GetSystemPromptSection());

        registrar.Revoke();
        Assert.Empty(registry.Resolve<IChatContextProvider>());
    }

    [Fact]
    public async Task Commit_RejectsARegistrationThatIsStillBeingPrepared()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out _);
        var sink = new BlockingNameTraceSink();
        var add = Task.Run(() => registrar.Add<ITraceSink>(sink));
        Assert.True(sink.NameEntered.Wait(TimeSpan.FromSeconds(5)));

        var error = Assert.Throws<InvalidOperationException>(registrar.Commit);
        Assert.Contains("finish before activation returns", error.Message, StringComparison.Ordinal);
        Assert.Empty(registry.Resolve<ITraceSink>());

        sink.ReleaseName.Set();
        await add;
        registrar.Commit();
        Assert.Single(registry.Resolve<ITraceSink>());

        registrar.Revoke();
        await registrar.DisposeTargetsAsync();
    }

    [Fact]
    public async Task AddAfterCommit_IsRejected()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out var calls);
        registrar.Commit();

        var error = Assert.Throws<InvalidOperationException>(() =>
            registrar.Add<IChatContextProvider>(new StaticChatContext("late")));

        Assert.Contains("only during activation", error.Message, StringComparison.Ordinal);
        Assert.Empty(registry.Resolve<IChatContextProvider>());
        registrar.Revoke();
        await calls.CloseAsync();
        Assert.Empty(await registrar.DisposeTargetsAsync());
    }

    [Fact]
    public async Task Close_WaitsForAnAdmittedCallbackBeforeTargetsAreDetached()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out var calls);
        var target = new BlockingChatContext();
        registrar.Add<IChatContextProvider>(target);
        registrar.Commit();
        var adapter = Assert.Single(registry.Resolve<IChatContextProvider>());

        var callback = Task.Run(adapter.GetSystemPromptSection);
        Assert.True(target.Entered.Wait(TimeSpan.FromSeconds(5)));
        registrar.Revoke();
        var drain = calls.CloseAsync();
        Assert.False(drain.IsCompleted);

        target.Release.Set();
        Assert.Equal("done", await callback);
        await drain;
        Assert.False(target.Disposed);

        Assert.Empty(await registrar.DisposeTargetsAsync());
        Assert.True(target.Disposed);
        Assert.Throws<PluginContributionUnavailableException>(adapter.GetSystemPromptSection);
    }

    [Fact]
    public async Task UnknownContracts_AreRejectedBeforeCommit()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out var calls);

        Assert.Throws<InvalidOperationException>(() =>
            registrar.Add<IUnsupportedContribution>(new UnsupportedContribution()));

        registrar.Revoke();
        await calls.CloseAsync();
        await registrar.DisposeTargetsAsync();
        Assert.Empty(registry.Resolve<IUnsupportedContribution>());
    }

    [Fact]
    public async Task ThreadContextItemProvider_IsRejectedBeforeCommit()
    {
        var registry = new ContributionRegistry();
        var registrar = Create(registry, out var calls);

        Assert.Throws<InvalidOperationException>(() =>
            registrar.Add<IThreadSystemPromptContextProvider>(new ThreadContextItemProvider()));

        registrar.Commit();
        Assert.Empty(registry.Resolve<IThreadSystemPromptContextProvider>());
        registrar.Revoke();
        await calls.CloseAsync();
        Assert.Empty(await registrar.DisposeTargetsAsync());
    }

    [Fact]
    public async Task MaterializedChatClient_IsDisposedOnlyOnce()
    {
        var calls = new PluginCallGate();
        var invocation = new PluginInvocation("test.plugin", "generation-1", calls);
        var target = new CountingChatClient();
        var adapter = new PluginChatClient(target, invocation);

        adapter.Dispose();
        Assert.Empty(await invocation.DisposeCapturedTargetsAsync());

        Assert.Equal(1, target.DisposeCount);
    }

    private static PluginContributionRegistrar Create(
        ContributionRegistry registry,
        out PluginCallGate calls)
    {
        calls = new PluginCallGate();
        return new PluginContributionRegistrar(
            registry,
            ContributionOrigin.Plugin("test.plugin", "generation-1"),
            calls,
            new object());
    }

    private sealed class StaticChatContext(string content) : IChatContextProvider
    {
        public string? GetSystemPromptSection() => content;

        public IEnumerable<string> GetRuntimeContextLines() => [];
    }

    private sealed class ThreadContextItemProvider : IThreadSystemPromptContextProvider
    {
        public ContextPageKey ContextPageKey { get; } = new("test", "thread-context", "");

        public ThreadPromptPlacement Placement => ThreadPromptPlacement.ThreadContextItem;

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) => "context";
    }

    private sealed class BlockingChatContext : IChatContextProvider, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public bool Disposed { get; private set; }

        public string? GetSystemPromptSection()
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(5));
            return "done";
        }

        public IEnumerable<string> GetRuntimeContextLines() => [];

        public void Dispose() => Disposed = true;
    }

    private sealed class BlockingNameTraceSink : ITraceSink
    {
        public ManualResetEventSlim NameEntered { get; } = new();

        public ManualResetEventSlim ReleaseName { get; } = new();

        public string Name
        {
            get
            {
                NameEntered.Set();
                ReleaseName.Wait(TimeSpan.FromSeconds(5));
                return "blocking";
            }
        }

        public void Record(TraceEvent evt)
        {
        }
    }

    private interface IUnsupportedContribution : IContributionContract
    {
    }

    private sealed class UnsupportedContribution : IUnsupportedContribution
    {
    }

    private sealed class CountingChatClient : IChatClient
    {
        public int DisposeCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => DisposeCount++;
    }
}
