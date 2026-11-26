using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Contributions;

namespace DotCraft.Agents;

public sealed record ModelBackendRegistration(ModelBackendDescriptor Descriptor, string? PluginId);

public sealed class ModelBackendResolver : IModelRuntimeResolver, IDisposable
{
    private const string OpenRouterBackendId = "openrouter";
    private readonly ModelProviderRegistry builtins;
    private readonly RemoteHttpModelProvider remoteHttp;
    private readonly IContributionView contributions;
    private readonly Func<string, string, IProviderCredentialStore> createCredentialStore;
    private readonly ConcurrentDictionary<(string BackendId, string ProviderId), Lazy<IProviderCredentialStore>> _credentialStores = new();
    private readonly IReadOnlyDictionary<string, IModelBackend> officialBackends;
    private readonly List<IContributionHandle> _officialRegistrations = [];
    private readonly ConcurrentDictionary<EffectiveModelRuntime, Lazy<IModelProvider>> _providers = new();
    private readonly ConcurrentDictionary<(string Instance, string Model), ModelCatalogEntry> _pluginMetadata = new();
    private readonly ConcurrentDictionary<string, long> _authenticationRevisions = new(StringComparer.Ordinal);
    private long _chatGptRevision;
    public event Action? Changed;

    public ModelBackendResolver(ModelProviderRegistry builtins, IContributionView contributions, string credentialRoot,
        IEnumerable<IModelBackend>? officialBackends = null,
        Func<string, string, IProviderCredentialStore>? createCredentialStore = null)
    {
        this.builtins = builtins;
        remoteHttp = new RemoteHttpModelProvider(builtins);
        this.contributions = contributions;
        this.createCredentialStore = createCredentialStore
            ?? ((backendId, providerId) => new ProviderCredentialStore(credentialRoot, backendId, providerId));
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
        GetBackendRegistrations().Select(registration => registration.Descriptor).ToArray();

    public IReadOnlyList<ModelBackendRegistration> GetBackendRegistrations() =>
        officialBackends.Values.Select(backend => new ModelBackendRegistration(backend.Descriptor, null))
            .Concat(contributions.ResolveEntries<IModelBackendSource>()
                .Where(entry => !officialBackends.ContainsKey(entry.Contribution.Backend.Descriptor.Id))
                .Select(entry => new ModelBackendRegistration(
                    entry.Contribution.Backend.Descriptor,
                    entry.Origin.Kind == ContributionOriginKind.Plugin ? entry.Origin.Name : null)))
            .OrderBy(registration => registration.Descriptor.Id, StringComparer.Ordinal).ToArray();

    public string? TryGetPluginId(string? backendId)
    {
        if (string.IsNullOrWhiteSpace(backendId) || officialBackends.ContainsKey(backendId))
            return null;
        foreach (var entry in contributions.ResolveEntries<IModelBackendSource>())
        {
            if (entry.Contribution.Backend.Descriptor.Id == backendId)
                return entry.Origin.Kind == ContributionOriginKind.Plugin ? entry.Origin.Name : null;
        }
        return null;
    }

    public EffectiveModelRuntime Normalize(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
        {
            runtime = builtins.Normalize(runtime);
            return runtime.IsChatGptOAuth
                ? runtime with { BackendGeneration = "chatgpt:" + Interlocked.Read(ref _chatGptRevision) }
                : runtime;
        }
        if (IsGenericRemoteBackend(runtime))
            return RemoteHttpModelProvider.Normalize(runtime);
        if (officialBackends.TryGetValue(runtime.BackendId, out var official))
            return ResolveOfficialRuntime(runtime, official).Runtime;
        if (runtime.IsRemote)
            throw new ModelBackendUnavailableException(runtime.BackendId);
        var entry = Find(runtime.BackendId);
        var normalized = NormalizePluginBase(runtime, entry);
        if (string.IsNullOrWhiteSpace(normalized.Model))
            return normalized;
        return NormalizePluginModel(normalized, entry.Contribution.Backend.Descriptor, ResolvePlugin(normalized, entry));
    }

    /// <summary>Resolves a hosted backend and its stopping token from the same generation.</summary>
    public (EffectiveModelRuntime Runtime, IModelProvider Provider, CancellationToken Stopping) ResolveBackend(EffectiveModelRuntime runtime)
    {
        if (runtime.IsRemote || string.IsNullOrWhiteSpace(runtime.BackendId))
            throw new ArgumentException("A local backend provider instance is required.", nameof(runtime));
        if (officialBackends.TryGetValue(runtime.BackendId, out var official))
            return ResolveOfficialRuntime(runtime, official);

        var entry = Find(runtime.BackendId);
        var backend = entry.Contribution.Backend;
        var stopping = backend.Stopping;
        var configured = NormalizePluginBase(runtime, entry);
        var provider = ResolvePlugin(configured, entry);
        return (NormalizePluginModel(configured, backend.Descriptor, provider), provider, stopping);
    }

    private (EffectiveModelRuntime Runtime, IModelProvider Provider, CancellationToken Stopping) ResolveOfficialRuntime(
        EffectiveModelRuntime runtime, IModelBackend backend)
    {
        var configured = runtime with
        {
            IsBuiltInBackend = true,
            ConfiguredProtocol = runtime.ConfiguredProtocol ?? runtime.Protocol,
            ConfiguredEndPoint = runtime.ConfiguredEndPoint ?? runtime.EndPoint,
            AuthMethod = string.IsNullOrWhiteSpace(runtime.AuthMethod)
                ? backend.Descriptor.AuthenticationMethods.FirstOrDefault()?.Id ?? string.Empty : runtime.AuthMethod
        };
        var provider = ResolveOfficial(configured, backend);
        var selected = provider.GetService(typeof(IProviderModelRuntimeResolver)) is IProviderModelRuntimeResolver resolver
            ? resolver.ResolveRuntime(configured) : configured;
        return (selected, provider, backend.Stopping);
    }

    private EffectiveModelRuntime NormalizePluginModel(EffectiveModelRuntime configured, ModelBackendDescriptor descriptor, IModelProvider provider) =>
        !string.IsNullOrWhiteSpace(configured.Model)
        && provider.GetService(typeof(IProviderModelRuntimeResolver)) is IProviderModelRuntimeResolver resolver
            ? ApplyPluginResolvedRuntime(configured, resolver.ResolveRuntime(configured), descriptor)
            : configured;

    public IModelProvider Resolve(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
            return builtins.Resolve(runtime);
        if (IsGenericRemoteBackend(runtime))
            return remoteHttp;
        if (officialBackends.TryGetValue(runtime.BackendId, out var official))
            return ResolveOfficial(runtime, official);
        if (runtime.IsRemote)
            throw new ModelBackendUnavailableException(runtime.BackendId);
        var entry = Find(runtime.BackendId);
        return ResolvePlugin(NormalizePluginBase(runtime, entry), entry);
    }

    private static bool IsGenericRemoteBackend(EffectiveModelRuntime runtime) =>
        runtime.IsRemote && runtime.BackendId != OpenRouterBackendId;

    public CancellationToken GetStopping(EffectiveModelRuntime runtime) =>
        runtime.IsRemote || string.IsNullOrWhiteSpace(runtime.BackendId) ? CancellationToken.None
            : officialBackends.TryGetValue(runtime.BackendId, out var official) ? official.Stopping
            : Find(runtime.BackendId).Contribution.Backend.Stopping;

    private EffectiveModelRuntime NormalizePluginBase(EffectiveModelRuntime runtime, ContributionEntry<IModelBackendSource> entry)
    {
        var descriptor = entry.Contribution.Backend.Descriptor;
        var configured = string.IsNullOrWhiteSpace(runtime.ConfiguredProtocol)
            ? runtime.Protocol
            : runtime.ConfiguredProtocol.Trim().ToLowerInvariant();
        var protocol = string.IsNullOrWhiteSpace(configured) ? descriptor.DefaultProtocol : configured;
        if (!descriptor.Protocols.Contains(protocol, StringComparer.Ordinal))
            throw new ArgumentException($"Backend '{descriptor.Id}' does not support protocol '{protocol}'.");
        var method = string.IsNullOrWhiteSpace(runtime.AuthMethod)
            ? descriptor.AuthenticationMethods.FirstOrDefault()?.Id ?? string.Empty : runtime.AuthMethod;
        if (descriptor.AuthenticationMethods.Count > 0
            && !descriptor.AuthenticationMethods.Any(item => item.Id == method))
            throw new ArgumentException($"Backend '{descriptor.Id}' does not support authentication method '{method}'.");
        return runtime with
        {
            IsBuiltInBackend = false,
            Protocol = protocol,
            ConfiguredProtocol = string.IsNullOrWhiteSpace(runtime.ConfiguredProtocol) ? protocol : configured,
            ConfiguredEndPoint = runtime.ConfiguredEndPoint ?? runtime.EndPoint,
            AuthMethod = method,
            Capabilities = descriptor.Capabilities,
            BackendGeneration = entry.Id.Value + ":" + _authenticationRevisions.GetValueOrDefault(InstanceKey(runtime))
        };
    }

    private IModelProvider ResolvePlugin(EffectiveModelRuntime baseRuntime, ContributionEntry<IModelBackendSource> entry)
    {
        var key = baseRuntime with
        {
            Model = string.Empty,
            ModelMetadata = null,
            RequestAdaptation = null,
            MaxOutputTokens = baseRuntime.ConfiguredMaxOutputTokens,
            Capabilities = entry.Contribution.Backend.Descriptor.Capabilities
        };
        return _providers.GetOrAdd(key, value => new Lazy<IModelProvider>(() =>
        {
            var backend = entry.Contribution.Backend;
            var store = GetCredentials(backend.Descriptor.Id, value.ProviderId);
            return backend.CreateProvider(new ModelBackendContext(value, store, backend.Stopping));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private EffectiveModelRuntime ApplyPluginResolvedRuntime(
        EffectiveModelRuntime baseRuntime, EffectiveModelRuntime resolved, ModelBackendDescriptor descriptor)
    {
        var protocol = string.IsNullOrWhiteSpace(resolved.Protocol) ? baseRuntime.Protocol : resolved.Protocol;
        if (!descriptor.Protocols.Contains(protocol, StringComparer.Ordinal))
            throw new ArgumentException($"Backend '{descriptor.Id}' does not support protocol '{protocol}'.");
        return baseRuntime with
        {
            Protocol = protocol,
            ModelMetadata = InternPluginMetadata(baseRuntime, resolved.ModelMetadata),
            MaxOutputTokens = (baseRuntime.ConfiguredMaxOutputTokens, resolved.MaxOutputTokens) switch
            {
                (int configured, int modelLimit) when configured > 0 && modelLimit > 0 => Math.Min(configured, modelLimit),
                (int configured, _) when configured > 0 => configured,
                (_, int modelLimit) when modelLimit > 0 => modelLimit,
                _ => null
            },
            Capabilities = resolved.Capabilities
        };
    }

    private ModelCatalogEntry? InternPluginMetadata(EffectiveModelRuntime baseRuntime, ModelCatalogEntry? metadata) =>
        metadata is null
            ? null
            : _pluginMetadata.AddOrUpdate(
                (InstanceKey(baseRuntime), baseRuntime.Model),
                metadata,
                (_, existing) => MetadataEquals(existing, metadata) ? existing : metadata);

    private static bool MetadataEquals(ModelCatalogEntry a, ModelCatalogEntry b) =>
        a.Id == b.Id && a.DisplayName == b.DisplayName && a.OwnedBy == b.OwnedBy
        && a.CreatedAt == b.CreatedAt && a.ContextWindow == b.ContextWindow
        && a.MaxOutputTokens == b.MaxOutputTokens && a.DefaultProtocol == b.DefaultProtocol
        && a.Pricing == b.Pricing && ReasoningEquals(a.Reasoning, b.Reasoning)
        && SequenceEquals(a.SupportedProtocols, b.SupportedProtocols)
        && SequenceEquals(a.InputModalities, b.InputModalities)
        && SequenceEquals(a.SpeedModes, b.SpeedModes);

    private static bool ReasoningEquals(ModelReasoningSupport? a, ModelReasoningSupport? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null
            && a.SupportsDisable == b.SupportsDisable && a.DefaultEffort == b.DefaultEffort
            && a.DefaultEnabled == b.DefaultEnabled && a.UpstreamDefaultEffort == b.UpstreamDefaultEffort
            && SequenceEquals(a.SupportedEfforts, b.SupportedEfforts));

    private static bool SequenceEquals<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));

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
            new ModelBackendContext(value, GetCredentials(backend.Descriptor.Id, value.ProviderId), backend.Stopping)))).Value;
    }

    public void InvalidateAuthentication(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
        {
            Interlocked.Increment(ref _chatGptRevision);
            Changed?.Invoke();
            return;
        }
        var instanceKey = InstanceKey(runtime);
        _authenticationRevisions.AddOrUpdate(instanceKey, 1, (_, revision) => revision + 1);
        foreach (var key in _providers.Keys.Where(key => InstanceKey(key) == instanceKey))
            _providers.TryRemove(key, out _);
        foreach (var key in _pluginMetadata.Keys.Where(key => key.Instance == instanceKey))
            _pluginMetadata.TryRemove(key, out _);
        Changed?.Invoke();
    }

    public Task ClearCredentialsAsync(EffectiveModelRuntime runtime, CancellationToken ct) =>
        GetCredentials(runtime.BackendId!, runtime.ProviderId).DeleteAsync(ct);

    private IProviderCredentialStore GetCredentials(string backendId, string providerId) =>
        _credentialStores.GetOrAdd((backendId, providerId.ToLowerInvariant()), key =>
            new Lazy<IProviderCredentialStore>(() => createCredentialStore(key.BackendId, key.ProviderId))).Value;

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
        _pluginMetadata.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (contributions is IContributionRegistry registry) registry.Changed -= OnContributionsChanged;
        foreach (var handle in _officialRegistrations) handle.Dispose();
        _providers.Clear();
        _pluginMetadata.Clear();
        _credentialStores.Clear();
        Changed = null;
    }

    private sealed class OfficialSource(IModelBackend backend) : IModelBackendSource
    {
        public IModelBackend Backend => backend;
    }
}
