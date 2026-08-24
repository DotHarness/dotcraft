using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>Persists fingerprint grants in a dedicated machine-local authority file.</summary>
internal sealed class PluginDotnetTrustConfigStore : IPluginDotnetTrustStore, IPluginDotnetTrustChangeSource
{
    private const string AuthorityFileName = "dotnet-plugin-trust.json";
    private const string GrantsKey = "grants";
    private const string VersionKey = "version";
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(5);

    private readonly string _configPath;
    private readonly string _writeMutexName;
    private readonly ChangeHub _changeHub;

    public PluginDotnetTrustConfigStore(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        _configPath = Path.GetFullPath(configPath);
        var identity = OperatingSystem.IsWindows() ? _configPath.ToUpperInvariant() : _configPath;
        _writeMutexName = $"DotCraft.PluginTrust.{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";
        _changeHub = ChangeHub.For(identity, _configPath);
        _changeHub.Register(this);
    }

    public event EventHandler? Changed;

    /// <summary>Creates a store beside the configured global file, or none when persistence is unavailable.</summary>
    public static PluginDotnetTrustConfigStore? ForConfig(AppConfig config) =>
        string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? null
            : new PluginDotnetTrustConfigStore(PathForConfig(config.GlobalConfigPath!));

    internal static string PathForConfig(string globalConfigPath) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(globalConfigPath))
            ?? throw new ArgumentException("The global config path has no parent directory.", nameof(globalConfigPath)),
            AuthorityFileName);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Read()
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        if (TryLoadRoot() is not { } root
            || FindValue(root, GrantsKey) is not JsonObject grants)
        {
            return result;
        }

        foreach (var entry in grants)
        {
            if (entry.Value is not JsonArray values)
                continue;
            var fingerprints = values
                .Where(static value => value?.GetValueKind() == JsonValueKind.String)
                .Select(static value => value!.GetValue<string>())
                .Where(static fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                .ToHashSet(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(entry.Key) && fingerprints.Count > 0)
                result[entry.Key] = fingerprints;
        }
        return result;
    }

    /// <inheritdoc />
    public void SetTrusted(string pluginId, string fingerprint, bool isTrusted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        using var mutex = new Mutex(initiallyOwned: false, _writeMutexName);
        var acquired = false;
        var saved = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(WriteLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new PluginTrustPersistenceException(
                    "The plugin trust authority is busy; no trust change was written.");
            }

            var root = LoadRootForWrite();
            root[FindKey(root, VersionKey) ?? VersionKey] = 1;
            var grantsKey = FindKey(root, GrantsKey) ?? GrantsKey;
            if (root[grantsKey] is not JsonObject grants)
            {
                grants = new JsonObject();
                root[grantsKey] = grants;
            }

            var existingKey = FindKey(grants, pluginId) ?? pluginId;
            var fingerprints = grants[existingKey] is JsonArray existing
                ? existing
                    .Where(static value => value?.GetValueKind() == JsonValueKind.String)
                    .Select(static value => value!.GetValue<string>())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (isTrusted)
                fingerprints.Add(fingerprint);
            else
                fingerprints.Remove(fingerprint);

            if (fingerprints.Count == 0)
            {
                grants.Remove(existingKey);
            }
            else
            {
                grants[existingKey] = new JsonArray(
                    fingerprints.OrderBy(static value => value, StringComparer.Ordinal)
                        .Select(static value => JsonValue.Create(value))
                        .ToArray());
            }

            WriteAtomic(root);
            saved = true;
        }
        catch (PluginTrustPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginTrustPersistenceException(
                "The plugin trust authority could not be locked safely; no trust change was written.",
                exception);
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }

        if (saved)
            _changeHub.Publish();
    }

    private JsonObject LoadRootForWrite()
    {
        if (!File.Exists(_configPath))
            return new JsonObject();
        try
        {
            return JsonNode.Parse(File.ReadAllText(_configPath)) as JsonObject
                ?? throw new JsonException("The config root is not an object.");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new PluginTrustPersistenceException(
                "The plugin trust authority could not be read safely; no trust change was written.",
                exception);
        }
    }

    private JsonObject? TryLoadRoot()
    {
        if (!File.Exists(_configPath))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(_configPath)) as JsonObject;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteAtomic(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(_configPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
            File.Move(tempPath, _configPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static JsonNode? FindValue(JsonObject parent, string key) =>
        FindKey(parent, key) is { } actualKey ? parent[actualKey] : null;

    private static string? FindKey(JsonObject parent, string key)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }
        return null;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class ChangeHub
    {
        private static readonly object RegistrySync = new();
        private static readonly Dictionary<string, ChangeHub> Registry = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private readonly List<WeakReference<PluginDotnetTrustConfigStore>> _stores = [];
        private readonly string _authorityPath;
        private FileSystemWatcher? _watcher;
        private int _publishScheduled;

        private ChangeHub(string authorityPath)
        {
            _authorityPath = authorityPath;
            EnsureWatcher();
        }

        public static ChangeHub For(string identity, string authorityPath)
        {
            lock (RegistrySync)
            {
                if (!Registry.TryGetValue(identity, out var hub))
                    Registry[identity] = hub = new ChangeHub(authorityPath);
                return hub;
            }
        }

        public void Register(PluginDotnetTrustConfigStore store)
        {
            lock (_sync)
                _stores.Add(new WeakReference<PluginDotnetTrustConfigStore>(store));
        }

        public void Publish()
        {
            EnsureWatcher();
            PluginDotnetTrustConfigStore[] targets;
            lock (_sync)
            {
                targets = _stores
                    .Select(static reference => reference.TryGetTarget(out var target) ? target : null)
                    .Where(static target => target != null)
                    .Cast<PluginDotnetTrustConfigStore>()
                    .ToArray();
                _stores.RemoveAll(static reference => !reference.TryGetTarget(out _));
            }

            foreach (var target in targets)
                target.RaiseChanged();
        }

        private void EnsureWatcher()
        {
            lock (_sync)
            {
                if (_watcher != null)
                    return;
                var directory = Path.GetDirectoryName(_authorityPath);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                _watcher = new FileSystemWatcher(directory, Path.GetFileName(_authorityPath))
                {
                    NotifyFilter = NotifyFilters.FileName
                                   | NotifyFilters.LastWrite
                                   | NotifyFilters.Size
                                   | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnAuthorityFileChanged;
                _watcher.Created += OnAuthorityFileChanged;
                _watcher.Deleted += OnAuthorityFileChanged;
                _watcher.Renamed += OnAuthorityFileChanged;
            }
        }

        private void OnAuthorityFileChanged(object sender, FileSystemEventArgs args)
        {
            if (Interlocked.Exchange(ref _publishScheduled, 1) != 0)
                return;

            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                Volatile.Write(ref _publishScheduled, 0);
                Publish();
            });
        }
    }

}
