using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;

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
            Profile = WorkspaceBootstrapProfile.Developer,
            SaveToUserConfig = true
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.True(File.Exists(globalConfigPath));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.DoesNotContain("Language", globalNode.Select(p => p.Key));
        Assert.Equal("openai", globalNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("gpt-4o-mini", globalNode["Model"]?.GetValue<string>());
        var globalProvider = globalNode["Providers"]!["openai"]!;
        Assert.Equal("sk-global", globalProvider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://example.com/v1", globalProvider["EndPoint"]?.GetValue<string>());
        Assert.DoesNotContain("ApiKey", globalNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", globalNode.Select(p => p.Key));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("Model", workspaceNode.Select(p => p.Key));
        Assert.Contains("Developer Assistant", File.ReadAllText(Path.Combine(craftPath, "AGENTS.md")));
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
            SaveToUserConfig = false
        }, globalConfigPath);

        Assert.Equal(0, result);
        Assert.True(File.Exists(globalConfigPath));

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        var provider = globalNode["Providers"]!["openai"]!;
        Assert.Equal("sk-local", provider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://local.example/v1", provider["EndPoint"]?.GetValue<string>());
        Assert.DoesNotContain("ApiKey", globalNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", globalNode.Select(p => p.Key));

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
        Assert.Equal("openai", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("deepseek-chat", workspaceNode["Model"]?.GetValue<string>());
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
        Assert.Contains("Personal Assistant", File.ReadAllText(Path.Combine(craftPath, "AGENTS.md")));
    }

    [Fact]
    public void RunSetup_PreferExistingUserConfig_WritesWorkspaceOverridesOnly()
    {
        var craftPath = Path.Combine(tempRoot, ".craft");
        var globalConfigPath = Path.Combine(tempRoot, "user", ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        File.WriteAllText(globalConfigPath, """
        {
          "Language": "English",
          "ApiKey": "sk-global",
          "EndPoint": "https://example.com/v1",
          "Model": "gpt-4o-mini"
        }
        """);

        var result = InitHelper.RunSetup(craftPath, new WorkspaceSetupRequest
        {
            ApiKey = "",
            EndPoint = "https://workspace.example/v1",
            Model = "gpt-4.1",
            Profile = WorkspaceBootstrapProfile.Default,
            PreferExistingUserConfig = true
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.Equal("https://example.com/v1", globalNode["EndPoint"]?.GetValue<string>());
        Assert.Equal("gpt-4o-mini", globalNode["Model"]?.GetValue<string>());
        Assert.Equal("sk-global", globalNode["ApiKey"]?.GetValue<string>());
        Assert.DoesNotContain("Language", globalNode.Select(p => p.Key));
        var provider = globalNode["Providers"]!["openai"]!;
        Assert.Equal("", provider["ApiKey"]?.GetValue<string>());
        Assert.Equal("https://workspace.example/v1", provider["EndPoint"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
        Assert.Equal("openai", workspaceNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("gpt-4.1", workspaceNode["Model"]?.GetValue<string>());
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
        Assert.Equal("claude-sonnet-4-5", workspaceNode["Model"]?.GetValue<string>());
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("Language", workspaceNode.Select(p => p.Key));
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
                Protocol = "openai",
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
        Assert.Equal("gpt-4.1", workspaceNode["Model"]?.GetValue<string>());
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
                Protocol = "openai",
                ApiKey = "sk-openai",
                EndPoint = "https://api.openai.com/v1"
            }
        }, globalConfigPath);

        Assert.Equal(0, result);

        var globalNode = JsonNode.Parse(File.ReadAllText(globalConfigPath))!.AsObject();
        Assert.DoesNotContain("Language", globalNode.Select(p => p.Key));
        Assert.Equal("openai-main", globalNode["ProviderId"]?.GetValue<string>());
        Assert.Equal("gpt-4.1", globalNode["Model"]?.GetValue<string>());
        Assert.Equal("OpenAI", globalNode["Providers"]!["openai-main"]!["DisplayName"]?.GetValue<string>());

        var workspaceNode = JsonNode.Parse(File.ReadAllText(Path.Combine(craftPath, "config.json")))!.AsObject();
        Assert.DoesNotContain("ProviderId", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("Model", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
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
        Assert.DoesNotContain("Model", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("ApiKey", workspaceNode.Select(p => p.Key));
        Assert.DoesNotContain("EndPoint", workspaceNode.Select(p => p.Key));
        Assert.Contains("Personal Assistant", File.ReadAllText(Path.Combine(craftPath, "AGENTS.md")));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
