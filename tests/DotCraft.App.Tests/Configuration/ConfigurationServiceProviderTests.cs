using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceProviderTests
{
    [Fact]
    public async Task Write_ProviderEntry_TargetsUserLayerKeepsWrittenFieldsAndReplacesRuntimeProviders()
    {
        using var fixture = Create(workspace: """{ "Hub": { "Port": 4100 } }""");
        var agentRebuilds = 0;
        fixture.Service.RegisterSubsystemHandler(ConfigurationSubsystems.ThreadAgents, (_, _) =>
        {
            agentRebuilds++;
            return Task.CompletedTask;
        });

        var result = await fixture.WriteAsync("Providers.p1", """{ "Protocol": "Anthropic", "ApiKey": "sk-p1" }""");

        Assert.Equal(Path.GetFullPath(fixture.UserPath), result.FilePath);
        var stored = ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!["p1"]!.AsObject();
        Assert.Equal(["Protocol", "ApiKey"], stored.Select(property => property.Key));
        Assert.Equal("sk-p1", fixture.Monitor.Current.Providers["p1"].ApiKey);
        Assert.Equal(1, agentRebuilds);
        Assert.Equal(["Providers", ConfigChangeRegions.ProviderRegistry], Assert.Single(fixture.Events).Regions);
        Assert.Equal(4100, ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath)["Hub"]!["Port"]!.GetValue<int>());
    }

    [Fact]
    public async Task Write_ProviderApiKey_IsWriteOnly()
    {
        using var fixture = Create(user: """{ "Providers": { "p1": { "ApiKey": "sk-old" } } }""");

        await fixture.WriteAsync("Providers.p1.ApiKey", "\"sk-new\"");
        await fixture.WriteAsync("Providers.p1", """{ "DisplayName": "P1", "ApiKey": "***" }""", ConfigMergeStrategy.Upsert);

        var stored = ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!["p1"]!;
        Assert.Equal("sk-new", stored["ApiKey"]!.GetValue<string>());
        Assert.Equal("P1", stored["DisplayName"]!.GetValue<string>());
        var read = fixture.Service.Read(includeLayers: true);
        Assert.Equal("***", read.Config["Providers"]!["p1"]!["ApiKey"]!.GetValue<string>());
        Assert.Equal("***", read.Layers![0].Config["Providers"]!["p1"]!["ApiKey"]!.GetValue<string>());
        Assert.Equal(ConfigLayerType.User, read.Origins["Providers"].Type);
    }

    [Fact]
    public async Task ProviderKeyPath_AddressesIdsContainingDots()
    {
        using var fixture = Create();

        await fixture.WriteAsync(ProviderConfiguration.ProviderKeyPath("openai.work", "EndPoint"), "\"https://work.example/v1\"");

        var providers = ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!.AsObject();
        Assert.Equal("https://work.example/v1", providers["openai.work"]!["EndPoint"]!.GetValue<string>());
    }

    [Fact]
    public async Task Write_MixedBatchWithoutFilePath_IsRejectedUntilItNamesTheUserLayer()
    {
        using var fixture = Create();
        ConfigEdit[] edits =
        [
            ConfigurationServiceFixture.Edit("Providers.p1", "{}"),
            ConfigurationServiceFixture.Edit("ProviderId", "\"p1\"")
        ];

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.Service.WriteAsync(edits, null, null, "test"));
        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.False(File.Exists(fixture.UserPath));
        Assert.False(File.Exists(fixture.WorkspacePath));

        await fixture.Service.WriteAsync(edits, fixture.UserPath, null, "test");

        var user = ConfigurationServiceFixture.ReadFile(fixture.UserPath);
        Assert.Equal("p1", user["ProviderId"]!.GetValue<string>());
        Assert.NotNull(user["Providers"]!["p1"]);
    }

    [Fact]
    public async Task Write_ProviderEditAimedAtTheWorkspaceLayer_IsRejected()
    {
        using var fixture = Create(workspace: """{ "Hub": { "Port": 4100 } }""");
        var before = File.ReadAllBytes(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() =>
            fixture.WriteAsync("Providers.p1.EndPoint", "\"https://p1.example\"", filePath: fixture.WorkspacePath));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));
        Assert.False(File.Exists(fixture.UserPath));
    }

    [Fact]
    public async Task ReplaceWorkspaceLayer_IsRejectedOnlyWhenItChangesProviders()
    {
        const string workspace = """{ "Providers": { "p1": { "EndPoint": "https://p1.example" } }, "Hub": { "Port": 4100 } }""";
        using var fixture = Create(workspace: workspace);
        var before = File.ReadAllBytes(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.Service.ReplaceLayerAsync(
            ConfigLayerType.Workspace,
            (JsonObject)JsonNode.Parse("""{ "Providers": { "p1": { "EndPoint": "https://p1.example" }, "p2": {} }, "Hub": { "Port": 4100 } }""")!,
            "dashboard"));
        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));

        await fixture.Service.ReplaceLayerAsync(
            ConfigLayerType.Workspace,
            (JsonObject)JsonNode.Parse("""{ "Providers": { "p1": { "EndPoint": "https://p1.example" } }, "Hub": { "Port": 4200 } }""")!,
            "dashboard");

        Assert.Equal(4200, ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath)["Hub"]!["Port"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("Providers.a/b", "{}")]
    [InlineData("Providers.a:b", "{}")]
    [InlineData("Providers.p1", "\"not-an-object\"")]
    [InlineData("Providers.p1", """{ "Protocol": "gemini" }""")]
    [InlineData("Providers.p1", """{ "EndPoint": "not a uri" }""")]
    [InlineData("Providers.p1", """{ "NetworkTimeoutSeconds": 0 }""")]
    [InlineData("Providers.p1", """{ "MaxOutputTokens": 0 }""")]
    [InlineData("Providers.p1", """{ "StreamIdleTimeoutMs": 0 }""")]
    [InlineData("Providers.p1", """{ "StreamMaxRetries": -1 }""")]
    [InlineData("Providers.p1", """{ "StreamMaxRetries": 101 }""")]
    [InlineData("Providers.p1", """{ "AuthMethod": "token" }""")]
    [InlineData("Providers.p1", """{ "AuthMethod": "chatgptOAuth", "Protocol": "anthropic" }""")]
    public async Task Write_InvalidProvider_IsRejectedAndLeavesTheFileUnchanged(string keyPath, string valueJson)
    {
        using var fixture = Create(user: """{ "Providers": { "p0": { "ApiKey": "sk-p0" } } }""");
        var before = File.ReadAllBytes(fixture.UserPath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync(keyPath, valueJson));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Contains("Providers", error.Message);
        Assert.DoesNotContain("not a uri", error.Message);
        Assert.Equal(before, File.ReadAllBytes(fixture.UserPath));
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Write_AcceptsBoundaryValuesAndCaseInsensitiveOptions()
    {
        using var fixture = Create();

        await fixture.WriteAsync(
            "Providers.p1",
            """{ "Protocol": "OpenAI-Responses", "AuthMethod": "CHATGPTOAUTH", "StreamMaxRetries": 0 }""");
        await fixture.WriteAsync("Providers.p2", """{ "StreamMaxRetries": 100, "EndPoint": "https://p2.example/v1" }""");

        Assert.Equal(2, ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!.AsObject().Count);
    }

    [Fact]
    public async Task Write_DoesNotRevalidateUnchangedProviders()
    {
        using var fixture = Create(user: """{ "Providers": { "unsupported": { "Protocol": "gemini" } } }""");

        await fixture.WriteAsync("Providers.p1", """{ "Protocol": "anthropic" }""");

        Assert.NotNull(ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!["p1"]);
    }

    [Fact]
    public async Task Write_DeletingTheSelectedProvider_IsRejected()
    {
        using var fixture = Create(
            user: """{ "Providers": { "p1": {}, "p2": {} } }""",
            workspace: """{ "ProviderId": "P1" }""");
        var before = File.ReadAllBytes(fixture.UserPath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync("Providers.p1", null));

        Assert.Equal("'ProviderId' must name a defined provider.", error.Message);
        Assert.Equal(before, File.ReadAllBytes(fixture.UserPath));
        await fixture.WriteAsync("Providers.p2", null);
        Assert.Null(ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!["p2"]);
    }

    [Fact]
    public async Task Write_SelectingAnUndefinedProvider_IsRejected()
    {
        using var fixture = Create(user: """{ "Providers": { "p1": {} } }""");

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync("ProviderId", "\"missing\""));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        await fixture.WriteAsync("ProviderId", "\"p1\"");
        Assert.Equal("p1", fixture.Monitor.Current.ProviderId);
    }

    [Fact]
    public async Task Write_KeepsAnAlreadyUnresolvedSelection()
    {
        using var fixture = Create(workspace: """{ "ProviderId": "missing" }""");

        await fixture.WriteAsync("Providers.p1", "{}");
        await fixture.WriteAsync("ProviderId", "\"missing\"");

        Assert.NotNull(ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Providers"]!["p1"]);
    }

    [Fact]
    public async Task RemoteModelService_RejectsProviderEditsAndKeepsTheServiceProviders()
    {
        using var fixture = Create(user: """{ "Providers": { "local": { "ApiKey": "sk-local" } } }""");
        var managed = ManageRemotely(fixture);
        var before = File.ReadAllBytes(fixture.UserPath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync("Providers.p1", "{}"));
        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Contains("model service", error.Message);
        await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.Service.ReplaceLayerAsync(
            ConfigLayerType.User,
            (JsonObject)JsonNode.Parse("""{ "Providers": {} }""")!,
            "dashboard"));
        Assert.Equal(before, File.ReadAllBytes(fixture.UserPath));

        await fixture.WriteAsync("ProviderId", "\"managed\"");
        await fixture.WriteAsync("InstantInterruptEnabled", "false");

        Assert.Same(managed, Assert.Single(fixture.Monitor.Current.Providers).Value);
        Assert.Equal("managed", fixture.Monitor.Current.ProviderId);
        Assert.DoesNotContain(fixture.Events, change => change.Regions.Contains("Providers"));
    }

    [Fact]
    public async Task RemoteModelService_SelectionMustNameAServiceManagedProvider()
    {
        using var fixture = Create(user: """{ "Providers": { "local": { "ApiKey": "sk-local" } } }""", workspace: "{}");
        ManageRemotely(fixture);
        var before = File.ReadAllBytes(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync("ProviderId", "\"local\""));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));
        await fixture.WriteAsync("ProviderId", "\"MANAGED\"");
        Assert.Equal("MANAGED", fixture.Monitor.Current.ProviderId);
    }

    [Fact]
    public async Task RemoteModelService_PreviousConfigurationThatFailsToBuild_DoesNotReplaceTheServiceProviders()
    {
        using var fixture = Create();
        var managed = ManageRemotely(fixture);
        ConfigurationServiceFixture.WriteFile(fixture.WorkspacePath, """{ "InstantInterruptEnabled": "yes" }""");

        await fixture.WriteAsync("InstantInterruptEnabled", "false");

        Assert.Same(managed, Assert.Single(fixture.Monitor.Current.Providers).Value);
    }

    private static ConfigurationServiceFixture Create(string? user = null, string? workspace = null)
    {
        var fixture = new ConfigurationServiceFixture(user, workspace);
        ConfigurationSubsystems.Register(fixture.Service, fixture.Monitor, null, null, null, null, null);
        return fixture;
    }

    private static AppConfig.ModelProviderConfig ManageRemotely(ConfigurationServiceFixture fixture)
    {
        var managed = new AppConfig.ModelProviderConfig { Protocol = ModelProviderProtocols.OpenAIResponses };
        fixture.Monitor.Current.ModelService = new AppConfig.ModelServiceConfig { Endpoint = "https://models.example", Token = "client" };
        fixture.Monitor.Current.Providers = new(StringComparer.OrdinalIgnoreCase) { ["managed"] = managed };
        return managed;
    }
}
