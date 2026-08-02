using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.CLI;

public sealed class InitHelperSetupTests : IDisposable
{
    private readonly string tempRoot;

    public InitHelperSetupTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public void RunSetup_SaveToUserConfig_WritesGlobalConfigAndKeepsWorkspaceConfigEmpty()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "sk-global",
            EndPoint = "https://example.com/v1",
            Model = "gpt-4o-mini",
            Preference = new ModelPreference
            {
                Model = "gpt-4o-mini",
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = true,
                    Effort = ReasoningEffort.High,
                    Output = ReasoningOutput.Summary
                },
                Speed = InferenceSpeed.Fast,
                ContextWindow = new ModelPreferenceContextWindow { Mode = ContextWindowMode.Max }
            },
            Profile = WorkspaceBootstrapProfile.Developer,
            SaveToUserConfig = true,
            ProviderMode = WorkspaceSetupProviderMode.Create,
            SetAsUserDefault = true,
            Provider = new WorkspaceSetupProviderDraft
            {
                Id = "openai",
                DisplayName = "OpenAI",
                Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                ApiKey = "sk-global",
                EndPoint = "https://example.com/v1"
            }
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.True(File.Exists(globalConfigPath));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("openai", globalNode["ProviderId"]?.GetValue<string>());
        var preference = globalNode["ProviderPreferences"]!["openai"]!;
        Assert.Equal("gpt-4o-mini", preference["model"]?.GetValue<string>());
        Assert.True(preference["reasoning"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("High", preference["reasoning"]!["effort"]?.GetValue<string>());
        Assert.Equal("Summary", preference["reasoning"]!["output"]?.GetValue<string>());
        Assert.Equal("Fast", preference["speed"]?.GetValue<string>());
        Assert.Equal("Max", preference["contextWindow"]!["mode"]?.GetValue<string>());
        var globalProvider = globalNode["Providers"]!["openai"]!;
        Assert.Equal("sk-global", globalProvider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://example.com/v1", globalProvider["EndPoint"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        var agentsPath = Path.Combine(craftPath, "AGENTS.md");
        Assert.True(File.Exists(agentsPath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(agentsPath)));
        Assert.False(File.Exists(Path.Combine(craftPath, "USER.md")));
        Assert.Contains("/visualizations/", File.ReadAllText(Path.Combine(craftPath, ".gitignore")), StringComparison.Ordinal);
    }

    [Fact]
    public void RunSetup_WorkspaceOnly_WritesWorkspaceConfigAndDoesNotCreateGlobalConfig()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "sk-local",
            EndPoint = "https://local.example/v1",
            Model = "deepseek-chat",
            Profile = WorkspaceBootstrapProfile.PersonalAssistant,
            SaveToUserConfig = false,
            ProviderMode = WorkspaceSetupProviderMode.Create,
            Provider = new WorkspaceSetupProviderDraft
            {
                Id = "openai",
                DisplayName = "OpenAI",
                Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                ApiKey = "sk-local",
                EndPoint = "https://local.example/v1"
            }
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.True(File.Exists(globalConfigPath));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        var provider = globalNode["Providers"]!["openai"]!;
        Assert.Equal("sk-local", provider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://local.example/v1", provider["EndPoint"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.Equal("openai", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("deepseek-chat", workspaceNode["ProviderPreferences"]!["openai"]!["model"]?.GetValue<string>());
        var agentsPath = Path.Combine(craftPath, "AGENTS.md");
        Assert.True(File.Exists(agentsPath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(agentsPath)));
        Assert.False(File.Exists(Path.Combine(craftPath, "USER.md")));
    }

    [Fact]
    public void RunSetup_ExistingPersonalDefaultWithoutFutureDefault_PinsMatchingWorkspacePreference()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");
        Directory.CreateDirectory(craftPath);
        File.WriteAllText(Path.Combine(craftPath, "config.json"), """
        {
          "providerId": "stale-lower",
          "PROVIDERID": "stale-upper",
          "providerPreferences": {
            "stale-lower": { "model": "stale-lower-model" }
          },
          "PROVIDERPREFERENCES": {
            "stale-upper": { "model": "stale-upper-model" }
          }
        }
        """);
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, """
        {
          "ProviderId": "openai",
          "ProviderPreferences": {
            "openai": {
              "model": "gpt-5.6-sol",
              "reasoning": {
                "enabled": false,
                "effort": "Medium",
                "output": "Full"
              },
              "speed": "Standard",
              "contextWindow": {
                "mode": "Default"
              }
            }
          },
          "Providers": {
            "openai": {
              "DisplayName": "OpenAI",
              "Protocol": "openai-responses",
              "ApiKey": "test-existing-key"
            }
          }
        }
        """);

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "gpt-5.6-sol",
            Preference = new ModelPreference { Model = "gpt-5.6-sol" },
            Profile = WorkspaceBootstrapProfile.Default,
            ProviderMode = WorkspaceSetupProviderMode.Existing,
            ProviderId = "openai",
            SetAsUserDefault = false
        }, globalConfigPath);

        Assert.Equal(0, result);

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.Equal("openai", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal(
            "gpt-5.6-sol",
            workspaceNode["ProviderPreferences"]!["openai"]!["model"]?.GetValue<string>());
        Assert.False(workspaceNode.ContainsKey("providerId"));
        Assert.False(workspaceNode.ContainsKey("PROVIDERID"));
        Assert.False(workspaceNode.ContainsKey("providerPreferences"));
        Assert.False(workspaceNode.ContainsKey("PROVIDERPREFERENCES"));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("openai", globalNode["ProviderId"]?.GetValue<string>());
        Assert.Equal(
            "gpt-5.6-sol",
            globalNode["ProviderPreferences"]!["openai"]!["model"]?.GetValue<string>());
    }

    [Fact]
    public void RunSetup_SkipProvider_PreservesUnrelatedGlobalSettings()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, """
        {
          "CustomSettings": {
            "theme": "dark"
          }
        }
        """);

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "",
            Profile = WorkspaceBootstrapProfile.Default,
            ProviderMode = WorkspaceSetupProviderMode.Skip
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("dark", globalNode["CustomSettings"]?["theme"]?.GetValue<string>());
        Assert.False(globalNode.ContainsKey("Providers"));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        Assert.False(File.Exists(Path.Combine(craftPath, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(craftPath, "USER.md")));
    }

    [Fact]
    public void RunSetup_CreateProvider_WritesProviderToGlobalAndWorkspaceSelectionOnly()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "claude-sonnet-4-5",
            Profile = WorkspaceBootstrapProfile.Developer,
            ProviderMode = WorkspaceSetupProviderMode.Create,
            Provider = new WorkspaceSetupProviderDraft
            {
                Id = "anthropic",
                DisplayName = "Anthropic",
                Protocol = "anthropic",
                ApiKey = "sk-ant",
                EndPoint = "https://api.anthropic.com",
                NetworkTimeoutSeconds = 120
            }
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        var provider = globalNode["Providers"]!["anthropic"]!;
        Assert.Equal("Anthropic", provider["DisplayName"]?.GetValue<string>());
        Assert.Equal("anthropic", provider["Protocol"]?.GetValue<string>());
        Assert.Equal("sk-ant", provider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://api.anthropic.com", provider["EndPoint"]?.GetValue<string>());
        Assert.Equal(120, provider["NetworkTimeoutSeconds"]?.GetValue<int>());
        Assert.DoesNotContain("ProviderId", globalNode.Select(p => p.Key));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.Equal("anthropic", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("claude-sonnet-4-5", workspaceNode["ProviderPreferences"]!["anthropic"]!["model"]?.GetValue<string>());
    }

    [Fact]
    public void RunSetup_CreateOpenAiProvider_AllowsBlankEndpoint()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "gpt-4.1",
            Profile = WorkspaceBootstrapProfile.Default,
            ProviderMode = WorkspaceSetupProviderMode.Create,
            Provider = new WorkspaceSetupProviderDraft
            {
                Id = "openai-api",
                DisplayName = "OpenAI",
                Protocol = "openai-chat-completions",
                ApiKey = "sk-openai",
                EndPoint = ""
            }
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        var provider = globalNode["Providers"]!["openai-api"]!.AsObject();
        Assert.Equal("OpenAI", provider["DisplayName"]?.GetValue<string>());
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, provider["Protocol"]?.GetValue<string>());
        Assert.Equal("sk-openai", provider["ApiKey"]?.GetValue<string>());
        Assert.DoesNotContain("EndPoint", provider.Select(p => p.Key));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.Equal("openai-api", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("gpt-4.1", workspaceNode["ProviderPreferences"]!["openai-api"]!["model"]?.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(craftPath, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(craftPath, "USER.md")));
    }

    [Fact]
    public void RunSetup_CreateProviderAsDefault_OmitsWorkspaceProviderAndModelOverrides()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "gpt-4.1",
            Profile = WorkspaceBootstrapProfile.Default,
            ProviderMode = WorkspaceSetupProviderMode.Create,
            SetAsUserDefault = true,
            Provider = new WorkspaceSetupProviderDraft
            {
                Id = "openai-main",
                DisplayName = "OpenAI",
                Protocol = "openai-chat-completions",
                ApiKey = "sk-openai",
                EndPoint = "https://api.openai.com/v1"
            }
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("openai-main", globalNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("gpt-4.1", globalNode["ProviderPreferences"]!["openai-main"]!["model"]?.GetValue<string>());
        Assert.Equal("OpenAI", globalNode["Providers"]!["openai-main"]!["DisplayName"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ProviderPreferences", workspaceNode.Select(p => p.Key));
    }

    [Fact]
    public void RunSetup_SkipProvider_CreatesWorkspaceWithoutModelProviderConfig()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "",
            Model = "",
            Profile = WorkspaceBootstrapProfile.PersonalAssistant,
            ProviderMode = WorkspaceSetupProviderMode.Skip
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.False(File.Exists(globalConfigPath));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        var agentsPath = Path.Combine(craftPath, "AGENTS.md");
        Assert.True(File.Exists(agentsPath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(agentsPath)));
        Assert.False(File.Exists(Path.Combine(craftPath, "USER.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
