using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>Trust applies to specific bundle fingerprints, including built-in .NET extensions.</summary>
public enum DotNetExtensionTrustStatus
{
    Untrusted,

    Trusted,

    /// <summary>Grants exist, but none match the current bundle fingerprint.</summary>
    Modified
}

/// <summary>A failed write must throw so the resolver does not apply the change in memory.</summary>
public interface IDotNetExtensionTrustStore
{
    /// <summary>Reads every persisted grant, keyed by canonical plugin id.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> Read();

    void SetTrusted(string pluginId, string fingerprint, bool trusted);
}

/// <summary>Optional notification contract for an embedding host's trust store.</summary>
public interface IDotNetExtensionTrustChangeSource
{
    event EventHandler? Changed;
}

public sealed class DotNetExtensionTrustChangedEventArgs(IReadOnlyList<string> pluginIds) : EventArgs
{
    public IReadOnlyList<string> PluginIds { get; } = pluginIds;
}

public sealed class PluginTrustPersistenceException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>Without an explicit store, grants come from the machine-local authority beside global configuration, not workspace configuration.</summary>
public sealed class DotNetExtensionTrust : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, HashSet<string>> _grants;
    private readonly IDotNetExtensionTrustStore? _store;
    private int _localMutation;

    public event EventHandler<DotNetExtensionTrustChangedEventArgs>? Changed;

    public DotNetExtensionTrust(AppConfig config, IDotNetExtensionTrustStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _store = store ?? DotNetExtensionTrustConfigStore.ForConfig(config);
        _grants = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        ReplaceGrants(ReadStore());
        if (_store is IDotNetExtensionTrustChangeSource changeSource)
            changeSource.Changed += OnStoreChanged;
    }

    public DotNetExtensionTrustStatus ResolveStatus(string pluginId, string currentFingerprint)
    {
        RefreshFromStore();
        HashSet<string>? granted;
        lock (_sync)
            _grants.TryGetValue(PluginIds.Canonicalize(pluginId), out granted);
        if (granted == null || granted.Count == 0)
            return DotNetExtensionTrustStatus.Untrusted;

        return granted.Contains(currentFingerprint)
            ? DotNetExtensionTrustStatus.Trusted
            : DotNetExtensionTrustStatus.Modified;
    }

    /// <summary>Returns one existing grant fingerprint when the plugin has any grants.</summary>
    public string? TrustedFingerprint(string pluginId)
    {
        RefreshFromStore();
        lock (_sync)
        {
            return _grants.TryGetValue(PluginIds.Canonicalize(pluginId), out var fingerprints)
                ? fingerprints.OrderBy(static fingerprint => fingerprint, StringComparer.Ordinal).FirstOrDefault()
                : null;
        }
    }

    /// <summary>Persists a grant before adding it to in-memory trust.</summary>
    public bool Grant(string pluginId, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        RefreshFromStore();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        lock (_sync)
        {
            if (_grants.TryGetValue(canonicalId, out var existing)
                && existing.Contains(fingerprint))
            {
                return false;
            }

            Interlocked.Increment(ref _localMutation);
            try
            {
                RequireStore().SetTrusted(canonicalId, fingerprint, trusted: true);
            }
            finally
            {
                Interlocked.Decrement(ref _localMutation);
            }
            if (!_grants.TryGetValue(canonicalId, out var fingerprints))
                _grants[canonicalId] = fingerprints = new HashSet<string>(StringComparer.Ordinal);
            fingerprints.Add(fingerprint);
            return true;
        }
    }

    public bool Revoke(string pluginId, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        RefreshFromStore();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        lock (_sync)
        {
            if (!_grants.TryGetValue(canonicalId, out var fingerprints)
                || !fingerprints.Contains(fingerprint))
            {
                return false;
            }

            Interlocked.Increment(ref _localMutation);
            try
            {
                RequireStore().SetTrusted(canonicalId, fingerprint, trusted: false);
            }
            finally
            {
                Interlocked.Decrement(ref _localMutation);
            }
            fingerprints.Remove(fingerprint);
            if (fingerprints.Count == 0)
                _grants.Remove(canonicalId);
            return true;
        }
    }

    public static bool RevokePersistedGrants(string globalConfigPath, string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new DotNetExtensionTrustConfigStore(DotNetExtensionTrustConfigStore.PathForConfig(globalConfigPath))
            .RevokeAll(PluginIds.Canonicalize(pluginId));
    }

    private IDotNetExtensionTrustStore RequireStore() =>
        _store ?? throw new PluginTrustPersistenceException(
            "A machine-local authority path is required to persist .NET extension trust.");

    public void Dispose()
    {
        if (_store is IDotNetExtensionTrustChangeSource changeSource)
            changeSource.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _localMutation) == 0)
            RefreshFromStore();
    }

    private void RefreshFromStore()
    {
        if (_store == null)
            return;

        var incoming = ReadStore();
        string[] changed;
        lock (_sync)
        {
            changed = _grants.Keys
                .Concat(incoming.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(id => !_grants.TryGetValue(id, out var previous)
                             || !incoming.TryGetValue(id, out var current)
                             || !previous.SetEquals(current))
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();
            if (changed.Length == 0)
                return;

            ReplaceGrants(incoming);
        }

        Changed?.Invoke(this, new DotNetExtensionTrustChangedEventArgs(changed));
    }

    private Dictionary<string, HashSet<string>> ReadStore()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in _store?.Read()
                     ?? new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(grant.Key))
                continue;
            var fingerprints = grant.Value
                .Where(static fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                .ToHashSet(StringComparer.Ordinal);
            if (fingerprints.Count > 0)
                result[PluginIds.Canonicalize(grant.Key)] = fingerprints;
        }
        return result;
    }

    private void ReplaceGrants(IReadOnlyDictionary<string, HashSet<string>> grants)
    {
        _grants.Clear();
        foreach (var grant in grants)
            _grants[grant.Key] = new HashSet<string>(grant.Value, StringComparer.Ordinal);
    }
}
