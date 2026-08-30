using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task Initialize_ReportsPluginConfigurationCapability()
    {
        using var harness = CreateHarness();
        using var initialize = await harness.InitializeAsync();

        Assert.True(initialize.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("pluginConfiguration")
            .GetBoolean());
    }

    [Fact]
    public async Task PluginConfigGet_ReturnsSchemaLayersAndEffectiveValue()
    {
        WriteConfigurablePluginFixture("config-sample");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginConfigGet,
            new { id = "config-sample" }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("comfortable", result.GetProperty("value").GetProperty("density").GetString());
        Assert.Empty(result.GetProperty("personal").EnumerateObject());
        Assert.Empty(result.GetProperty("workspace").EnumerateObject());
        Assert.Equal(2, result.GetProperty("writableScopes").GetArrayLength());
        var field = Assert.Single(result.GetProperty("schema").GetProperty("fields").EnumerateArray());
        Assert.Equal("select", field.GetProperty("type").GetString());
    }

    [Fact]
    public async Task PluginConfigMutate_WritesRequestedScopeAndEmitsConfigChange()
    {
        WriteConfigurablePluginFixture("config-sample");
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        var changes = new List<AppConfigChangedEventArgs>();
        harness.Monitor.Changed += OnChanged;
        try
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginConfigMutate,
                new
                {
                    id = "config-sample",
                    scope = "workspace",
                    operations = new[] { new { op = "set", key = "density", value = "compact" } }
                }));

            using var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            Assert.Equal("compact", result.GetProperty("workspace").GetProperty("density").GetString());
            Assert.Equal("compact", result.GetProperty("value").GetProperty("density").GetString());
            var change = Assert.Single(changes);
            Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginConfigMutate, change.Source);
            Assert.Equal([ConfigChangeRegions.PluginConfiguration], change.Regions);
            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(_workspaceCraftPath, "plugin-config.json")));
            Assert.Equal(
                "compact",
                document.RootElement.GetProperty("config-sample").GetProperty("density").GetString());
        }
        finally
        {
            harness.Monitor.Changed -= OnChanged;
        }

        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task PluginConfigMutate_RejectsInvalidValueWithoutWriting()
    {
        WriteConfigurablePluginFixture("config-sample");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginConfigMutate,
            new
            {
                id = "config-sample",
                scope = "workspace",
                operations = new[] { new { op = "set", key = "density", value = "unknown" } }
            }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.PluginConfigurationErrorCode);
        Assert.Equal(
            DotCraft.Plugins.PluginConfigStore.MutationInvalid,
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Assert.False(File.Exists(Path.Combine(_workspaceCraftPath, "plugin-config.json")));
    }

    [Fact]
    public async Task InvalidNamespace_IsAStableErrorAndDoesNotDisablePluginList()
    {
        WriteConfigurablePluginFixture("config-sample");
        File.WriteAllText(
            Path.Combine(_workspaceCraftPath, "plugin-config.json"),
            """{"config-sample":{"unknown":true}}""");
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginConfigGet,
            new { id = "config-sample" }));
        using var getResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(getResponse, AppServerErrors.PluginConfigurationErrorCode);
        Assert.Equal(
            DotCraft.Plugins.PluginConfigStore.NamespaceInvalid,
            getResponse.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));
        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.Contains(
            listResponse.RootElement.GetProperty("result").GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == DotCraft.Plugins.PluginConfigStore.NamespaceInvalid
                          && diagnostic.GetProperty("pluginId").GetString() == "config-sample");
    }

    private void WriteConfigurablePluginFixture(string id)
    {
        var root = Path.Combine(_workspaceCraftPath, "plugins", id);
        Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(root, "skills"));
        File.WriteAllText(
            Path.Combine(root, "settings.schema.json"),
            """
            {
              "fields": [
                {
                  "key": "density",
                  "type": "select",
                  "defaultValue": "comfortable",
                  "options": ["compact", "comfortable"]
                }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(root, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "displayName": "Configuration Sample",
              "skills": "./skills",
              "settings": "./settings.schema.json"
            }
            """);
    }
}
