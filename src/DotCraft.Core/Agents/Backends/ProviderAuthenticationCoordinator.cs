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

public sealed class ProviderAuthenticationCoordinator(
    ModelBackendResolver resolver, HttpClient? balanceClient = null, TimeProvider? time = null) : IAsyncDisposable
{
    private static readonly HttpClient DefaultBalanceClient = new();
    private readonly DeepSeekBalanceReader _deepSeekBalance = new(balanceClient ?? DefaultBalanceClient, time ?? TimeProvider.System);
    private const string ChatGptInstanceKey = ModelProviderAuthMethods.ChatGptOAuth;
    private static readonly ProviderAuthenticationMethod ApiKeyMethod = new(ModelProviderAuthMethods.ApiKey, "API key", "apiKey");
    private static readonly ProviderAuthenticationMethod ChatGptMethod = new(ModelProviderAuthMethods.ChatGptOAuth, "ChatGPT", "oauth");
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderLoginSession> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggingOut = new(StringComparer.Ordinal);
    private bool _disposed;
    public event Action<string, ProviderLoginState>? Changed;

    private static IReadOnlyList<ProviderAuthenticationMethod> BuiltInMethods(string protocol) =>
        ModelProviderProtocols.IsOpenAIProtocol(protocol) ? [ApiKeyMethod, ChatGptMethod] : [ApiKeyMethod];

    public ProviderLoginState Login(AppConfig config, string providerId, string? method, string owner,
        CancellationToken ownerStopping = default)
    {
        var runtime = ResolveLocal(config, providerId);
        var selected = SelectMethod(runtime, string.IsNullOrWhiteSpace(method) ? null : method.Trim());
        if (!SupportsLogin(runtime, selected))
            throw new ProviderAuthenticationException("ProviderLoginNotSupported", "This authentication method does not sign in.");
        var authenticator = GetAuthenticator(runtime);
        var key = InstanceKey(runtime, selected.Id);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var finished in _sessions.Where(pair => pair.Value.IsExpired).Select(pair => pair.Key).ToArray())
            {
                _sessions[finished].Dispose();
                _sessions.Remove(finished);
            }
            if (_loggingOut.Contains(key))
                throw new ProviderAuthenticationException("ProviderAuthenticationBusy", "This provider is signing out.");
            if (_sessions.Values.Any(session => session.InstanceKey == key && !session.IsTerminal))
                throw new ProviderAuthenticationException("ProviderLoginInProgress", "A login is already in progress for this provider.");
            var session = new ProviderLoginSession(runtime.ProviderId, key, owner, authenticator, selected.Id,
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
        if (config.ModelService is not null)
            return RequireProvider(config, providerId).RemoteAuthentication ?? new ProviderAuthenticationStatus(false);
        return await GetConfiguredAuthenticator(config, providerId).GetStatusAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool?> TryGetAuthenticatedAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        if (config.ModelService is not null)
            return RequireProvider(config, providerId).RemoteAuthentication?.IsAuthenticated;
        try
        {
            var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
            var method = Methods(runtime).FirstOrDefault(item => item.Id == ConfiguredMethod(runtime));
            if (method is null || !SupportsLogin(runtime, method))
                return null;
            return (await GetAuthenticator(runtime).GetStatusAsync(ct).ConfigureAwait(false)).IsAuthenticated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return string.IsNullOrWhiteSpace(RequireProvider(config, providerId).BackendId) ? null : false;
        }
    }

    public async Task<string?> GetAccessTokenAsync(AppConfig config, string providerId, bool forceRefresh, CancellationToken ct)
    {
        if (config.ModelService is not null)
            return null;
        var runtime = ResolveLocal(config, providerId);
        if (resolver.Resolve(runtime).GetService(typeof(IProviderAccessTokenReader)) is not IProviderAccessTokenReader reader
            || !(await GetConfiguredAuthenticator(config, providerId).GetStatusAsync(ct).ConfigureAwait(false)).IsAuthenticated)
            return null;
        return await reader.TryGetAccessTokenAsync(forceRefresh, ct).ConfigureAwait(false);
    }

    public async Task LogoutAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        var runtime = ResolveLocal(config, providerId);
        var authenticator = GetConfiguredAuthenticator(config, providerId);
        var key = InstanceKey(runtime, ConfiguredMethod(runtime));
        ProviderLoginSession[] active;
        lock (_gate)
        {
            if (!_loggingOut.Add(key))
                throw new ProviderAuthenticationException("ProviderAuthenticationBusy", "This provider is signing out.");
            active = _sessions.Values.Where(session => session.InstanceKey == key && !session.IsTerminal).ToArray();
        }
        try
        {
            foreach (var session in active) session.Cancel();
            await Task.WhenAll(active.Select(session => session.Completion)).WaitAsync(ct).ConfigureAwait(false);
            await authenticator.LogoutAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(runtime.BackendId))
                await resolver.ClearCredentialsAsync(runtime, ct).ConfigureAwait(false);
            resolver.InvalidateAuthentication(runtime);
        }
        finally { lock (_gate) _loggingOut.Remove(key); }
    }

    public async Task<ProviderUsageSnapshot?> ReadUsageAsync(AppConfig config, string providerId, CancellationToken ct)
    {
        if (config.ModelService is not null)
        {
            RequireProvider(config, providerId);
            return null;
        }
        var runtime = resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        if (string.IsNullOrWhiteSpace(runtime.BackendId) && !runtime.IsChatGptOAuth)
        {
            return runtime.WebSearchBackend == ProviderWebSearchBackend.DeepSeek && !runtime.IsRemote
                && !string.IsNullOrWhiteSpace(runtime.ApiKey)
                ? await _deepSeekBalance.ReadAsync(runtime.ApiKey.Trim(), ct).ConfigureAwait(false)
                : null;
        }
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

    private EffectiveModelRuntime ResolveLocal(AppConfig config, string providerId)
    {
        if (config.ModelService is not null)
        {
            RequireProvider(config, providerId);
            throw new ProviderAuthenticationException("ProviderManagedByModelService",
                "Provider sign-in is managed by the model service. Use its configuration or CLI.");
        }
        return resolver.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
    }

    private IProviderAuthenticator GetConfiguredAuthenticator(AppConfig config, string providerId)
    {
        var runtime = ResolveLocal(config, providerId);
        if (string.IsNullOrWhiteSpace(runtime.BackendId) && !runtime.IsChatGptOAuth)
            throw new ProviderAuthenticationException("ProviderLoginNotSupported", "This authentication method does not sign in.");
        return GetAuthenticator(runtime);
    }

    private IReadOnlyList<ProviderAuthenticationMethod> Methods(EffectiveModelRuntime runtime) =>
        string.IsNullOrWhiteSpace(runtime.BackendId)
            ? BuiltInMethods(runtime.Protocol)
            : resolver.GetBackends().Single(item => item.Id == runtime.BackendId).AuthenticationMethods;

    private ProviderAuthenticationMethod SelectMethod(EffectiveModelRuntime runtime, string? method)
    {
        var selected = method ?? ConfiguredMethod(runtime);
        return Methods(runtime).FirstOrDefault(item => item.Id == selected)
            ?? throw new ProviderAuthenticationException("ProviderAuthenticationMethodUnsupported", "The authentication method is not supported.");
    }

    private static string ConfiguredMethod(EffectiveModelRuntime runtime) =>
        string.IsNullOrWhiteSpace(runtime.BackendId) ? ModelProviderAuthMethods.Normalize(runtime.AuthMethod) : runtime.AuthMethod;

    private static bool SupportsLogin(EffectiveModelRuntime runtime, ProviderAuthenticationMethod method) =>
        string.IsNullOrWhiteSpace(runtime.BackendId) ? method.Id == ModelProviderAuthMethods.ChatGptOAuth : method.Kind != "ambient";

    private static AppConfig.ModelProviderConfig RequireProvider(AppConfig config, string providerId) =>
        config.Providers.TryGetValue(providerId, out var provider) ? provider
            : throw new ProviderAuthenticationException("ProviderNotConfigured", $"Model provider '{providerId}' is not configured.");

    private IProviderAuthenticator GetAuthenticator(EffectiveModelRuntime runtime) =>
        resolver.Resolve(runtime).GetService(typeof(IProviderAuthenticator)) as IProviderAuthenticator
        ?? throw new ProviderAuthenticationException("ProviderAuthenticationUnsupported", "This provider does not provide interactive authentication.");

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

    private static string InstanceKey(EffectiveModelRuntime runtime, string method) =>
        string.IsNullOrWhiteSpace(runtime.BackendId) && method == ModelProviderAuthMethods.ChatGptOAuth
            ? ChatGptInstanceKey
            : string.Join('\n', runtime.BackendId, runtime.ProviderId.ToLowerInvariant());
}
