using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class WorkspaceConfigSchemaTests : IDisposable
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
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Initialize_WithWorkspaceConfigManagement_AdvertisesCapability()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(AppConfig)]);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            configSchema: schema);

        var initDoc = await harness.InitializeAsync();
        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(caps.GetProperty("workspaceConfigManagement").GetBoolean());
    }

    [Fact]
    public async Task WorkspaceConfigSchema_ReturnsSchemaSections()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(AppConfig)]);
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            configSchema: schema);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.WorkspaceConfigSchema, new { });
        await harness.ExecuteRequestAsync(msg);
        var doc = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var sections = doc.RootElement.GetProperty("result").GetProperty("sections");
        Assert.Equal(JsonValueKind.Array, sections.ValueKind);
        Assert.True(sections.GetArrayLength() > 0);
    }

    [Fact]
    public async Task WorkspaceConfigSchema_WithoutSchema_ReturnsMethodNotFound()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.WorkspaceConfigSchema, new { });
        await harness.ExecuteRequestAsync(msg);
        var doc = await harness.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.MethodNotFoundCode);
    }
}
