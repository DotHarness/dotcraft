using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceWriteTests
{
    private const string ExistingWorkspace = "{\n  \"tools\": { \"lsp\": { \"enabled\": false } },\n  \"Hub\": { \"Port\": 4100 }\n}\n";

    [Fact]
    public async Task Write_DefaultsToWorkspaceLayer_AndReturnsItsNewVersion()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);

        var result = await fixture.WriteAsync("Tools.Lsp.Enabled", "true");

        Assert.Equal(ConfigWriteStatus.Ok, result.Status);
        Assert.Equal(Path.GetFullPath(fixture.WorkspacePath), result.FilePath);
        var file = ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath);
        Assert.True(file["tools"]!["lsp"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(4100, file["Hub"]!["Port"]!.GetValue<int>());
        Assert.Equal(result.Version, fixture.Service.Read(includeLayers: true).Layers![1].Layer.Version);
    }

    [Fact]
    public async Task Write_KeepsNonAsciiTextReadableInTheFile()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);

        await fixture.WriteAsync("ProviderId", "\"本地模型\"");

        Assert.Contains("\"本地模型\"", File.ReadAllText(fixture.WorkspacePath));
    }

    [Theory]
    [InlineData("Tools.Lsp.Enabled", "true", "other", null, ConfigWriteErrorCode.ConfigLayerReadonly)]
    [InlineData("Tools.Lsp.Enabled", "true", null, "sha256:00", ConfigWriteErrorCode.ConfigVersionConflict)]
    [InlineData("Tools.Lsp.Missing", "true", null, null, ConfigWriteErrorCode.ConfigSchemaUnknownKey)]
    [InlineData("Tools.Lsp", "{}", null, null, ConfigWriteErrorCode.ConfigSchemaUnknownKey)]
    [InlineData("ProviderPreferences.", "{}", null, null, ConfigWriteErrorCode.ConfigSchemaUnknownKey)]
    [InlineData("DashBoard.Password", "\"secret\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("InstantInterruptEnabled", "\"yes\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("Tools.Web.SearchMaxResults", "50", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("Logging.MinLevel", "\"Loud\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("Tools.CodeMode.Mode", "\"Sometimes\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("Hub.Port", "\"high\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("Hub.Port", "70000", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    [InlineData("ProviderPreferences.custom", "\"not-an-object\"", null, null, ConfigWriteErrorCode.ConfigValidationError)]
    public async Task Write_Rejected_LeavesFileByteIdentical(
        string keyPath,
        string valueJson,
        string? fileName,
        string? expectedVersion,
        ConfigWriteErrorCode expected)
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);
        var before = File.ReadAllBytes(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync(
            keyPath,
            valueJson,
            filePath: fileName == null ? null : fixture.Scratch(fileName),
            expectedVersion: expectedVersion));

        Assert.Equal(expected, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Write_BatchWithOneInvalidEdit_WritesNothing()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);
        var before = File.ReadAllBytes(fixture.WorkspacePath);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.Service.WriteAsync(
            [
                ConfigurationServiceFixture.Edit("Tools.Lsp.Enabled", "true"),
                ConfigurationServiceFixture.Edit("Tools.Web.SearchMaxResults", "0")
            ],
            filePath: null,
            expectedVersion: null,
            source: "test"));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));
        Assert.False(fixture.Monitor.Current.Tools.Lsp.Enabled);
    }

    [Fact]
    public async Task Write_MatchingExpectedVersion_IsAccepted()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);
        var version = fixture.Service.Read(includeLayers: true).Layers![1].Layer.Version;

        var result = await fixture.WriteAsync("Hub.Port", "4200", expectedVersion: version);

        Assert.NotEqual(version, result.Version);
    }

    [Fact]
    public async Task Write_NullRemovesKeyAndPrunesEmptyParents()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);

        await fixture.WriteAsync("Tools.Lsp.Enabled", null);

        var file = ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath);
        Assert.Null(AtomicConfigDocument.Key(file, "Tools"));
        Assert.Equal(4100, file["Hub"]!["Port"]!.GetValue<int>());
    }

    [Fact]
    public async Task Write_UpsertDeepMergesObject_ReplaceSetsWholeValue()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: """
            {
              "ProviderPreferences": {
                "anthropic": { "Model": "claude" },
                "openai": { "Model": "old", "Reasoning": { "Enabled": true } }
              }
            }
            """);

        await fixture.WriteAsync("ProviderPreferences", """{ "openai": { "Model": "new" } }""", ConfigMergeStrategy.Upsert);
        var upserted = ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath)["ProviderPreferences"]!;
        Assert.Equal("claude", upserted["anthropic"]!["Model"]!.GetValue<string>());
        Assert.Equal("new", upserted["openai"]!["Model"]!.GetValue<string>());
        Assert.True(upserted["openai"]!["Reasoning"]!["Enabled"]!.GetValue<bool>());

        await fixture.WriteAsync("ProviderPreferences.openai", """{ "Model": "replaced" }""");
        var replaced = ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath)["ProviderPreferences"]!;
        Assert.Equal("replaced", replaced["openai"]!["Model"]!.GetValue<string>());
        Assert.Null(replaced["openai"]!["Reasoning"]);
        Assert.Equal("replaced", fixture.Monitor.Current.ProviderPreferences["openai"].Model);
    }

    [Fact]
    public async Task Write_UserValueHiddenByWorkspace_ReportsOverridden()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: """{ "InstantInterruptEnabled": false }""");

        var result = await fixture.WriteAsync("InstantInterruptEnabled", "true", filePath: fixture.UserPath);

        Assert.Equal(ConfigWriteStatus.OkOverridden, result.Status);
        Assert.Equal(Path.GetFullPath(fixture.UserPath), result.FilePath);
        Assert.Equal(ConfigLayerType.Workspace, result.OverriddenMetadata!.OverridingLayer.Type);
        Assert.False(result.OverriddenMetadata.EffectiveValue!.GetValue<bool>());
        Assert.True(ConfigurationServiceFixture.ReadFile(fixture.UserPath)["InstantInterruptEnabled"]!.GetValue<bool>());
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Write_SemanticValidatorRunsForItsKeyPath_AndRejectsAtomically()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: ExistingWorkspace);
        var calls = 0;
        fixture.Service.RegisterValidator("Dreams.Interval", config =>
        {
            calls++;
            return config.Dreams.Interval <= TimeSpan.Zero ? "Dreams interval must be positive." : null;
        });

        await fixture.WriteAsync("Dreams.AutoApply", "true");
        Assert.Equal(0, calls);

        var before = File.ReadAllBytes(fixture.WorkspacePath);
        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.WriteAsync("Dreams.Interval", "\"00:00:00\""));

        Assert.Equal(ConfigWriteErrorCode.ConfigValidationError, error.Code);
        Assert.Equal(1, calls);
        Assert.Equal(before, File.ReadAllBytes(fixture.WorkspacePath));
    }

    [Fact]
    public async Task Write_ExistingOutOfRangeValueElsewhere_DoesNotBlockUnrelatedWrite()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: "{ \"Hub\": { \"Port\": 70000 } }");

        var result = await fixture.WriteAsync("Tools.Lsp.Enabled", "true");

        Assert.Equal(["Tools.Lsp.Enabled"], Assert.Single(fixture.Events).Regions);
        var file = ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath);
        Assert.True(file["Tools"]!["Lsp"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(70000, file["Hub"]!["Port"]!.GetValue<int>());
    }
}
