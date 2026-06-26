using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Conformance tests for the source control binding methods (spec Section 25A):
/// persistence, the <c>sourceControl</c> config-changed region, capability gating,
/// and the no-password guarantee.
/// </summary>
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
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task SourceControlUpdate_Perforce_PersistsNonSensitiveConfigAndEmitsRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
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
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.SourceControlUpdate, ConfigChangeRegions.SourceControl);

        var result = SingleResult(sent);
        Assert.Equal("perforce", result.GetProperty("provider").GetString());
        Assert.Equal("perforce", result.GetProperty("effectiveProvider").GetString());
        Assert.Equal("manual", result.GetProperty("connectionMode").GetString());
        Assert.Equal("game-main-alice", result.GetProperty("perforce").GetProperty("client").GetString());

        var json = await File.ReadAllTextAsync(_configPath);
        using var doc = JsonDocument.Parse(json);
        var sc = doc.RootElement.GetProperty("SourceControl");
        Assert.Equal("perforce", sc.GetProperty("Provider").GetString());
        Assert.Equal("ssl:perforce.example.com:1666", sc.GetProperty("Perforce").GetProperty("Port").GetString());
        Assert.Equal(45, sc.GetProperty("Perforce").GetProperty("TimeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task SourceControlUpdate_RejectsPasswordAndPersistsNothing()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
        {
            provider = "perforce",
            perforce = new { port = "ssl:p4:1666", password = "super-secret" }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
        Assert.False(File.Exists(_configPath));
    }

    [Fact]
    public async Task SourceControlUpdate_PartialPerforcePatch_PreservesOmittedFields()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await SeedPerforceConfigAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
        {
            provider = "perforce",
            perforce = new
            {
                timeoutSeconds = 60
            }
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var result = SingleResult(sent);
        var perforce = result.GetProperty("perforce");
        Assert.Equal("ssl:p4:1666", perforce.GetProperty("port").GetString());
        Assert.Equal("game-main-alice", perforce.GetProperty("client").GetString());
        Assert.Equal("alice", perforce.GetProperty("user").GetString());
        Assert.Equal(60, perforce.GetProperty("timeoutSeconds").GetInt32());

        var json = await File.ReadAllTextAsync(_configPath);
        using var doc = JsonDocument.Parse(json);
        var persisted = doc.RootElement.GetProperty("SourceControl").GetProperty("Perforce");
        Assert.Equal("ssl:p4:1666", persisted.GetProperty("Port").GetString());
        Assert.Equal("game-main-alice", persisted.GetProperty("Client").GetString());
        Assert.Equal("alice", persisted.GetProperty("User").GetString());
        Assert.Equal(60, persisted.GetProperty("TimeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task SourceControlUpdate_NullPerforce_ClearsStaleFields()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await SeedPerforceConfigAsync(harness);

        var req = JsonSerializer.Deserialize<AppServerIncomingMessage>(
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
            """)!;
        await harness.ExecuteRequestAsync(req);
        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var result = SingleResult(sent);
        Assert.Equal("git", result.GetProperty("provider").GetString());
        Assert.False(result.TryGetProperty("perforce", out _));

        var json = await File.ReadAllTextAsync(_configPath);
        using var doc = JsonDocument.Parse(json);
        var perforce = doc.RootElement.GetProperty("SourceControl").GetProperty("Perforce");
        Assert.Equal(string.Empty, perforce.GetProperty("Port").GetString());
        Assert.Equal(string.Empty, perforce.GetProperty("Client").GetString());
        Assert.Equal(string.Empty, perforce.GetProperty("User").GetString());
    }

    [Fact]
    public async Task SourceControlUpdate_OmittedPerforce_PreservesExistingPerforceConfig()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await SeedPerforceConfigAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "p4config"
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var result = SingleResult(sent);
        Assert.Equal("p4config", result.GetProperty("connectionMode").GetString());
        Assert.Equal("ssl:p4:1666", result.GetProperty("perforce").GetProperty("port").GetString());
        Assert.Equal("game-main-alice", result.GetProperty("perforce").GetProperty("client").GetString());
    }

    [Fact]
    public async Task SourceControlGet_AfterBind_ReturnsPerforceSnapshotWithoutPassword()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
        {
            provider = "perforce",
            connectionMode = "manual",
            perforce = new { port = "ssl:p4:1666", client = "c", user = "u" }
        }));
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlGet, new { }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        var result = SingleResult(sent);
        Assert.Equal("perforce", result.GetProperty("provider").GetString());
        Assert.Equal("notTested", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("capabilities").GetProperty("perforceChangelist").GetBoolean());
        Assert.False(result.GetProperty("capabilities").GetProperty("perforceSubmit").GetBoolean());
        Assert.DoesNotContain("assword", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SourceControlThreadTarget_Update_PersistsPerThreadPerforceTarget()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlThreadTargetGet, new
        {
            threadId = thread.Id
        }));
        var initial = SingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("default", initial.GetProperty("target").GetProperty("changelist").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlThreadTargetUpdate, new
        {
            threadId = thread.Id,
            target = new { provider = "perforce", changelist = "12345" }
        }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var result = SingleResult(sent);
        Assert.Equal("12345", result.GetProperty("target").GetProperty("changelist").GetString());

        var updated = await harness.Service.GetThreadAsync(thread.Id);
        Assert.Equal("perforce", updated.Metadata[ThreadSourceControlMetadata.ProviderKey]);
        Assert.Equal("12345", updated.Metadata[ThreadSourceControlMetadata.PerforceChangelistKey]);
    }

    [Fact]
    public async Task ForkThread_DoesNotInheritPerforceThreadTarget()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        var source = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.Service.UpdateThreadSourceControlTargetAsync(
            source.Id,
            new ThreadSourceControlTarget { Provider = "perforce", Changelist = "555" });

        var fork = await harness.Service.ForkThreadAsync(source.Id);

        Assert.DoesNotContain(fork.Metadata, kv => kv.Key.StartsWith("sourceControl.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SourceControlUpdate_PreservesUnrelatedConfigKeys()
    {
        await File.WriteAllTextAsync(_configPath, "{\n  \"Model\": \"gpt-4o-mini\"\n}\n");

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
        {
            provider = "git"
        }));
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var json = await File.ReadAllTextAsync(_configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("gpt-4o-mini", doc.RootElement.GetProperty("Model").GetString());
        Assert.Equal("git", doc.RootElement.GetProperty("SourceControl").GetProperty("Provider").GetString());
    }

    [Fact]
    public async Task SourceControlGet_WithoutWorkspaceCraftPath_ReturnsMethodNotFound()
    {
        // No workspaceCraftPath -> sourceControlManagement capability is absent and methods 404.
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlGet, new { }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), -32601);
    }

    [Fact]
    public async Task SourceControlGet_WithNoConfig_DefaultsToGit()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlGet, new { }));
        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));

        var result = SingleResult(sent);
        Assert.Equal("git", result.GetProperty("provider").GetString());
        Assert.Equal("git", result.GetProperty("effectiveProvider").GetString());
    }

    private static async Task SeedPerforceConfigAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SourceControlUpdate, new
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
            if (!harness.Connection.SupportsConfigChange || !harness.Connection.ShouldSendNotification(AppServerMethods.WorkspaceConfigChanged))
                return;

            var notification = new
            {
                jsonrpc = "2.0",
                method = AppServerMethods.WorkspaceConfigChanged,
                @params = new WorkspaceConfigChangedParams
                {
                    Source = change.Source,
                    Regions = [.. change.Regions],
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
                && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        Assert.Contains(expectedRegion, payload.GetProperty("regions").EnumerateArray().Select(v => v.GetString()));
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private static void AssertNoConfigChanged(IReadOnlyList<JsonDocument> sent)
    {
        Assert.DoesNotContain(
            sent,
            d => d.RootElement.TryGetProperty("method", out var method)
                 && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal));
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
