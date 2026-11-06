using System.Text.Json.Nodes;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginVerifierTests : IDisposable
{
    private const string SquareIcon =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" fill="#2563EB"/></svg>""";

    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-plugin-verifier-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompletePlugin_IsValid()
    {
        var result = await VerifyAsync(WritePlugin());

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal("review-tools", result.PluginId);
        Assert.Equal("0.1.0", result.Version);
    }

    [Fact]
    public async Task MissingManifest_IsInvalid()
    {
        Directory.CreateDirectory(_root);

        var result = await VerifyAsync(_root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "PluginManifestMissing");
    }

    [Fact]
    public async Task PluginWithoutPresentation_LoadsButFailsVerification()
    {
        var root = WritePlugin(manifest => manifest.Remove("interface"));

        var parsed = PluginManifestParser.Load(root);
        var result = await VerifyAsync(root);

        Assert.NotNull(parsed.Manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginMetadataMissing" } && item.Message.Contains("developerName"));
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginIconMissing" } && item.Message.Contains("logo"));
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginIconMissing" } && item.Message.Contains("composerIcon"));
    }

    [Fact]
    public async Task RemainingPlaceholders_AreErrors()
    {
        var root = WritePlugin(manifest => manifest["description"] = "[TODO: one sentence]");
        File.WriteAllText(
            Path.Combine(root, "skills", "review", "SKILL.md"),
            "---\nname: review\ndescription: Review changes.\n---\n\n[TODO: steps]\n");

        var result = await VerifyAsync(root);

        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginPlaceholderRemaining", Path: ".craft-plugin/plugin.json" });
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginPlaceholderRemaining", Path: "skills/review/SKILL.md" });
    }

    [Fact]
    public async Task CopyLimitsAndCategory_AreEnforced()
    {
        var root = WritePlugin(manifest =>
        {
            var metadata = manifest["interface"]!.AsObject();
            metadata["shortDescription"] = new string('x', 49);
            metadata["defaultPrompt"] = "first line\nsecond line";
            metadata["category"] = "coding";
        });

        var result = await VerifyAsync(root);

        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginMetadataTooLong" } && item.Message.Contains("shortDescription"));
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginMetadataNotSingleLine" });
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginCategoryInvalid" });
    }

    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 32"/>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><script/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><use href="https://example.com/a.svg#b"/></svg>""")]
    [InlineData("not xml")]
    public async Task UnacceptableSvg_IsAnError(string svg)
    {
        var root = WritePlugin();
        File.WriteAllText(Path.Combine(root, "assets", "icon.svg"), svg);

        var result = await VerifyAsync(root);

        Assert.Contains(result.Diagnostics, item => item is { Severity: "error", Code: "PluginIconInvalid" });
    }

    [Fact]
    public async Task RasterIcon_MustMeetMinimumSizeAndMatchItsExtension()
    {
        var root = WritePlugin(manifest =>
        {
            var metadata = manifest["interface"]!.AsObject();
            metadata["logo"] = "./assets/small.png";
            metadata["composerIcon"] = "./assets/fake.png";
        });
        File.WriteAllBytes(Path.Combine(root, "assets", "small.png"), Convert.FromBase64String(OnePixelPngBase64));
        File.WriteAllText(Path.Combine(root, "assets", "fake.png"), "not an image");

        var result = await VerifyAsync(root);

        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginIconInvalid" } && item.Message.Contains("256"));
        Assert.Contains(result.Diagnostics, item => item is { Code: "PluginIconInvalid" } && item.Message.Contains("not a valid"));
    }

    [Fact]
    public async Task WarningsDoNotFailVerification()
    {
        var root = WritePlugin(manifest =>
        {
            manifest["capabilities"] = new JsonArray("metadata");
            manifest["interface"]!["defaultPrompt"] = "Use Review Tools.";
        });
        File.WriteAllText(
            Path.Combine(root, "assets", "icon.svg"),
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><text>R</text></svg>""");

        var result = await VerifyAsync(root);

        Assert.True(result.IsValid, Describe(result));
        Assert.Contains(result.Diagnostics, item => item is { Severity: "warning", Code: "PluginCapabilityMismatch" });
        Assert.Contains(result.Diagnostics, item => item is { Severity: "warning", Code: "PluginDefaultPromptGeneric" });
        Assert.Contains(result.Diagnostics, item => item is { Severity: "warning", Code: "PluginIconFontText" });
    }

    [Fact]
    public async Task DescriptorsAndSkills_FollowRuntimeRules()
    {
        var root = WritePlugin(manifest => manifest["mcpServers"] = "./.mcp.json");
        File.WriteAllText(Path.Combine(root, ".mcp.json"), "{ not json");
        File.WriteAllText(
            Path.Combine(root, "skills", "review", "SKILL.md"),
            "---\nname: other\ndescription: Review changes.\n---\n\nSteps.\n");

        var result = await VerifyAsync(root);

        Assert.Contains(result.Diagnostics, item => item is { Severity: "error", Code: "InvalidPluginMcpConfig" });
        Assert.Contains(result.Diagnostics, item => item is { Code: "InvalidPluginSkill" } && item.Message.Contains("other"));
    }

    [Fact]
    public async Task PublicationChecks_ApplyOnlyWhenRequested()
    {
        var root = WritePlugin(manifest =>
        {
            manifest["id"] = "Review_Tools";
            manifest["version"] = "1.0";
            manifest["mcpServers"] = "./.mcp.json";
        });
        File.WriteAllText(
            Path.Combine(root, ".mcp.json"),
            """
            {
              "mcpServers": {
                "review": {
                  "transport": "stdio",
                  "command": "C:\\tools\\server.exe",
                  "environmentVariables": { "API_TOKEN": "literal-value", "OTHER_TOKEN": "${OTHER_TOKEN}" }
                }
              }
            }
            """);
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        File.WriteAllText(Path.Combine(root, "notes.txt"), "ghp_" + new string('a', 36));

        var local = await VerifyAsync(root);
        var publish = await VerifyAsync(root, forPublish: true);

        Assert.True(local.IsValid, Describe(local));
        Assert.False(publish.IsValid);
        Assert.Contains(publish.Diagnostics, item => item.Code == "PluginVersionInvalid");
        Assert.Contains(publish.Diagnostics, item => item.Code == "PluginIdNotHyphenCase");
        Assert.Contains(publish.Diagnostics, item => item.Code == "PluginContainsDevelopmentArtifact");
        Assert.Contains(publish.Diagnostics, item => item is { Code: "PluginContainsCredential", Path: "notes.txt" });
        Assert.Contains(publish.Diagnostics, item => item.Code == "PluginAbsolutePath");
        Assert.Single(publish.Diagnostics, item => item.Code == "PluginLiteralCredential");
        Assert.DoesNotContain(publish.Diagnostics, item => item.Message.Contains("ghp_"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Task<PluginVerificationResult> VerifyAsync(string path, bool forPublish = false) =>
        PluginVerifier.VerifyAsync(new PluginVerificationRequest(path, forPublish));

    private static string Describe(PluginVerificationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Severity} {item.Code}: {item.Message}"));

    private string WritePlugin(Action<JsonObject>? edit = null)
    {
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "skills", "review"));

        var manifest = JsonNode.Parse(
            """
            {
              "schemaVersion": 1,
              "id": "review-tools",
              "version": "0.1.0",
              "displayName": "Review Tools",
              "description": "Reviews pull requests against your team's written rules.",
              "capabilities": ["skill"],
              "skills": "./skills/",
              "interface": {
                "displayName": "Review Tools",
                "shortDescription": "Review pull requests against team rules",
                "developerName": "Acme",
                "category": "Coding",
                "capabilities": ["Skill"],
                "defaultPrompt": "Review this branch against our review rules.",
                "websiteUrl": "https://example.com/review-tools",
                "logo": "./assets/icon.svg",
                "composerIcon": "./assets/icon.svg"
              }
            }
            """)!.AsObject();
        edit?.Invoke(manifest);

        File.WriteAllText(Path.Combine(root, ".craft-plugin", "plugin.json"), manifest.ToJsonString());
        File.WriteAllText(Path.Combine(root, "assets", "icon.svg"), SquareIcon);
        File.WriteAllText(
            Path.Combine(root, "skills", "review", "SKILL.md"),
            "---\nname: review\ndescription: Review changes against team rules.\n---\n\nRead the rules, then review.\n");
        return root;
    }
}
