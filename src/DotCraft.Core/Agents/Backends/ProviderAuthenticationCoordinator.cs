using DotCraft.Configuration;

namespace DotCraft.Agents;

public sealed record ProviderLoginState(
    string Id, string ProviderId, long Revision, string Status,
    ProviderLoginEvent? Event = null, string? PromptId = null, ProviderLoginPrompt? Prompt = null,
    ProviderAuthenticationStatus? Authentication = null, string? ErrorCode = null, string? ErrorMessage = null);

public sealed class ProviderAuthenticationException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class ProviderAuthenticationCoordinator(ModelBackendResolver resolver) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderLoginSession> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggingOut = new(StringComparer.Ordinal);
    private bool _disposed;
    public event Action<string, ProviderLoginState>? Changed;

    public ProviderLoginState Login(AppConfig config, string providerId, string? method, string owner,
        CancellationToken ownerStopping = default)
    {
        var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        var authenticator = GetAuthenticator(runtime);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var finished in _sessions.Where(pair => pair.Value.IsExpired).Select(pair => pair.Key).ToArray())
            {
                _sessions[finished].Dispose();
                _sessions.Remove(finished);
            }
            if (_loggingOut.Contains(InstanceKey(runtime)))
                throw new ProviderAuthenticationException("ProviderAuthenticationBusy", "This provider is signing out.");
            if (_sessions.Values.Any(session => session.InstanceKey == InstanceKey(runtime) && !session.IsTerminal))
                throw new ProviderAuthenticationException("ProviderLoginInProgress", "A login is already in progress for this provider.");
            var selectedMethod = string.IsNullOrWhiteSpace(method) ? runtime.AuthMethod : method;
            var descriptor = resolver.GetBackends().Single(item => item.Id == runtime.BackendId);
            if (!descriptor.AuthenticationMethods.Any(item => item.Id == selectedMethod))
                throw new ProviderAuthenticationException("ProviderAuthenticationMethodUnsupported", "The authentication method is not supported.");
            var session = new ProviderLoginSession(runtime, owner, authenticator, selectedMethod,
                resolver.GetStopping(runtime), ownerStopping, state => Publish(owner, state),
                () => resolver.InvalidateAuthentication(runtime));
            _sessions.Add(session.State.Id, session);
            session.Start();
            return session.State;
        }
    }

    public ProviderLoginState Get(string id, string owner) => Find(id, owner).State;
    public void Respond(string id, string promptId, string value, string owner) => Find(id, owner).Respond(promptId, value);
    public void Cancel(string id, string owner) => Find(id, owner).Cancel();

    public async Task<ProviderAuthenticationStatus> GetStatusAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        return await GetAuthenticator(runtime).GetStatusAsync(ct).ConfigureAwait(false);
    }

    public async Task LogoutAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        ProviderLoginSession[] active;
        var key = InstanceKey(runtime);
        lock (_gate)
        {
            if (!_loggingOut.Add(key))
                throw new ProviderAuthenticationException("ProviderAuthenticationBusy", "This provider is signing out.");
            active = _sessions.Values.Where(session => session.InstanceKey == InstanceKey(runtime) && !session.IsTerminal).ToArray();
        }
        try
        {
            foreach (var session in active) session.Cancel();
            await Task.WhenAll(active.Select(session => session.Completion)).WaitAsync(ct).ConfigureAwait(false);
            await GetAuthenticator(runtime).LogoutAsync(ct).ConfigureAwait(false);
            await resolver.ClearCredentialsAsync(runtime, ct).ConfigureAwait(false);
            resolver.InvalidateAuthentication(runtime);
        }
        finally { lock (_gate) _loggingOut.Remove(key); }
    }

    public async Task<ProviderUsageSnapshot?> ReadUsageAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        var usage = resolver.Resolve(runtime).GetService(typeof(IProviderUsageReader)) as IProviderUsageReader;
        return usage is null ? null : await usage.ReadAsync(ct).ConfigureAwait(false);
    }

    public void CancelOwner(string owner)
    {
        ProviderLoginSession[] owned;
        lock (_gate) owned = _sessions.Values.Where(session => session.Owner == owner && !session.IsTerminal).ToArray();
        foreach (var session in owned) session.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        ProviderLoginSession[] sessions;
        lock (_gate)
        {
            _disposed = true;
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }
        foreach (var session in sessions) session.Cancel();
        await Task.WhenAll(sessions.Select(session => session.Completion)).ConfigureAwait(false);
        foreach (var session in sessions) session.Dispose();
    }

    private IProviderAuthenticator GetAuthenticator(EffectiveModelRuntime runtime) =>
        resolver.Resolve(runtime).GetService(typeof(IProviderAuthenticator)) as IProviderAuthenticator
        ?? throw new ProviderAuthenticationException("ProviderAuthenticationUnsupported", "This backend does not provide interactive authentication.");

    private ProviderLoginSession Find(string id, string owner)
    {
        lock (_gate)
            return _sessions.TryGetValue(id, out var session) && session.Owner == owner
                ? session : throw new ProviderAuthenticationException("ProviderLoginNotFound", "The login operation is not available.");
    }

    private void Publish(string owner, ProviderLoginState state)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<string, ProviderLoginState>)handler)(owner, state); }
            catch { }
        }
    }

    internal static string InstanceKey(EffectiveModelRuntime runtime) =>
        string.Join('\n', runtime.BackendId, runtime.ProviderId.ToLowerInvariant(), runtime.ConfigurationSource);
}
