using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class SourceControlConfigTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"source_control_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly string _configPath;

    public SourceControlConfigTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
        _configPath = Path.Combine(_workspaceCraftPath, "config.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task SourceControlUpdate_RejectsPasswordsThenPersistsNonSensitivePerforceConfigAndEmitsRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            perforce = new { port = "ssl:p4:1666", password = "super-secret" }
        }));
        var rejected = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(rejected), AppServerErrors.InvalidParamsCode);
        Assert.False(File.Exists(_configPath));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "manual",
            perforce = new
            {
                port = "ssl:perforce.example.com:1666",
                client = "game-main-alice",
                user = "alice",
                charset = "none",
                timeoutSeconds = 45
            }
        }));

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, ConfigChangeRegions.SourceControl);

        var result = SingleResult(sent);
        Assert.Equal("perforce", result.GetProperty("provider").GetString());
        Assert.Equal("perforce", result.GetProperty("effectiveProvider").GetString());
        Assert.Equal("manual", result.GetProperty("connectionMode").GetString());
        Assert.Equal("game-main-alice", result.GetProperty("perforce").GetProperty("client").GetString());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath));
        var sc = doc.RootElement.GetProperty("SourceControl");
        Assert.Equal("perforce", sc.GetProperty("Provider").GetString());
        Assert.Equal("ssl:perforce.example.com:1666", sc.GetProperty("Perforce").GetProperty("Port").GetString());
        Assert.Equal(45, sc.GetProperty("Perforce").GetProperty("TimeoutSeconds").GetInt32());
        Assert.DoesNotContain("super-secret", doc.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceControlUpdate_PatchesPreserveOmittedPerforceFieldsAndNullClearsThem()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);
        await SeedPerforceConfigAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            perforce = new { timeoutSeconds = 60 }
        }));
        var patched = SingleResult(await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5))).GetProperty("perforce");
        Assert.Equal("ssl:p4:1666", patched.GetProperty("port").GetString());
        Assert.Equal("game-main-alice", patched.GetProperty("client").GetString());
        Assert.Equal("alice", patched.GetProperty("user").GetString());
        Assert.Equal(60, patched.GetProperty("timeoutSeconds").GetInt32());
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath)))
        {
            var persisted = doc.RootElement.GetProperty("SourceControl").GetProperty("Perforce");
            Assert.Equal("ssl:p4:1666", persisted.GetProperty("Port").GetString());
            Assert.Equal("game-main-alice", persisted.GetProperty("Client").GetString());
            Assert.Equal("alice", persisted.GetProperty("User").GetString());
            Assert.Equal(60, persisted.GetProperty("TimeoutSeconds").GetInt32());
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "p4config"
        }));
        var modeOnly = SingleResult(await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5)));
        Assert.Equal("p4config", modeOnly.GetProperty("connectionMode").GetString());
        Assert.Equal("ssl:p4:1666", modeOnly.GetProperty("perforce").GetProperty("port").GetString());
        Assert.Equal("game-main-alice", modeOnly.GetProperty("perforce").GetProperty("client").GetString());

        await harness.ExecuteRequestAsync(JsonSerializer.Deserialize<AppServerIncomingMessage>(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "sourceControl/update",
              "params": {
                "provider": "git",
                "perforce": null
              }
            }
            """)!);
        var cleared = SingleResult(await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5)));
        Assert.Equal("git", cleared.GetProperty("provider").GetString());
        Assert.False(cleared.TryGetProperty("perforce", out _));
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath)))
        {
            var perforce = doc.RootElement.GetProperty("SourceControl").GetProperty("Perforce");
            Assert.Equal(string.Empty, perforce.GetProperty("Port").GetString());
            Assert.Equal(string.Empty, perforce.GetProperty("Client").GetString());
            Assert.Equal(string.Empty, perforce.GetProperty("User").GetString());
        }
    }

    [Fact]
    public async Task SourceControlGet_ReflectsPerforceOnlineStateInCapabilitiesWithoutPassword()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await UpdatePerforceAsync(harness, online: true);
        var online = await GetSourceControlAsync(harness);
        Assert.Equal("perforce", online.GetProperty("provider").GetString());
        Assert.Equal("notTested", online.GetProperty("status").GetString());
        Assert.True(online.GetProperty("capabilities").GetProperty("perforceChangelist").GetBoolean());
        Assert.False(online.GetProperty("capabilities").GetProperty("perforceSubmit").GetBoolean());
        Assert.DoesNotContain("assword", online.GetRawText(), StringComparison.OrdinalIgnoreCase);

        await UpdatePerforceAsync(harness, online: false);
        var offline = await GetSourceControlAsync(harness);
        Assert.Equal("perforce", offline.GetProperty("effectiveProvider").GetString());
        Assert.Equal("offline", offline.GetProperty("status").GetString());
        var capabilities = offline.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("gitCommit").GetBoolean());
        Assert.True(capabilities.GetProperty("perforceBinding").GetBoolean());
        Assert.False(capabilities.GetProperty("perforceChangelist").GetBoolean());
        Assert.False(capabilities.GetProperty("perforceShelve").GetBoolean());
        Assert.False(capabilities.GetProperty("perforceSubmit").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlChangelistList, new
        {
            threadId = thread.Id
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task SourceControlThreadTarget_Update_PersistsPerThreadPerforceTargetThatForksDoNotInherit()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlThreadTargetGet, new
        {
            threadId = thread.Id
        }));
        var initial = SingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("default", initial.GetProperty("target").GetProperty("changelist").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlThreadTargetUpdate, new
        {
            threadId = thread.Id,
            target = new { provider = "perforce", changelist = "12345" }
        }));
        var result = SingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("12345", result.GetProperty("target").GetProperty("changelist").GetString());

        var updated = await harness.Service.GetThreadAsync(thread.Id);
        Assert.Equal("perforce", updated.Metadata[ThreadSourceControlMetadata.ProviderKey]);
        Assert.Equal("12345", updated.Metadata[ThreadSourceControlMetadata.PerforceChangelistKey]);

        var fork = await harness.Service.ForkThreadAsync(thread.Id);
        Assert.DoesNotContain(fork.Metadata, kv => kv.Key.StartsWith("sourceControl.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SourceControl_DefaultsToGitAndPreservesUnrelatedConfigKeys()
    {
        await File.WriteAllTextAsync(_configPath, "{\n  \"CustomSettings\": { \"keep\": true }\n}\n");

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var defaults = await GetSourceControlAsync(harness);
        Assert.Equal("git", defaults.GetProperty("provider").GetString());
        Assert.Equal("git", defaults.GetProperty("effectiveProvider").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "git"
        }));
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath));
        Assert.True(doc.RootElement.GetProperty("CustomSettings").GetProperty("keep").GetBoolean());
        Assert.Equal("git", doc.RootElement.GetProperty("SourceControl").GetProperty("Provider").GetString());
    }

    [Fact]
    public async Task SourceControlGet_WithoutWorkspaceCraftPath_ReturnsMethodNotFound()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlGet, new { }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), -32601);
    }

    private static async Task UpdatePerforceAsync(AppServerTestHarness harness, bool online)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "manual",
            perforce = new { port = "ssl:p4:1666", client = "c", user = "u", online }
        }));
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
    }

    private static async Task<JsonElement> GetSourceControlAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlGet, new { }));
        return SingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5))).Clone();
    }

    private static async Task SeedPerforceConfigAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "manual",
            perforce = new
            {
                port = "ssl:p4:1666",
                client = "game-main-alice",
                user = "alice",
                charset = "none",
                p4ConfigName = ".p4config",
                timeoutSeconds = 45
            }
        }));
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
    }

    private static JsonElement SingleResult(IReadOnlyList<JsonDocument> sent)
    {
        var response = sent.Single(d => d.RootElement.TryGetProperty("result", out _));
        return response.RootElement.GetProperty("result");
    }

    private static IDisposable AttachConfigChangedBridge(AppServerTestHarness harness)
    {
        void OnChanged(object? sender, AppConfigChangedEventArgs change)
        {
            if (!harness.Connection.SupportsConfigChange || !harness.Connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged))
                return;

            var notification = new
            {
                jsonrpc = "2.0",
                method = DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged,
                @params = new DotCraft.Protocol.AppServer.ConfigChangedParams
                {
                    Source = change.Source,
                    Regions = change.Regions.ToArray(),
                    ChangedAt = change.ChangedAt
                }
            };
            harness.Transport.WriteMessageAsync(notification).GetAwaiter().GetResult();
        }

        harness.Monitor.Changed += OnChanged;
        return new ActionOnDispose(() => harness.Monitor.Changed -= OnChanged);
    }

    private static void AssertSingleConfigChanged(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        string expectedRegion)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        Assert.Contains(expectedRegion, payload.GetProperty("regions").EnumerateArray().Select(v => v.GetString()));
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
