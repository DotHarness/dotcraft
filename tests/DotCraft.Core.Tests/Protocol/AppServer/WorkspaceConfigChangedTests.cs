using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using System.Text.Json;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class WorkspaceConfigChangedTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"workspace_config_changed_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public WorkspaceConfigChangedTests()
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
    public async Task WorkspaceConfigUpdate_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-test-new") }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceProviderPreferences);
    }




    [Fact]
    public async Task WorkspaceConfigUpdate_WelcomeSuggestionsOnly_EmitsWelcomeSuggestionsRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            welcomeSuggestionsEnabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WelcomeSuggestions);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_SkillsSelfLearningOnly_WritesConfigAndEmitsSkillsRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            skillsSelfLearningEnabled = true
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.Skills);
        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Skills").GetProperty("SelfLearning").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_SkillsSelfLearningNull_RemovesLeafAndPrunesEmptySections()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Skills": {
                "SelfLearning": {
                  "Enabled": true
                }
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        using var requestDoc = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "workspace/config/update",
              "params": {
                "skillsSelfLearningEnabled": null
              }
            }
            """);
        var req = new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Id = requestDoc.RootElement.GetProperty("id").Clone(),
            Method = DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("skillsSelfLearningEnabled").ValueKind);
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.Skills);
        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Skills", out _));
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_MemoryAutoConsolidateOnly_WritesConfigUpdatesMonitorAndEmitsMemoryRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            memoryAutoConsolidateEnabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.Memory);
        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Memory").GetProperty("AutoConsolidateEnabled").GetBoolean());
        Assert.False(harness.Monitor.Current.Memory.AutoConsolidateEnabled);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_MemoryAutoConsolidateNull_RemovesLeafAndPrunesEmptySection()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Memory": {
                "AutoConsolidateEnabled": false
              }
            }
            """);

        var monitor = new AppConfigMonitor(new AppConfig
        {
            Memory = new MemoryConfig
            {
                AutoConsolidateEnabled = false
            }
        });
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath, appConfigMonitor: monitor);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        using var requestDoc = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "workspace/config/update",
              "params": {
                "memoryAutoConsolidateEnabled": null
              }
            }
            """);
        var req = new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Id = requestDoc.RootElement.GetProperty("id").Clone(),
            Method = DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("memoryAutoConsolidateEnabled").ValueKind);
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.Memory);
        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Memory", out _));
        Assert.True(harness.Monitor.Current.Memory.AutoConsolidateEnabled);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_DefaultApprovalPolicy_RoundTripsAndEmitsRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            defaultApprovalPolicy = "autoApprove"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal("autoApprove", response.RootElement.GetProperty("result").GetProperty("defaultApprovalPolicy").GetString());
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "autoApprove",
            doc.RootElement.GetProperty("Permissions").GetProperty("DefaultApprovalPolicy").GetString());
        Assert.Equal(ApprovalPolicy.AutoApprove, harness.Monitor.Current.Permissions.DefaultApprovalPolicy);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_DefaultApprovalPolicyNull_RemovesLeafAndPrunesEmptySection()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Permissions": {
                "DefaultApprovalPolicy": "autoApprove"
              }
            }
            """);

        var monitor = new AppConfigMonitor(new AppConfig
        {
            Permissions = new AppConfig.PermissionsConfig
            {
                DefaultApprovalPolicy = ApprovalPolicy.AutoApprove
            }
        });
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath, appConfigMonitor: monitor);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        using var requestDoc = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "workspace/config/update",
              "params": {
                "defaultApprovalPolicy": null
              }
            }
            """);
        var req = new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Id = requestDoc.RootElement.GetProperty("id").Clone(),
            Method = DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("defaultApprovalPolicy").ValueKind);
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Permissions", out _));
        Assert.Equal(ApprovalPolicy.Default, harness.Monitor.Current.Permissions.DefaultApprovalPolicy);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_Reasoning_WritesConfigUpdatesMonitorAndEmitsReasoningRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            reasoning = new
            {
                enabled = true,
                effort = "high"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ReasoningNull_RemovesReasoningSection()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Reasoning": {
                "Enabled": true,
                "Effort": "High",
                "Output": "Full"
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            new System.Text.Json.Nodes.JsonObject { ["reasoning"] = null });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
        Assert.True(File.Exists(configPath));
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_Speed_WritesConfigUpdatesMonitorAndEmitsSpeedRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            new { speed = "fast" }));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ContextWindowMax_WritesConfigUpdatesMonitorAndEmitsRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var monitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI(model: "gpt-5.5"));
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath, appConfigMonitor: monitor);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            contextWindow = new
            {
                mode = "max"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ContextWindowNull_RemovesLeafAndPrunesEmptySection()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Compaction": {
                "ContextWindowMode": "Max"
              }
            }
            """);

        var monitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI(model: "gpt-5.5"));
        monitor.Current.Compaction.ContextWindowMode = ContextWindowMode.Max;
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath, appConfigMonitor: monitor);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            new System.Text.Json.Nodes.JsonObject { ["contextWindow"] = null });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
        Assert.True(File.Exists(configPath));
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ModelWelcomeSuggestionsAndSelfLearning_EmitsAllWorkspaceRegions()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-test-new") },
            welcomeSuggestionsEnabled = false,
            skillsSelfLearningEnabled = true,
            memoryAutoConsolidateEnabled = false,
            defaultApprovalPolicy = "autoApprove"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChangedRegions(
            sent,
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
            [
                ConfigChangeRegions.WorkspaceProviderPreferences,
                ConfigChangeRegions.WelcomeSuggestions,
                ConfigChangeRegions.Skills,
                ConfigChangeRegions.Memory,
                ConfigChangeRegions.WorkspaceDefaultApprovalPolicy
            ]);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_NoEffectiveChange_DoesNotEmitWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var firstReq = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-4.1") }
        });
        await harness.ExecuteRequestAsync(firstReq);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var secondReq = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-4.1") }
        });
        await harness.ExecuteRequestAsync(secondReq);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_PreservesUnrelatedFieldsAndKeyCasing()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Theme": "dark",
              "CustomSettings": {
                "keep": true
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-4o-mini") }
        });
        await harness.ExecuteRequestAsync(req);

        var json = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"ProviderPreferences\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Theme\": \"dark\"", json, StringComparison.Ordinal);
        using var preservedDoc = JsonDocument.Parse(json);
        Assert.True(preservedDoc.RootElement.GetProperty("CustomSettings").GetProperty("keep").GetBoolean());
        Assert.False(File.Exists(harness.Monitor.Current.GlobalConfigPath!));
    }

    [Fact]
    public async Task SkillsSetEnabled_EmitsWorkspaceConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        var skillName = loader.ListSkills(filterUnavailable: false).First().Name;

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsSetEnabled, new { name = skillName, enabled = false });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsSetEnabled, ConfigChangeRegions.Skills);
    }

    [Fact]
    public async Task McpUpsert_EmitsWorkspaceConfigChanged()
    {
        var manager = new McpClientManager();
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            mcpClientManager: manager);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpUpsert, new
        {
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
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpUpsert, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task McpRemove_EmitsWorkspaceConfigChanged()
    {
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

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpRemove, new { name = "demo" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpRemove, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task ExternalChannelUpsert_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
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
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelUpsert, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task ExternalChannelRemove_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
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

        var remove = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelRemove, new { name = "telegram" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelRemove, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task SubAgentProfileSetEnabled_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, new
        {
            name = "cursor-cli",
            enabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileUpsert_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileUpsert, new
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
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileUpsert, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileRemove_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileUpsert, new
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

        var remove = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileRemove, new { name = "codex-cli" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SubAgentProfileRemove, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task FailedWrite_DoesNotEmitWorkspaceConfigChanged()
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();

        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsSetEnabled, new { name = "missing_skill", enabled = true });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        AppServerTestHarness.AssertIsErrorResponse(sent[0], AppServerErrors.SkillNotFoundCode);
    }

    [Fact]
    public async Task ReadMethods_DoNotEmitWorkspaceConfigChanged()
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

        var skillsList = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.SkillsList, new { includeUnavailable = true });
        await harness.ExecuteRequestAsync(skillsList);
        var skillsSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(skillsSent);

        var mcpList = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.McpList, new { });
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

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-test-new") }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        Assert.Single(monitorEvents);
        Assert.Equal(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, monitorEvents[0].Source);
        Assert.Contains(ConfigChangeRegions.WorkspaceProviderPreferences, monitorEvents[0].Regions);

        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs e)
        {
            monitorEvents.Add(e);
        }
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderPreferences_RoundTripsPersistsAndEmitsWorkspacePreferenceRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference>
            {
                ["openai"] = Preference("gpt-x", InferenceSpeed.Fast),
                ["anthropic-main"] = Preference("claude-y")
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceProviderPreferences);

        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("model", out _));
        var resultPreferences = result.GetProperty("providerPreferences");
        Assert.Equal("gpt-x", resultPreferences.GetProperty("openai").GetProperty("model").GetString());
        Assert.Equal("fast", resultPreferences.GetProperty("openai").GetProperty("speed").GetString());
        Assert.Equal("default", resultPreferences.GetProperty("openai").GetProperty("contextWindow").GetProperty("mode").GetString());
        Assert.Equal("claude-y", resultPreferences.GetProperty("anthropic-main").GetProperty("model").GetString());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var persisted = doc.RootElement.GetProperty("ProviderPreferences");
        Assert.Equal("gpt-x", persisted.GetProperty("openai").GetProperty("Model").GetString());
        Assert.Equal("claude-y", persisted.GetProperty("anthropic-main").GetProperty("Model").GetString());
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderPreferencesEmpty_RemovesKeyAndReturnsNull()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "ProviderPreferences": {
                "openai": {
                  "Model": "gpt-x",
                  "Reasoning": { "Enabled": false, "Effort": "Medium", "Output": "Full" },
                  "Speed": "Standard",
                  "ContextWindow": { "Mode": "Default" }
                }
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference>()
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceProviderPreferences);

        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.False(response.RootElement.GetProperty("result").TryGetProperty("providerPreferences", out _));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert.False(doc.RootElement.TryGetProperty("ProviderPreferences", out _));
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderPreferences_PreservesUnrelatedFieldsAndKeyCasing()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "Theme": "dark",
              "CustomSettings": {
                "keep": true
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerPreferences = new Dictionary<string, ModelPreference> { ["openai"] = Preference("gpt-x") }
        });
        await harness.ExecuteRequestAsync(req);

        var json = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"Theme\": \"dark\"", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("CustomSettings").GetProperty("keep").GetBoolean());
        Assert.Equal("gpt-x", doc.RootElement.GetProperty("ProviderPreferences").GetProperty("openai").GetProperty("Model").GetString());
    }

    private static ModelPreference Preference(
        string model,
        InferenceSpeed speed = InferenceSpeed.Standard,
        ContextWindowMode contextMode = ContextWindowMode.Default) => new()
        {
            Model = model,
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = false,
                Effort = Microsoft.Extensions.AI.ReasoningEffort.Medium,
                Output = Microsoft.Extensions.AI.ReasoningOutput.Full
            },
            Speed = speed,
            ContextWindow = new ModelPreferenceContextWindow { Mode = contextMode }
        };

    private static IDisposable AttachConfigChangedBridge(AppServerTestHarness harness)
    {
        void OnChanged(object? sender, AppConfigChangedEventArgs change)
        {
            if (!harness.Connection.SupportsConfigChange || !harness.Connection.ShouldSendNotification(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigChanged))
                return;

            var notification = new
            {
                jsonrpc = "2.0",
                method = DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigChanged,
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
                && string.Equals(method.GetString(), DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        Assert.Contains(expectedRegion, payload.GetProperty("regions").EnumerateArray().Select(v => v.GetString()));
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private static void AssertSingleConfigChangedRegions(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        IReadOnlyList<string> expectedRegions)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigChanged, StringComparison.Ordinal))
            .ToList();
        Assert.Single(notifications);

        var payload = notifications[0].RootElement.GetProperty("params");
        Assert.Equal(expectedSource, payload.GetProperty("source").GetString());
        var regions = payload.GetProperty("regions").EnumerateArray()
            .Select(v => v.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();
        Assert.Equal(expectedRegions.Count, regions.Count);
        foreach (var region in expectedRegions)
            Assert.Contains(region, regions);
        _ = payload.GetProperty("changedAt").GetDateTimeOffset();
    }

    private static void AssertNoConfigChanged(IReadOnlyList<JsonDocument> sent)
    {
        Assert.DoesNotContain(
            sent,
            d => d.RootElement.TryGetProperty("method", out var method)
                 && string.Equals(method.GetString(), DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigChanged, StringComparison.Ordinal));
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
