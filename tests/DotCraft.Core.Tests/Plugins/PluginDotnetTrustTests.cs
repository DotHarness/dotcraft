using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

/// <summary>Covers fingerprint-bound trust resolution and its user-global persistence.</summary>
public sealed class PluginDotnetTrustTests : IDisposable
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
        var trust = new PluginDotnetTrust(config, new MemoryStore());

        Assert.Equal(PluginDotnetTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));

        Assert.True(trust.Grant("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Modified, trust.ResolveStatus("sample", "bbbb"));

        Assert.False(trust.Grant("sample", "aaaa"));
        Assert.True(trust.Revoke("sample", "aaaa"));
        Assert.False(trust.Revoke("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));
    }

    [Fact]
    public void ResolveStatus_TreatsPluginIdsCaseInsensitively()
    {
        var trust = new PluginDotnetTrust(new AppConfig(), new MemoryStore());
        trust.Grant("Sample.Plugin", "aaaa");

        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample.plugin", "aaaa"));
        Assert.Equal("aaaa", trust.TrustedFingerprint("SAMPLE.PLUGIN"));
    }

    [Fact]
    public void Grants_AllowMultipleBundleFingerprintsForOnePluginId()
    {
        var trust = new PluginDotnetTrust(new AppConfig(), new MemoryStore());

        Assert.True(trust.Grant("sample", "aaaa"));
        Assert.True(trust.Grant("sample", "bbbb"));
        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample", "bbbb"));

        Assert.True(trust.Revoke("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Modified, trust.ResolveStatus("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample", "bbbb"));
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
              "Plugins": { "EnabledPlugins": ["sample"] },
              "Hooks": { "Enabled": true }
            }
            """);
        var config = new AppConfig { GlobalConfigPath = configPath };

        var originalConfig = File.ReadAllText(configPath);
        new PluginDotnetTrust(config).Grant("sample", "ffff");

        Assert.Equal(originalConfig, File.ReadAllText(configPath));
        var authorityPath = PluginDotnetTrustConfigStore.PathForConfig(configPath);
        var root = JsonNode.Parse(File.ReadAllText(authorityPath))!.AsObject();
        Assert.Equal(1, root["version"]!.GetValue<int>());
        Assert.Contains(
            root["grants"]!["sample"]!.AsArray(),
            value => value!.GetValue<string>() == "ffff");

        var reloaded = new PluginDotnetTrust(new AppConfig { GlobalConfigPath = configPath });
        Assert.Equal(PluginDotnetTrustStatus.Trusted, reloaded.ResolveStatus("sample", "ffff"));
        Assert.Equal("ffff", reloaded.TrustedFingerprint("sample"));

        reloaded.Revoke("sample", "ffff");
        Assert.Empty(new PluginDotnetTrustConfigStore(authorityPath).Read());
    }

    [Fact]
    public void ConfigurationCannotPreAuthorizePluginCode()
    {
        var configPath = Path.Combine(_root, "user", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(
            configPath,
            """
            { "Plugins": { "TrustedDotnetPlugins": { "sample": "aaaa" } } }
            """);
        var config = new AppConfig { GlobalConfigPath = configPath };

        var trust = new PluginDotnetTrust(config);

        Assert.Equal(PluginDotnetTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.False(File.Exists(PluginDotnetTrustConfigStore.PathForConfig(configPath)));
    }

    [Fact]
    public void WorkspaceGrantAndGrantMutation_AreRejectedWithoutAUserStore()
    {
        var config = new AppConfig();

        var trust = new PluginDotnetTrust(config);

        Assert.Equal(PluginDotnetTrustStatus.Untrusted, trust.ResolveStatus("sample", "aaaa"));
        Assert.Throws<PluginTrustPersistenceException>(() => trust.Grant("sample", "aaaa"));
    }

    [Fact]
    public void UnreadableAuthorityFile_LeavesEveryPluginUntrusted()
    {
        var configPath = Path.Combine(_root, "broken.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(configPath, "{ not json");

        var store = new PluginDotnetTrustConfigStore(configPath);
        Assert.Empty(store.Read());

        Assert.Throws<PluginTrustPersistenceException>(() => store.SetTrusted("sample", "aaaa", isTrusted: true));
        Assert.Equal("{ not json", File.ReadAllText(configPath));
    }

    [Fact]
    public async Task ConcurrentStores_PreserveBothTrustChanges()
    {
        var configPath = Path.Combine(_root, "concurrent.json");
        var first = new PluginDotnetTrustConfigStore(configPath);
        var second = new PluginDotnetTrustConfigStore(configPath);

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
        var first = new PluginDotnetTrust(new AppConfig { GlobalConfigPath = configPath });
        var second = new PluginDotnetTrust(new AppConfig { GlobalConfigPath = configPath });
        var changed = new List<string>();
        second.Changed += (_, args) => changed.AddRange(args.PluginIds);

        Assert.True(first.Grant("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Trusted, second.ResolveStatus("sample", "aaaa"));
        Assert.Contains("sample", changed, StringComparer.OrdinalIgnoreCase);

        Assert.True(second.Revoke("sample", "aaaa"));
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, first.ResolveStatus("sample", "aaaa"));
    }

    [Fact]
    public async Task ResolverObservesAuthorityWrittenOutsideTheProcessHub()
    {
        var configPath = Path.Combine(_root, "external", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var trust = new PluginDotnetTrust(new AppConfig { GlobalConfigPath = configPath });
        var changed = new TaskCompletionSource<PluginDotnetTrustChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        trust.Changed += (_, args) => changed.TrySetResult(args);

        var authorityPath = PluginDotnetTrustConfigStore.PathForConfig(configPath);
        await File.WriteAllTextAsync(
            authorityPath,
            """
            { "version": 1, "grants": { "sample": ["aaaa"] } }
            """);

        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("sample", notification.PluginIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(PluginDotnetTrustStatus.Trusted, trust.ResolveStatus("sample", "aaaa"));
    }

    private sealed class MemoryStore : IPluginDotnetTrustStore
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
