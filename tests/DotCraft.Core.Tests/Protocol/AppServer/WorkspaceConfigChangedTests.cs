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

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-test-new" }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceModel);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ApiKeyOnly_ReturnsInvalidParamsWithoutConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { apiKey = "sk-live-key" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_LegacyModel_ReturnsInvalidParamsWithoutConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { model = "gpt-legacy" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_EndPointOnly_ReturnsInvalidParamsWithoutConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { endPoint = "https://example.com/v1" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertIsErrorResponse(Assert.Single(sent), AppServerErrors.InvalidParamsCode);
        AssertNoConfigChanged(sent);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_WelcomeSuggestionsOnly_EmitsWelcomeSuggestionsRegion()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            welcomeSuggestionsEnabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WelcomeSuggestions);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_SkillsSelfLearningOnly_WritesConfigAndEmitsSkillsRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            skillsSelfLearningEnabled = true
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.Skills);
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
            Method = AppServerMethods.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("skillsSelfLearningEnabled").ValueKind);
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.Skills);
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

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            memoryAutoConsolidateEnabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.Memory);
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
            Method = AppServerMethods.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("memoryAutoConsolidateEnabled").ValueKind);
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.Memory);
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

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            defaultApprovalPolicy = "autoApprove"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal("autoApprove", response.RootElement.GetProperty("result").GetProperty("defaultApprovalPolicy").GetString());
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);

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
            Method = AppServerMethods.WorkspaceConfigUpdate,
            Params = requestDoc.RootElement.GetProperty("params").Clone()
        };
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("defaultApprovalPolicy").ValueKind);
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Permissions", out _));
        Assert.Equal(ApprovalPolicy.Default, harness.Monitor.Current.Permissions.DefaultApprovalPolicy);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_Reasoning_WritesConfigUpdatesMonitorAndEmitsReasoningRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            reasoning = new
            {
                enabled = true,
                effort = "high"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceReasoning);
        var response = sent.Single(message => message.RootElement.TryGetProperty("id", out _));
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.Equal("high", result.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("full", result.GetProperty("reasoning").GetProperty("output").GetString());
        Assert.True(harness.Monitor.Current.Reasoning.Enabled);
        Assert.Equal(Microsoft.Extensions.AI.ReasoningEffort.High, harness.Monitor.Current.Reasoning.Effort);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        var reasoning = doc.RootElement.GetProperty("Reasoning");
        Assert.True(reasoning.GetProperty("Enabled").GetBoolean());
        Assert.Equal("High", reasoning.GetProperty("Effort").GetString());
        Assert.Equal("Full", reasoning.GetProperty("Output").GetString());
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
            AppServerMethods.WorkspaceConfigUpdate,
            new System.Text.Json.Nodes.JsonObject { ["reasoning"] = null });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceReasoning);
        var response = sent.Single(message => message.RootElement.TryGetProperty("id", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("reasoning").ValueKind);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Reasoning", out _));
        Assert.False(harness.Monitor.Current.Reasoning.Enabled);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_Speed_WritesConfigUpdatesMonitorAndEmitsSpeedRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.WorkspaceConfigUpdate,
            new { speed = "fast" }));

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceSpeed);
        var response = sent.Single(message => message.RootElement.TryGetProperty("id", out _));
        Assert.Equal("fast", response.RootElement.GetProperty("result").GetProperty("speed").GetString());
        Assert.Equal(InferenceSpeed.Fast, harness.Monitor.Current.Speed);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert.Equal("Fast", doc.RootElement.GetProperty("Speed").GetString());
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ContextWindowMax_WritesConfigUpdatesMonitorAndEmitsRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        var monitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI(model: "gpt-5.5"));
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath, appConfigMonitor: monitor);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            contextWindow = new
            {
                mode = "max"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceContextWindow);
        var response = sent.Single(message => message.RootElement.TryGetProperty("id", out _));
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("max", result.GetProperty("contextWindow").GetProperty("mode").GetString());
        Assert.Equal(ContextWindowMode.Max, harness.Monitor.Current.Compaction.ContextWindowMode);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "Max",
            doc.RootElement.GetProperty("Compaction").GetProperty("ContextWindowMode").GetString());
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
            AppServerMethods.WorkspaceConfigUpdate,
            new System.Text.Json.Nodes.JsonObject { ["contextWindow"] = null });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceContextWindow);
        var response = sent.Single(message => message.RootElement.TryGetProperty("id", out _));
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("result").GetProperty("contextWindow").ValueKind);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Compaction", out _));
        Assert.Equal(ContextWindowMode.Default, harness.Monitor.Current.Compaction.ContextWindowMode);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ModelWelcomeSuggestionsAndSelfLearning_EmitsAllWorkspaceRegions()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-test-new" },
            welcomeSuggestionsEnabled = false,
            skillsSelfLearningEnabled = true,
            memoryAutoConsolidateEnabled = false,
            defaultApprovalPolicy = "autoApprove"
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChangedRegions(
            sent,
            AppServerMethods.WorkspaceConfigUpdate,
            [
                ConfigChangeRegions.WorkspaceModel,
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

        var firstReq = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-4.1" }
        });
        await harness.ExecuteRequestAsync(firstReq);
        await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));

        var secondReq = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-4.1" }
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
              "model": "gpt-legacy",
              "apikey": "sk-old",
              "endpoint": "https://old.example.com/v1",
              "Theme": "dark"
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-4o-mini" }
        });
        await harness.ExecuteRequestAsync(req);

        var json = await File.ReadAllTextAsync(configPath);
        Assert.DoesNotContain("\"model\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"ProviderModels\"", json, StringComparison.Ordinal);
        Assert.Contains("\"apikey\": \"sk-old\"", json, StringComparison.Ordinal);
        Assert.Contains("\"endpoint\": \"https://old.example.com/v1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Theme\": \"dark\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Model\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ApiKey\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"EndPoint\":", json, StringComparison.Ordinal);
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

        var req = harness.BuildRequest(AppServerMethods.SkillsSetEnabled, new { name = skillName, enabled = false });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.SkillsSetEnabled, ConfigChangeRegions.Skills);
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

        var req = harness.BuildRequest(AppServerMethods.McpUpsert, new
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
        AssertSingleConfigChanged(sent, AppServerMethods.McpUpsert, ConfigChangeRegions.Mcp);
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

        var req = harness.BuildRequest(AppServerMethods.McpRemove, new { name = "demo" });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.McpRemove, ConfigChangeRegions.Mcp);
    }

    [Fact]
    public async Task ExternalChannelUpsert_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
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
        AssertSingleConfigChanged(sent, AppServerMethods.ExternalChannelUpsert, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task ExternalChannelRemove_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
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

        var remove = harness.BuildRequest(AppServerMethods.ExternalChannelRemove, new { name = "telegram" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.ExternalChannelRemove, ConfigChangeRegions.ExternalChannel);
    }

    [Fact]
    public async Task SubAgentProfileSetEnabled_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SubAgentProfileSetEnabled, new
        {
            name = "cursor-cli",
            enabled = false
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.SubAgentProfileSetEnabled, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileUpsert_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.SubAgentProfileUpsert, new
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
        AssertSingleConfigChanged(sent, AppServerMethods.SubAgentProfileUpsert, ConfigChangeRegions.SubAgent);
    }

    [Fact]
    public async Task SubAgentProfileRemove_EmitsWorkspaceConfigChanged()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var upsert = harness.BuildRequest(AppServerMethods.SubAgentProfileUpsert, new
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

        var remove = harness.BuildRequest(AppServerMethods.SubAgentProfileRemove, new { name = "codex-cli" });
        await harness.ExecuteRequestAsync(remove);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.SubAgentProfileRemove, ConfigChangeRegions.SubAgent);
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

        var req = harness.BuildRequest(AppServerMethods.SkillsSetEnabled, new { name = "missing_skill", enabled = true });
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

        var skillsList = harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true });
        await harness.ExecuteRequestAsync(skillsList);
        var skillsSent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(skillsSent);

        var mcpList = harness.BuildRequest(AppServerMethods.McpList, new { });
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

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-test-new" }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        AssertNoConfigChanged(sent);
        Assert.Single(monitorEvents);
        Assert.Equal(AppServerMethods.WorkspaceConfigUpdate, monitorEvents[0].Source);
        Assert.Contains(ConfigChangeRegions.WorkspaceModel, monitorEvents[0].Regions);

        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs e)
        {
            monitorEvents.Add(e);
        }
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderModels_RoundTripsPersistsAndEmitsWorkspaceModelRegion()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string>
            {
                ["openai"] = "gpt-x",
                ["anthropic-main"] = "claude-y"
            }
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceModel);

        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("model", out _));
        var resultModels = result.GetProperty("providerModels");
        Assert.Equal("gpt-x", resultModels.GetProperty("openai").GetString());
        Assert.Equal("claude-y", resultModels.GetProperty("anthropic-main").GetString());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var persisted = doc.RootElement.GetProperty("ProviderModels");
        Assert.Equal("gpt-x", persisted.GetProperty("openai").GetString());
        Assert.Equal("claude-y", persisted.GetProperty("anthropic-main").GetString());
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderModelsEmpty_RemovesKeyAndReturnsNull()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "ProviderModels": {
                "openai": "gpt-x",
                "anthropic-main": "claude-y"
              }
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        using var bridge = AttachConfigChangedBridge(harness);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string>()
        });
        await harness.ExecuteRequestAsync(req);

        var sent = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AssertSingleConfigChanged(sent, AppServerMethods.WorkspaceConfigUpdate, ConfigChangeRegions.WorkspaceModel);

        var response = Assert.Single(sent, d => d.RootElement.TryGetProperty("result", out _));
        Assert.False(response.RootElement.GetProperty("result").TryGetProperty("providerModels", out _));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        Assert.False(doc.RootElement.TryGetProperty("ProviderModels", out _));
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_ProviderModels_PreservesUnrelatedFieldsAndKeyCasing()
    {
        var configPath = Path.Combine(_workspaceCraftPath, "config.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "model": "gpt-legacy",
              "Theme": "dark"
            }
            """);

        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync(configChange: true);

        var req = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
        {
            providerModels = new Dictionary<string, string> { ["openai"] = "gpt-x" }
        });
        await harness.ExecuteRequestAsync(req);

        var json = await File.ReadAllTextAsync(configPath);
        Assert.DoesNotContain("\"model\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Theme\": \"dark\"", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("gpt-x", doc.RootElement.GetProperty("ProviderModels").GetProperty("openai").GetString());
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

    private static void AssertSingleConfigChangedRegions(
        IReadOnlyList<JsonDocument> sent,
        string expectedSource,
        IReadOnlyList<string> expectedRegions)
    {
        var notifications = sent
            .Where(d =>
                d.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal))
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
                 && string.Equals(method.GetString(), AppServerMethods.WorkspaceConfigChanged, StringComparison.Ordinal));
    }

    private sealed class ActionOnDispose(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
}
