using System.Text.Json.Nodes;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed partial class PluginVerifierTests
{
    [Fact]
    public async Task ChannelPlugin_ReportsCompatibilitySeparatelyFromPublicationValidation()
    {
        var root = WriteChannelPlugin();
        var verification = await VerifyAsync(root, forPublish: true);
        Assert.True(verification.IsValid, Describe(verification));

        var channel = Assert.IsType<PluginChannelManifest>(PluginManifestParser.Load(root).Manifest?.Channel);
        Assert.Equal("ChannelPluginHostVersionUnsatisfied", channel.GetCompatibilityError("1.2.2"));
        Assert.Equal("ChannelPluginProtocolUnsupported",
            (channel with { SupportedChannelProtocolVersions = ["9.0"] }).GetCompatibilityError("1.2.3"));
    }

    [Fact]
    public async Task ChannelCategory_RequiresAnActualChannelContribution()
    {
        var root = WritePlugin(manifest => manifest["interface"]!["category"] = "Channels");
        var verification = await VerifyAsync(root);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, item => item.Code == "PluginChannelContributionRequired");
    }

    [Theory]
    [InlineData("config.json")]
    [InlineData("../somewhere.json")]
    public async Task ChannelVerifier_RejectsUnsafeWorkspaceConfigName(string configName)
    {
        var root = WriteChannelPlugin(configName);
        var verification = await VerifyAsync(root);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, item => item.Code == "InvalidPluginChannel");
    }

    [Fact]
    public async Task ChannelVerifier_RejectsLauncherEscapingThroughDirectoryLink()
    {
        var root = WriteChannelPlugin();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.Move(Path.Combine(root, "channel", "entry.mjs"), Path.Combine(outside, "entry.mjs"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "launcher"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var manifestPath = Path.Combine(root, ".craft-plugin", "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["channel"]!["entry"] = "./launcher/entry.mjs";
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var verification = await VerifyAsync(root);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, item => item.Code == "InvalidPluginChannel");
    }

    [Fact]
    public async Task ChannelVerifier_RejectsASecondPluginSettingsSchema()
    {
        var root = WriteChannelPlugin();
        File.WriteAllText(Path.Combine(root, "settings.schema.json"),
            """{"fields":[{"key":"other","type":"text"}]}""");
        var path = Path.Combine(root, ".craft-plugin", "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["settings"] = "./settings.schema.json";
        File.WriteAllText(path, manifest.ToJsonString());

        var verification = await VerifyAsync(root);
        Assert.False(verification.IsValid);
        Assert.Contains(verification.Diagnostics, item => item.Code == "PluginChannelSettingsConflict");
    }

    private string WriteChannelPlugin(string configName = "example.json")
    {
        var root = WritePlugin(manifest =>
        {
            manifest["capabilities"] = new JsonArray("skill", "channel");
            manifest["interface"]!["category"] = "Channels";
            manifest["channel"] = new JsonObject
            {
                ["manifest"] = "./channel/manifest.json",
                ["entry"] = "./channel/entry.mjs",
                ["minHostVersion"] = "1.2.3",
            };
        });
        var channel = Path.Combine(root, "channel");
        Directory.CreateDirectory(channel);
        File.WriteAllText(Path.Combine(channel, "entry.mjs"), "throw new Error('Must not run in verification');");
        File.WriteAllText(Path.Combine(channel, "manifest.json"), $$"""
            {
              "moduleId": "example-standard",
              "channelName": "example",
              "displayName": "Example",
              "packageName": "@example/channel",
              "configFileName": "{{configName}}",
              "variant": "standard",
              "channelContractVersion": "1.0.0",
              "supportedChannelProtocolVersions": ["0.2"],
              "supportedTransports": ["websocket"],
              "requiresInteractiveSetup": false,
              "capabilitySummary": {
                "hasChannelTools": false,
                "requiresInteractiveSetup": false,
                "capabilitySetMayVaryByEnvironment": false
              },
              "launcher": { "bin": "example", "supportsWorkspaceFlag": true, "supportsConfigOverrideFlag": true },
              "configDescriptors": [
                {
                  "key": "example.bots",
                  "displayLabel": "Bots",
                  "dataKind": "list",
                  "required": true,
                  "masked": true,
                  "interactiveSetupOnly": false
                }
              ]
            }
            """);
        return root;
    }
}
