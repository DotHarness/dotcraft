using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;

namespace DotCraft.Runtime;

/// <summary>Adapters for contribution contracts whose callbacks return longer-lived plugin objects.</summary>
internal sealed class PluginAgentContextSourceAdapter(
    IAgentContextSource target,
    PluginInvocation invocation) : IAgentContextSource
{
    private readonly PluginTarget<IAgentContextSource> _target = invocation.Capture(target);

    public AIContextProvider? CreateProvider(AgentContextRequest request) =>
        invocation.Invoke(() =>
        {
            var provider = _target.Value.CreateProvider(request);
            return provider == null ? null : new PluginAIContextProvider(provider, invocation);
        });
}

internal sealed class PluginAIContextProvider : AIContextProvider
{
    private readonly PluginTarget<AIContextProvider> _target;
    private readonly PluginInvocation _invocation;

    public PluginAIContextProvider(AIContextProvider target, PluginInvocation invocation)
    {
        _target = invocation.Capture(target, ownsTarget: true);
        _invocation = invocation;
    }

    protected override ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken) =>
        _invocation.InvokeAsync(new Func<ValueTask<AIContext>>(async () =>
        {
            var result = await _target.Value.InvokingAsync(context, cancellationToken).ConfigureAwait(false);
            return CopyContext(result, context.AIContext);
        }));

    protected override ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken) =>
        _invocation.InvokeAsync(() => _target.Value.InvokedAsync(context, cancellationToken));

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    private static AIContext CopyContext(AIContext result, AIContext input)
    {
        ArgumentNullException.ThrowIfNull(result);
        var inputMessages = input.Messages?.ToArray() ?? [];
        var inputTools = input.Tools?.ToArray() ?? [];
        var messages = result.Messages?.ToArray() ?? [];
        var tools = result.Tools?.ToArray() ?? [];

        if (messages.Any(message => !inputMessages.Contains(message, ReferenceEqualityComparer.Instance))
            || tools.Any(tool => !inputTools.Contains(tool, ReferenceEqualityComparer.Instance)))
        {
            throw new NotSupportedException(
                "Collectible agent-context plugins may contribute instructions but not new messages or AI tools.");
        }

        return new AIContext
        {
            Instructions = result.Instructions,
            Messages = messages,
            Tools = tools
        };
    }
}

internal sealed class PluginChatMiddlewareAdapter : IChatMiddleware
{
    private readonly PluginTarget<IChatMiddleware> _target;
    private readonly PluginInvocation _invocation;

    public PluginChatMiddlewareAdapter(IChatMiddleware target, PluginInvocation invocation)
    {
        _target = invocation.Capture(target);
        _invocation = invocation;
        Name = invocation.Invoke(() => target.Name);
    }

    public string Name { get; }

    public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
        _invocation.Invoke(() =>
        {
            var client = _target.Value.Wrap(inner, context)
                ?? throw new InvalidOperationException("Plugin chat middleware returned null.");
            return ReferenceEquals(client, inner)
                ? inner
                : new PluginChatClient(client, _invocation);
        });
}

internal sealed class PluginChatClient : IChatClient
{
    private readonly PluginTarget<IChatClient> _target;
    private readonly PluginInvocation _invocation;
    private int _disposed;

    public PluginChatClient(IChatClient target, PluginInvocation invocation)
    {
        _target = invocation.Capture(target, ownsTarget: true);
        _invocation = invocation;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _invocation.InvokeAsync(new Func<Task<ChatResponse>>(async () =>
        {
            var response = await _target.Value
                .GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
            PluginObjectGraphGuard.EnsureHostOwnedGraph(response, "chat response");
            return response;
        }));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = _invocation.Enter();
        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = _target.Value
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception)
        {
            throw _invocation.Normalize(exception);
        }

        try
        {
            while (await MoveNextAsync(enumerator).ConfigureAwait(false))
            {
                var update = enumerator.Current;
                try
                {
                    PluginObjectGraphGuard.EnsureHostOwnedGraph(update, "chat response update");
                }
                catch (Exception exception)
                {
                    throw _invocation.Normalize(exception);
                }
                yield return update;
            }
        }
        finally
        {
            await DisposeEnumeratorAsync(enumerator).ConfigureAwait(false);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
            return this;
        if (serviceType.Assembly.IsCollectible)
            return null;

        return _invocation.Invoke(() =>
        {
            var service = _target.Value.GetService(serviceType, serviceKey);
            PluginObjectGraphGuard.EnsureHostOwnedGraph(service, "chat service");
            return service;
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _invocation.Invoke(() => _target.DisposeAsync().AsTask().GetAwaiter().GetResult());
        }
        catch (PluginContributionUnavailableException)
        {
            // Generation teardown owns the target after admission closes.
        }
    }

    private async Task<bool> MoveNextAsync(IAsyncEnumerator<ChatResponseUpdate> enumerator)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw _invocation.Normalize(exception);
        }
    }

    private async ValueTask DisposeEnumeratorAsync(IAsyncEnumerator<ChatResponseUpdate> enumerator)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw _invocation.Normalize(exception);
        }
    }

}

