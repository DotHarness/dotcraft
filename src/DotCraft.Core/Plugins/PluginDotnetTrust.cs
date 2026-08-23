using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>Fingerprint-bound trust state of one .NET plugin bundle. Every plugin needs an explicit grant, including built-ins.</summary>
public enum PluginDotnetTrustStatus
{
    /// <summary>No grant exists for this plugin.</summary>
    Untrusted,

    /// <summary>A grant exists and matches the current bundle fingerprint.</summary>
    Trusted,

    /// <summary>A grant exists for a different fingerprint, so the bundle bytes changed.</summary>
    Modified
}

/// <summary>The store a trust grant is written through. A store that throws fails the grant.</summary>
internal interface IPluginDotnetTrustStore
{
    /// <summary>Reads every persisted grant, keyed by canonical plugin id.</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> Read();

    /// <summary>Adds or removes one exact plugin bundle grant.</summary>
    void SetTrusted(string pluginId, string fingerprint, bool trusted);
}

internal interface IPluginDotnetTrustChangeSource
{
    event EventHandler? Changed;
}

internal sealed class PluginDotnetTrustChangedEventArgs(IReadOnlyList<string> pluginIds) : EventArgs
{
    public IReadOnlyList<string> PluginIds { get; } = pluginIds;
}

/// <summary>Indicates that a trust change could not be durably stored.</summary>
internal sealed class PluginTrustPersistenceException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>Resolves and mutates the fingerprint-bound trust that gates .NET plugin activation.</summary>
/// <remarks>Grants come from a machine-local authority beside the global config, never from merged
/// workspace configuration: a repository must not pre-authorize its own in-process code.</remarks>
internal sealed class PluginDotnetTrust
{
    private readonly object _sync = new();
    private readonly Dictionary<string, HashSet<string>> _grants;
    private readonly IPluginDotnetTrustStore? _store;
    private int _localMutation;

    internal event EventHandler<PluginDotnetTrustChangedEventArgs>? Changed;

    /// <summary>Creates a trust resolver over the machine-local trust authority, when configured.</summary>
    public PluginDotnetTrust(AppConfig config, IPluginDotnetTrustStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _store = store ?? PluginDotnetTrustConfigStore.ForConfig(config);
        _grants = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        ReplaceGrants(ReadStore());
        if (_store is IPluginDotnetTrustChangeSource changeSource)
            changeSource.Changed += OnStoreChanged;

    }

    /// <summary>Resolves the trust status of one plugin against its current bundle fingerprint.</summary>
    public PluginDotnetTrustStatus ResolveStatus(string pluginId, string currentFingerprint)
    {
        RefreshFromStore();
        HashSet<string>? granted;
        lock (_sync)
            _grants.TryGetValue(PluginIds.Canonicalize(pluginId), out granted);
        if (granted == null || granted.Count == 0)
            return PluginDotnetTrustStatus.Untrusted;

        return granted.Contains(currentFingerprint)
            ? PluginDotnetTrustStatus.Trusted
            : PluginDotnetTrustStatus.Modified;
    }

    /// <summary>Returns the fingerprint one plugin is trusted at, or <see langword="null"/>.</summary>
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

    /// <summary>Grants trust for one plugin at one bundle fingerprint, persisting before the in-memory
    /// state changes so a store that cannot write leaves the plugin untrusted.</summary>
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

    /// <summary>Revokes one exact plugin bundle's trust grant.</summary>
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

    private IPluginDotnetTrustStore RequireStore() =>
        _store ?? throw new PluginTrustPersistenceException(
            "A machine-local authority path is required to persist .NET plugin trust.");

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

        Changed?.Invoke(this, new PluginDotnetTrustChangedEventArgs(changed));
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
