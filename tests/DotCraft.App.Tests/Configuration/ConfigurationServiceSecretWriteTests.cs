using System.Text.Json;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceSecretWriteTests
{
    private const string Secret = "k1-secret";
    private const string StoredUser = """{ "DashBoard": { "Password": "k1-secret" } }""";

    [Theory]
    [InlineData("DashBoard.Password")]
    [InlineData("AppServer.WebSocket.Token")]
    public async Task Write_SensitiveField_StoresValueThatReadsBackMasked(string keyPath)
    {
        using var fixture = new ConfigurationServiceFixture();

        var result = await fixture.WriteAsync(keyPath, $"\"{Secret}\"", filePath: fixture.UserPath);

        Assert.Equal(ConfigWriteStatus.Ok, result.Status);
        var path = keyPath.Split('.');
        Assert.True(ConfigJsonPath.TryGet(ConfigurationServiceFixture.ReadFile(fixture.UserPath), path, out var stored));
        Assert.Equal(Secret, stored!.GetValue<string>());
        var read = fixture.Service.Read(includeLayers: true);
        Assert.True(ConfigJsonPath.TryGet(read.Config, path, out var effective));
        Assert.Equal("***", effective!.GetValue<string>());
        Assert.True(ConfigJsonPath.TryGet(read.Layers![0].Config, path, out var layer));
        Assert.Equal("***", layer!.GetValue<string>());
        Assert.Equal([keyPath], Assert.Single(fixture.Events).Regions);
    }

    [Fact]
    public async Task Write_Mask_KeepsStoredValue_NeverStoresTheMask_AndRaisesNoEvent()
    {
        using var fixture = new ConfigurationServiceFixture(user: StoredUser);
        var before = File.ReadAllBytes(fixture.UserPath);

        await fixture.WriteAsync("DashBoard.Password", "\"***\"", filePath: fixture.UserPath);
        await fixture.WriteAsync("DashBoard.Password", "\"***\"");

        Assert.Equal(before, File.ReadAllBytes(fixture.UserPath));
        Assert.False(File.Exists(fixture.WorkspacePath)
            && AtomicConfigDocument.Key(ConfigurationServiceFixture.ReadFile(fixture.WorkspacePath), "DashBoard") is not null);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Write_Null_RemovesStoredSecret()
    {
        using var fixture = new ConfigurationServiceFixture(user: StoredUser);

        await fixture.WriteAsync("DashBoard.Password", null, filePath: fixture.UserPath);

        Assert.Empty(ConfigurationServiceFixture.ReadFile(fixture.UserPath));
        Assert.Equal(["DashBoard.Password"], Assert.Single(fixture.Events).Regions);
    }

    [Fact]
    public async Task Write_UpsertWithNestedMasks_RestoresStoredSecretAndAppliesOtherFields()
    {
        using var fixture = new ConfigurationServiceFixture(user: """
            {
              "Desktop": {
                "CustomFileHandlers": {
                  "review": { "Label": "Review", "Command": "review", "AccessToken": "k1-secret" }
                }
              }
            }
            """);

        await fixture.WriteAsync(
            "Desktop.CustomFileHandlers",
            """{ "review": { "Label": "Renamed", "AccessToken": "***", "RefreshToken": "***" } }""",
            ConfigMergeStrategy.Upsert,
            filePath: fixture.UserPath);

        var handler = ConfigurationServiceFixture.ReadFile(fixture.UserPath)["Desktop"]!["CustomFileHandlers"]!["review"]!;
        Assert.Equal("Renamed", handler["Label"]!.GetValue<string>());
        Assert.Equal("review", handler["Command"]!.GetValue<string>());
        Assert.Equal(Secret, handler["AccessToken"]!.GetValue<string>());
        Assert.Null(handler["RefreshToken"]);
    }

    [Fact]
    public async Task Write_OverriddenSensitiveField_ReturnsMaskedEffectiveValue()
    {
        using var fixture = new ConfigurationServiceFixture(workspace: """{ "DashBoard": { "Password": "workspace-secret" } }""");

        var result = await fixture.WriteAsync("DashBoard.Password", $"\"{Secret}\"", filePath: fixture.UserPath);

        Assert.Equal(ConfigWriteStatus.OkOverridden, result.Status);
        Assert.Equal("***", result.OverriddenMetadata!.EffectiveValue!.GetValue<string>());
        Assert.DoesNotContain("workspace-secret", JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("Hub.Port", "70000", null)]
    [InlineData("Hub.Port", "4200", "sha256:00")]
    public async Task Write_RejectedBatchCarryingSecret_ErrorOmitsTheSecret(string keyPath, string valueJson, string? expectedVersion)
    {
        using var fixture = new ConfigurationServiceFixture(user: StoredUser);

        var error = await Assert.ThrowsAsync<ConfigWriteException>(() => fixture.Service.WriteAsync(
            [
                ConfigurationServiceFixture.Edit("DashBoard.Password", "\"new-secret\""),
                ConfigurationServiceFixture.Edit(keyPath, valueJson)
            ],
            filePath: fixture.UserPath,
            expectedVersion: expectedVersion,
            source: "test"));

        Assert.DoesNotContain("new-secret", error.Message);
        Assert.DoesNotContain(Secret, error.Message);
        Assert.Equal(Secret, ConfigurationServiceFixture.ReadFile(fixture.UserPath)["DashBoard"]!["Password"]!.GetValue<string>());
    }
}
