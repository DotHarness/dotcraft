using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class DotNetExtensionTrustTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"dotcraft_plugin_trust_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ResolveStatus_DistinguishesUntrustedTrustedAndModified()
    {
        var config = new AppConfig();
        var trust = new DotNetExtensionTrust(config, new MemoryStore());

        Assert.Equal(DotNetExtensionTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));

        Assert.True(trust.Grant("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, trust.ResolveStatus("SAMPLE", "aaaa"));
        Assert.Equal("aaaa", trust.TrustedFingerprint("Sample"));
        Assert.Equal(DotNetExtensionTrustStatus.Modified, trust.ResolveStatus("sample", "bbbb"));

        Assert.False(trust.Grant("sample", "aaaa"));
        Assert.True(trust.Revoke("sample", "aaaa"));
        Assert.False(trust.Revoke("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));
    }

    [Fact]
    public void Grants_AllowMultipleBundleFingerprintsForOnePluginId()
    {
        var trust = new DotNetExtensionTrust(new AppConfig(), new MemoryStore());

        Assert.True(trust.Grant("sample", "aaaa"));
        Assert.True(trust.Grant("sample", "bbbb"));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, trust.ResolveStatus("sample", "bbbb"));

        Assert.True(trust.Revoke("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Modified, trust.ResolveStatus("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, trust.ResolveStatus("sample", "bbbb"));
    }

    [Fact]
    public void Grant_WritesDedicatedAuthorityWithoutMutatingUserConfiguration()
    {
        var configPath = Path.Combine(_root, "config.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            configPath,
            """
            {
              "Plugins": { "DisabledPlugins": ["other"] },
              "Hooks": { "Enabled": true }
            }
            """);
        var config = new AppConfig { GlobalConfigPath = configPath };

        var originalConfig = File.ReadAllText(configPath);
        new DotNetExtensionTrust(config).Grant("sample", "ffff");

        Assert.Equal(originalConfig, File.ReadAllText(configPath));
        var authorityPath = DotNetExtensionTrustConfigStore.PathForConfig(configPath);
        var root = JsonNode.Parse(File.ReadAllText(authorityPath))!.AsObject();
        Assert.Equal(1, root["version"]!.GetValue<int>());
        Assert.Contains(
            root["grants"]!["sample"]!.AsArray(),
            value => value!.GetValue<string>() == "ffff");

        var reloaded = new DotNetExtensionTrust(new AppConfig { GlobalConfigPath = configPath });
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, reloaded.ResolveStatus("sample", "ffff"));
        Assert.Equal("ffff", reloaded.TrustedFingerprint("sample"));

        reloaded.Revoke("sample", "ffff");
        Assert.Empty(new DotNetExtensionTrustConfigStore(authorityPath).Read());
    }

    [Fact]
    public void ConfigurationCannotPreAuthorizePluginCodeAndGrantsNeedAUserStore()
    {
        var configPath = Path.Combine(_root, "user", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(
            configPath,
            """
            { "Plugins": { "TrustedDotNetExtensions": { "sample": "aaaa" } } }
            """);
        var config = new AppConfig { GlobalConfigPath = configPath };

        var trust = new DotNetExtensionTrust(config);

        Assert.Equal(DotNetExtensionTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.False(File.Exists(DotNetExtensionTrustConfigStore.PathForConfig(configPath)));

        var withoutUserStore = new DotNetExtensionTrust(new AppConfig());
        Assert.Equal(DotNetExtensionTrustStatus.Untrusted, withoutUserStore.ResolveStatus("sample", "aaaa"));
        Assert.Throws<PluginTrustPersistenceException>(() => withoutUserStore.Grant("sample", "aaaa"));
    }

    [Fact]
    public void UnreadableAuthorityFile_LeavesEveryPluginUntrusted()
    {
        var configPath = Path.Combine(_root, "broken.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(configPath, "{ not json");

        var store = new DotNetExtensionTrustConfigStore(configPath);
        Assert.Empty(store.Read());

        Assert.Throws<PluginTrustPersistenceException>(() => store.SetTrusted("sample", "aaaa", isTrusted: true));
        Assert.Equal("{ not json", File.ReadAllText(configPath));
    }

    [Fact]
    public async Task ConcurrentStores_PreserveBothTrustChanges()
    {
        var configPath = Path.Combine(_root, "concurrent.json");
        var first = new DotNetExtensionTrustConfigStore(configPath);
        var second = new DotNetExtensionTrustConfigStore(configPath);

        await Task.WhenAll(
            Task.Run(() => first.SetTrusted("first", "aaaa", isTrusted: true)),
            Task.Run(() => second.SetTrusted("second", "bbbb", isTrusted: true)));

        var grants = first.Read();
        Assert.Contains("aaaa", grants["first"]);
        Assert.Contains("bbbb", grants["second"]);
    }

    [Fact]
    public void ResolversSharingAnAuthorityObserveGrantAndRevoke()
    {
        var configPath = Path.Combine(_root, "shared", "config.json");
        var first = new DotNetExtensionTrust(new AppConfig { GlobalConfigPath = configPath });
        var second = new DotNetExtensionTrust(new AppConfig { GlobalConfigPath = configPath });
        var changed = new List<string>();
        second.Changed += (_, args) => changed.AddRange(args.PluginIds);

        Assert.True(first.Grant("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, second.ResolveStatus("sample", "aaaa"));
        Assert.Contains("sample", changed, StringComparer.OrdinalIgnoreCase);

        Assert.True(second.Revoke("sample", "aaaa"));
        Assert.Equal(DotNetExtensionTrustStatus.Untrusted, first.ResolveStatus("sample", "aaaa"));
    }

    [Fact]
    public async Task ResolverObservesAuthorityWrittenOutsideTheProcessHub()
    {
        var configPath = Path.Combine(_root, "external", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var trust = new DotNetExtensionTrust(new AppConfig { GlobalConfigPath = configPath });
        var changed = new TaskCompletionSource<DotNetExtensionTrustChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        trust.Changed += (_, args) => changed.TrySetResult(args);

        var authorityPath = DotNetExtensionTrustConfigStore.PathForConfig(configPath);
        await File.WriteAllTextAsync(
            authorityPath,
            """
            { "version": 1, "grants": { "sample": ["aaaa"] } }
            """);

        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("sample", notification.PluginIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DotNetExtensionTrustStatus.Trusted, trust.ResolveStatus("sample", "aaaa"));
    }

    private sealed class MemoryStore : IDotNetExtensionTrustStore
    {
        private readonly Dictionary<string, HashSet<string>> _grants = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, IReadOnlySet<string>> Read() => _grants.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        public void SetTrusted(string pluginId, string fingerprint, bool trusted)
        {
            if (!_grants.TryGetValue(pluginId, out var fingerprints))
                _grants[pluginId] = fingerprints = new HashSet<string>(StringComparer.Ordinal);
            if (trusted)
                fingerprints.Add(fingerprint);
            else
                fingerprints.Remove(fingerprint);
            if (fingerprints.Count == 0)
                _grants.Remove(pluginId);
        }
    }
}
