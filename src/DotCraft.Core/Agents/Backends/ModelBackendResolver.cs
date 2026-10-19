using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Contributions;

namespace DotCraft.Agents;

public sealed class ModelBackendResolver : IModelRuntimeResolver, IDisposable
{
    private readonly ModelProviderRegistry builtins;
    private readonly IContributionView contributions;
    private readonly string credentialRoot;
    private readonly string? _defaultConfigurationSource;
    private readonly ConcurrentDictionary<EffectiveModelRuntime, Lazy<IModelProvider>> _providers = new();
    private readonly ConcurrentDictionary<string, long> _authenticationRevisions = new(StringComparer.Ordinal);
    private long _chatGptRevision;
    public event Action? Changed;

    public ModelBackendResolver(ModelProviderRegistry builtins, IContributionView contributions, string credentialRoot,
        string? defaultConfigurationSource = null)
    {
        this.builtins = builtins;
        this.contributions = contributions;
        this.credentialRoot = credentialRoot;
        _defaultConfigurationSource = defaultConfigurationSource;
        if (contributions is IContributionRegistry registry) registry.Changed += OnContributionsChanged;
    }

    public IReadOnlyList<ModelBackendDescriptor> GetBackends() =>
        contributions.Resolve<IModelBackendSource>().Select(source => source.Backend.Descriptor)
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
        if (runtime.ConfigurationSource is null or "in-memory" && _defaultConfigurationSource is not null)
            runtime = runtime with { ConfigurationSource = _defaultConfigurationSource };
        runtime = runtime with { ConfigurationSource = ProviderCredentialStore.CanonicalSource(runtime.ConfigurationSource ?? "in-memory") };
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
            BackendOwnsRetry = !descriptor.UseHostRetry,
            BackendGeneration = entry.Id.Value + ":" + _authenticationRevisions.GetValueOrDefault(InstanceKey(runtime))
        };
    }

    public IModelProvider Resolve(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.BackendId))
            return builtins.Resolve(runtime);
        var normalized = Normalize(runtime) with { Model = string.Empty };
        return _providers.GetOrAdd(normalized, key => new Lazy<IModelProvider>(() =>
        {
            var backend = Find(key.BackendId!).Contribution.Backend;
            var store = new ProviderCredentialStore(credentialRoot, key.BackendId!, key.ProviderId, key.ConfigurationSource ?? "in-memory");
            return backend.CreateProvider(new ModelBackendContext(key, store, backend.Stopping));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public CancellationToken GetStopping(EffectiveModelRuntime runtime) =>
        string.IsNullOrWhiteSpace(runtime.BackendId) ? CancellationToken.None
            : Find(runtime.BackendId).Contribution.Backend.Stopping;

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
        new ProviderCredentialStore(credentialRoot, runtime.BackendId!, runtime.ProviderId,
            runtime.ConfigurationSource ?? "in-memory").DeleteAsync(ct);

    private ContributionEntry<IModelBackendSource> Find(string id)
    {
        var entries = contributions.ResolveEntries<IModelBackendSource>()
            .Where(entry => entry.Contribution.Backend.Descriptor.Id == id).ToArray();
        return entries.Length switch
        {
            1 => entries[0],
            0 => throw new ModelBackendUnavailableException(id),
            _ => throw new InvalidOperationException($"Multiple model backends are registered as '{id}'.")
        };
    }

    private static string InstanceKey(EffectiveModelRuntime runtime) =>
        string.Join('\n', runtime.BackendId, runtime.ProviderId.ToLowerInvariant(), runtime.ConfigurationSource);

    private void OnContributionsChanged(object? sender, ContributionsChangedEventArgs args)
    {
        if (!args.Includes<IModelBackendSource>()) return;
        _providers.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (contributions is IContributionRegistry registry) registry.Changed -= OnContributionsChanged;
        _providers.Clear();
        Changed = null;
    }
}
