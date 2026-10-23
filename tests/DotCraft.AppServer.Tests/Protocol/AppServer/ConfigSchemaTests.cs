using System.Text.Json;
using DotCraft.AppServer;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using ConfigSchemaField = DotCraft.Configuration.ConfigSchemaField;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ConfigSchemaTests : IDisposable
{
    private readonly string _workspaceCraftPath = Path.Combine(Path.GetTempPath(), $"workspace_schema_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspaceCraftPath))
                Directory.Delete(_workspaceCraftPath, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ConfigSchema_IsAdvertisedAndReturnsSchemaSections()
    {
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            configSchema: CreateTestSchema());
        using (var initDoc = await harness.InitializeAsync())
            Assert.True(initDoc.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("workspaceConfigManagement").GetBoolean());

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigSchema, new { });
        await harness.ExecuteRequestAsync(msg);
        var doc = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var sections = doc.RootElement.GetProperty("result").GetProperty("sections");
        Assert.Equal(JsonValueKind.Array, sections.ValueKind);
        Assert.True(sections.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ConfigSchema_WithoutSchema_ReturnsMethodNotFound()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigSchema, new { });
        await harness.ExecuteRequestAsync(msg);
        var doc = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.MethodNotFoundCode);
    }

    private static IReadOnlyList<ConfigSchemaSection> CreateTestSchema() =>
    [
        new ConfigSchemaSection
        {
            Section = "Core",
            Fields =
            [
                new ConfigSchemaField
                {
                    Key = "Theme",
                    Type = "text",
                    DefaultValue = "dark"
                }
            ]
        }
    ];
}
