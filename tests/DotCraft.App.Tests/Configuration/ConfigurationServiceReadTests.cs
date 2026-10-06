using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigurationServiceReadTests
{
    [Fact]
    public void Read_OverlaysDefaultsWithUserThenWorkspace_AndReportsFileOrigins()
    {
        using var fixture = new ConfigurationServiceFixture(
            user: """{ "Tools": { "Lsp": { "Enabled": true, "MaxFileSize": 100 } }, "InstantInterruptEnabled": false }""",
            workspace: """{ "tools": { "lsp": { "enabled": false } }, "Hub": { "Port": 4100 } }""");

        var result = fixture.Service.Read(includeLayers: true);

        Assert.False(result.Config["Tools"]!["Lsp"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(100, result.Config["Tools"]!["Lsp"]!["MaxFileSize"]!.GetValue<int>());
        Assert.False(result.Config["InstantInterruptEnabled"]!.GetValue<bool>());
        Assert.True(result.Config["Memory"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(4100, result.Config["Hub"]!["Port"]!.GetValue<int>());

        var user = result.Layers![0].Layer;
        var workspace = result.Layers[1].Layer;
        Assert.Equal(ConfigLayerType.User, user.Type);
        Assert.Equal(ConfigLayerType.Workspace, workspace.Type);
        Assert.Equal(workspace, result.Origins["Tools.Lsp.Enabled"]);
        Assert.Equal(user, result.Origins["Tools.Lsp.MaxFileSize"]);
        Assert.Equal(user, result.Origins["InstantInterruptEnabled"]);
        Assert.Equal(workspace, result.Origins["Hub.Port"]);
        Assert.DoesNotContain("Memory.Enabled", result.Origins.Keys);
        Assert.Null(fixture.Service.Read().Layers);
    }

    [Fact]
    public void Read_LayerVersionDependsOnContentOnly()
    {
        using var empty = new ConfigurationServiceFixture();
        using var compact = new ConfigurationServiceFixture(workspace: """{"b":1,"a":{"y":2,"x":1}}""");
        using var spaced = new ConfigurationServiceFixture(workspace: "{\n  \"a\": { \"x\": 1, \"y\": 2 },\n  \"b\": 1\n}\n");
        using var emptyObject = new ConfigurationServiceFixture(workspace: "{ }");

        var missing = empty.Service.Read(includeLayers: true).Layers!;
        Assert.StartsWith("sha256:", missing[1].Layer.Version, StringComparison.Ordinal);
        Assert.Equal(missing[0].Layer.Version, missing[1].Layer.Version);
        Assert.Equal(missing[1].Layer.Version, emptyObject.Service.Read(includeLayers: true).Layers![1].Layer.Version);
        Assert.Equal(
            compact.Service.Read(includeLayers: true).Layers![1].Layer.Version,
            spaced.Service.Read(includeLayers: true).Layers![1].Layer.Version);
        Assert.NotEqual(missing[1].Layer.Version, compact.Service.Read(includeLayers: true).Layers![1].Layer.Version);
    }

    [Fact]
    public void Read_MasksSensitiveValuesInEffectiveConfigAndLayers()
    {
        using var fixture = new ConfigurationServiceFixture(
            user: """{ "DashBoard": { "Password": "secret" }, "Providers": { "custom": { "ApiKey": "key" } } }""",
            workspace: """{ "AppServer": { "WebSocket": { "Token": "token" } } }""");

        var result = fixture.Service.Read(includeLayers: true);

        Assert.Equal("***", result.Config["DashBoard"]!["Password"]!.GetValue<string>());
        Assert.Equal("***", result.Config["Providers"]!["custom"]!["ApiKey"]!.GetValue<string>());
        Assert.Equal("***", result.Config["AppServer"]!["WebSocket"]!["Token"]!.GetValue<string>());
        Assert.Equal("***", result.Layers![0].Config["DashBoard"]!["Password"]!.GetValue<string>());
        Assert.Equal("***", result.Layers[1].Config["AppServer"]!["WebSocket"]!["Token"]!.GetValue<string>());
        Assert.Contains("secret", File.ReadAllText(fixture.UserPath), StringComparison.Ordinal);
    }
}