internal sealed class PluginSubAgentRuntimeSourceAdapter : ISubAgentRuntimeSource
{
    public PluginSubAgentRuntimeSourceAdapter(
        ISubAgentRuntimeSource target,
        PluginInvocation invocation)
    {
        var source = invocation.Capture(target);
        (Runtime, Profiles) = invocation.Invoke(() =>
        {
            var runtime = source.Value.Runtime
                ?? throw new InvalidOperationException("Plugin SubAgent runtime source returned null.");
            var profiles = source.Value.Profiles?.Select(static profile => profile.Clone()).ToArray() ?? [];
            return ((ISubAgentRuntime)new PluginSubAgentRuntime(runtime, invocation),
                (IReadOnlyList<SubAgentProfile>)profiles);
        });
    }

    public ISubAgentRuntime Runtime { get; }

    public IReadOnlyList<SubAgentProfile> Profiles { get; }
}

internal sealed class PluginSubAgentRuntime : ISubAgentRuntime
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly PluginTarget<ISubAgentRuntime> _target;
    private readonly PluginInvocation _invocation;
    private readonly ConcurrentDictionary<Guid, PluginTarget<SubAgentSessionHandle>> _sessions = new();

    public PluginSubAgentRuntime(ISubAgentRuntime target, PluginInvocation invocation)
    {
        _target = invocation.Capture(target);
        _invocation = invocation;
        RuntimeType = invocation.Invoke(() => target.RuntimeType);
    }

    public string RuntimeType { get; }

    public Task<SubAgentSessionHandle> CreateSessionAsync(
        SubAgentProfile profile,
        SubAgentLaunchContext context,
        CancellationToken cancellationToken) =>
        _invocation.InvokeAsync(new Func<Task<SubAgentSessionHandle>>(async () =>
        {
            var raw = await _target.Value
                .CreateSessionAsync(profile.Clone(), context, cancellationToken)
                .ConfigureAwait(false);
            var sessionId = Guid.NewGuid();
            _sessions[sessionId] = _invocation.Capture(raw);
            return new SubAgentSessionHandle(
                RuntimeType,
                raw.ProfileName,
                new PluginSubAgentSessionKey(_ownerId, sessionId));
        }));

    public Task<SubAgentRunResult> RunAsync(
        SubAgentSessionHandle session,
        SubAgentTaskRequest request,
        ISubAgentEventSink sink,
        CancellationToken cancellationToken) =>
        _invocation.InvokeAsync(new Func<Task<SubAgentRunResult>>(async () =>
        {
            var result = await _target.Value
                .RunAsync(Resolve(session).Value, request, sink, cancellationToken)
                .ConfigureAwait(false);
            PluginObjectGraphGuard.EnsureHostOwnedGraph(result, "SubAgent result");
            return result;
        }));

    public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
        _invocation.InvokeAsync(() =>
            _target.Value.CancelAsync(Resolve(session).Value, cancellationToken));

    public async Task DisposeSessionAsync(
        SubAgentSessionHandle session,
        CancellationToken cancellationToken)
    {
        var (sessionId, raw) = ResolveWithId(session);
        await _invocation.InvokeAsync(() =>
            _target.Value.DisposeSessionAsync(raw.Value, cancellationToken)).ConfigureAwait(false);
        if (_sessions.TryRemove(sessionId, out var removed))
            await removed.DisposeAsync().ConfigureAwait(false);
    }

    private PluginTarget<SubAgentSessionHandle> Resolve(SubAgentSessionHandle session) =>
        ResolveWithId(session).Target;

    private (Guid SessionId, PluginTarget<SubAgentSessionHandle> Target) ResolveWithId(
        SubAgentSessionHandle session)
    {
        if (session.State is not PluginSubAgentSessionKey key
            || key.OwnerId != _ownerId
            || !_sessions.TryGetValue(key.SessionId, out var raw))
        {
            throw new InvalidOperationException("The SubAgent session does not belong to this plugin runtime.");
        }
        return (key.SessionId, raw);
    }

    private sealed record PluginSubAgentSessionKey(Guid OwnerId, Guid SessionId);
}
