using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Contributions;

namespace DotCraft.Agents;

public sealed class ModelBackendResolver : IModelRuntimeResolver, IDisposable
{
    private readonly ModelProviderRegistry builtins;
    private readonly IContributionView contributions;
    private readonly string credentialRoot;
    private readonly IReadOnlyDictionary<string, IModelBackend> officialBackends;
    private readonly List<IContributionHandle> _officialRegistrations = [];
    private readonly ConcurrentDictionary<EffectiveModelRuntime, Lazy<IModelProvider>> _providers = new();
    private readonly ConcurrentDictionary<string, long> _authenticationRevisions = new(StringComparer.Ordinal);
    private long _chatGptRevision;
    public event Action? Changed;

    public ModelBackendResolver(ModelProviderRegistry builtins, IContributionView contributions, string credentialRoot,
        IEnumerable<IModelBackend>? officialBackends = null)
    {
        this.builtins = builtins;
        this.contributions = contributions;
        this.credentialRoot = credentialRoot;
        this.officialBackends = (officialBackends ?? []).ToDictionary(backend => backend.Descriptor.Id, StringComparer.Ordinal);
        if (contributions is IContributionRegistry registry)
        {
            foreach (var backend in this.officialBackends.Values)
                _officialRegistrations.Add(registry.Add<IModelBackendSource>(new OfficialSource(backend)));
            registry.Changed += OnContributionsChanged;
        }
    }

    internal IContributionView Contributions => contributions;

    public IReadOnlyList<ModelBackendDescriptor> GetBackends() =>
        officialBackends.Values.Select(backend => backend.Descriptor)
            .Concat(contributions.Resolve<IModelBackendSource>().Where(source => !officialBackends.ContainsKey(source.Backend.Descriptor.Id)).Select(source => source.Backend.Descriptor))
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal).ToArray();

    public EffectiveModelRuntime Normalize(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
        {
            runtime = builtins.Normalize(runtime);
            return runtime.IsChatGptOAuth
                ? runtime with { BackendGeneration = "chatgpt:" + Interlocked.Read(ref _chatGptRevision) }
                : runtime;
        }
        if (officialBackends.TryGetValue(runtime.BackendId, out var official))
        {
            var configured = runtime with
            {
                IsBuiltInBackend = true,
                ConfiguredProtocol = runtime.ConfiguredProtocol ?? runtime.Protocol,
                ConfiguredEndPoint = runtime.ConfiguredEndPoint ?? runtime.EndPoint,
                AuthMethod = string.IsNullOrWhiteSpace(runtime.AuthMethod) ? official.Descriptor.AuthenticationMethods[0].Id : runtime.AuthMethod
            };
            var provider = ResolveOfficial(configured, official);
            return provider.GetService(typeof(IProviderModelRuntimeResolver)) is IProviderModelRuntimeResolver resolver
                ? resolver.ResolveRuntime(configured) : configured;
        }
        var entry = Find(runtime.BackendId);
        var descriptor = entry.Contribution.Backend.Descriptor;
        var protocol = string.IsNullOrWhiteSpace(runtime.Protocol) ? descriptor.DefaultProtocol : runtime.Protocol;
        if (!descriptor.Protocols.Contains(protocol, StringComparer.Ordinal))
            throw new ArgumentException($"Backend '{descriptor.Id}' does not support protocol '{protocol}'.");
        var method = string.IsNullOrWhiteSpace(runtime.AuthMethod)
            ? descriptor.AuthenticationMethods.FirstOrDefault()?.Id ?? string.Empty : runtime.AuthMethod;
        if (descriptor.AuthenticationMethods.Count > 0
            && !descriptor.AuthenticationMethods.Any(item => item.Id == method))
            throw new ArgumentException($"Backend '{descriptor.Id}' does not support authentication method '{method}'.");
        return runtime with
        {
            Protocol = protocol,
            AuthMethod = method,
            Capabilities = descriptor.Capabilities,
            BackendGeneration = entry.Id.Value + ":" + _authenticationRevisions.GetValueOrDefault(InstanceKey(runtime))
        };
    }

    public IModelProvider Resolve(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
            return builtins.Resolve(runtime);
        if (officialBackends.TryGetValue(runtime.BackendId, out var official))
            return ResolveOfficial(runtime, official);
        var normalized = Normalize(runtime) with { Model = string.Empty };
        return _providers.GetOrAdd(normalized, key => new Lazy<IModelProvider>(() =>
        {
            var backend = Find(key.BackendId!).Contribution.Backend;
            var store = new ProviderCredentialStore(credentialRoot, key.BackendId!, key.ProviderId);
            return backend.CreateProvider(new ModelBackendContext(key, store, backend.Stopping));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public CancellationToken GetStopping(EffectiveModelRuntime runtime) =>
        string.IsNullOrWhiteSpace(runtime.BackendId) ? CancellationToken.None
            : officialBackends.TryGetValue(runtime.BackendId, out var official) ? official.Stopping
            : Find(runtime.BackendId).Contribution.Backend.Stopping;

    private IModelProvider ResolveOfficial(EffectiveModelRuntime runtime, IModelBackend backend)
    {
        var key = runtime with
        {
            Model = string.Empty, ModelMetadata = null, RequestAdaptation = null,
            Protocol = runtime.ConfiguredProtocol ?? runtime.Protocol,
            EndPoint = runtime.ConfiguredEndPoint ?? runtime.EndPoint,
            BackendGeneration = _authenticationRevisions.GetValueOrDefault(InstanceKey(runtime)).ToString(),
            MaxOutputTokens = runtime.ConfiguredMaxOutputTokens, Capabilities = backend.Descriptor.Capabilities
        };
        return _providers.GetOrAdd(key, value => new Lazy<IModelProvider>(() => backend.CreateProvider(
            new ModelBackendContext(value, new ProviderCredentialStore(credentialRoot, backend.Descriptor.Id, value.ProviderId), backend.Stopping)))).Value;
    }

    public void InvalidateAuthentication(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
        {
            Interlocked.Increment(ref _chatGptRevision);
            Changed?.Invoke();
            return;
        }
        _authenticationRevisions.AddOrUpdate(InstanceKey(runtime), 1, (_, revision) => revision + 1);
        foreach (var key in _providers.Keys.Where(key => InstanceKey(key) == InstanceKey(runtime)))
            _providers.TryRemove(key, out _);
        Changed?.Invoke();
    }

    public Task ClearCredentialsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
        new ProviderCredentialStore(credentialRoot, runtime.BackendId!, runtime.ProviderId).DeleteAsync(ct);

    private ContributionEntry<IModelBackendSource> Find(string id)
    {
        foreach (var entry in contributions.ResolveEntries<IModelBackendSource>())
            if (entry.Contribution.Backend.Descriptor.Id == id) return entry;
        throw new ModelBackendUnavailableException(id);
    }

    private static string InstanceKey(EffectiveModelRuntime runtime) =>
        string.Join('\n', runtime.BackendId, runtime.ProviderId.ToLowerInvariant());

    private void OnContributionsChanged(object? sender, ContributionsChangedEventArgs args)
    {
        if (!args.Includes<IModelBackendSource>()) return;
        _providers.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (contributions is IContributionRegistry registry) registry.Changed -= OnContributionsChanged;
        foreach (var handle in _officialRegistrations) handle.Dispose();
        _providers.Clear();
        Changed = null;
    }

    private sealed class OfficialSource(IModelBackend backend) : IModelBackendSource
    {
        public IModelBackend Backend => backend;
    }
}
