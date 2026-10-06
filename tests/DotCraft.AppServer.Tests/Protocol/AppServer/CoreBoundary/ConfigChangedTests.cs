using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Mcp;
using DotCraft.Skills;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Sessions;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ConfigChangedTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"workspace_config_changed_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public ConfigChangedTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
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
    public async Task SkillsSetEnabled_EmitsConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        var skillName = loader.ListSkills().First().Name;

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsSetEnabled, new { name = skillName, enabled = false });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsSetEnabled, ConfigChangeRegions.Skills);
    }

    [Fact]
    public async Task McpUpsert_EmitsConfigChanged()
    {
        var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpUpsert, new
        {
            scope = "workspace",
            server = new
            {
                name = "demo",
                enabled = false,
                transport = "streamableHttp",
                url = "https://example.com/mcp"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.McpUpsert, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task McpRemove_EmitsConfigChanged()
    {
        DotCraft.Mcp.McpScopeStore.Upsert(Path.Combine(_workspaceCraftPath, "config.json"), new McpServerConfig
        {
            Name = "demo", Enabled = false, Transport = "streamableHttp", Url = "https://example.com/mcp"
        });
        var manager = new McpClientManager();
        await manager.UpsertAsync(new McpServerConfig
        {
            Name = "demo",
            Enabled = false,
            Transport = "streamableHttp",
            Url = "https://example.com/mcp"
        });

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpRemove, new { name = "demo", scope = "workspace" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.McpRemove, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task ExternalChannelUpsert_EmitsConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "telegram",
                enabled = true,
                transport = "websocket"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task ExternalChannelRemove_EmitsConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "telegram",
                enabled = true,
                transport = "websocket"
            }
        });
        await harness.ExecuteRequestAsync(upsert);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var remove = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelRemove, new { name = "telegram" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelRemove, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task SubAgentProfileSetEnabled_EmitsConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "cursor-cli",
            enabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileUpsert_EmitsConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileUpsert, new
        {
            name = "codex-cli",
            definition = new
            {
                runtime = "cli-oneshot",
                bin = "codex",
                args = new[] { "exec", "--skip-git-repo-check" },
                workingDirectoryMode = "workspace",
                inputMode = "arg",
                outputFormat = "text",
                outputFileArgTemplate = "--output-last-message {path}",
                readOutputFile = true,
                deleteOutputFileAfterRead = true,
                supportsStreaming = false,
                supportsResume = false,
                timeout = 600,
                maxOutputBytes = 1048576,
                trustLevel = "prompt"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileUpsert, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileRemove_EmitsConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileUpsert, new
        {
            name = "codex-cli",
            definition = new
            {
                runtime = "cli-oneshot",
                bin = "codex",
                args = new[] { "exec", "--skip-git-repo-check" },
                workingDirectoryMode = "workspace",
                inputMode = "arg",
                outputFormat = "text",
                outputFileArgTemplate = "--output-last-message {path}",
                readOutputFile = true,
                deleteOutputFileAfterRead = true,
                supportsStreaming = false,
                supportsResume = false,
                timeout = 600,
                maxOutputBytes = 1048576,
                trustLevel = "prompt"
            }
        });
        await harness.ExecuteRequestAsync(upsert);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var remove = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileRemove, new { name = "codex-cli" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentProfileRemove, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task FailedWrite_DoesNotEmitConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsSetEnabled, new { name = "missing_skill", enabled = true });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        AppServerTestHarness.AssertIsErrorResponse(sent[0], AppServerErrors.SkillNotFoundCode);
    }

    [Fact]
    public async Task ReadMethods_DoNotEmitConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        var manager = new McpClientManager();

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var skillsList = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsList, new { });
        await harness.ExecuteRequestAsync(skillsList);
        var skillsSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(skillsSent);

        var mcpList = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpList, new { });
        await harness.ExecuteRequestAsync(mcpList);
        var mcpSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(mcpSent);
    }

    [Fact]
    public async Task ConfigChangeCapabilityFalse_SuppressesWireNotification_ButMonitorStillFires()
    {
        var monitorEvents = new List<AppConfigChangedEventArgs>();
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync(configChange: false);

        var req = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigValueWrite, new
        {
            keyPath = "InstantInterruptEnabled",
            value = false,
            mergeStrategy = "replace"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        Assert.Single(monitorEvents);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigValueWrite, monitorEvents[0].Source);
        Assert.Equal(["InstantInterruptEnabled"], monitorEvents[0].Regions);

        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs e)
        {
            monitorEvents.Add(e);
        }
    }

    internal static IDisposable AttachConfigChangedBridge(AppServerTestHarness harness)
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

    internal static void AssertSingleConfigChanged(
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

    internal static void AssertNoConfigChanged(IReadOnlyList<JsonDocument> sent)
    {
        Assert.DoesNotContain(
            sent,
            d => d.RootElement.TryGetProperty("method", out var method)
                 && string.Equals(method.GetString(), DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigChanged, StringComparison.Ordinal));
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
