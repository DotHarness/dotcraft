using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ChannelConfigTests
{
    [Fact]
    public void ChannelSelectionsComeOnlyFromTheWorkspaceLayer()
    {
        var root = Directory.CreateTempSubdirectory("dotcraft-channel-config-");
        try
        {
            var personal = Path.Combine(root.FullName, "personal.json");
            var workspace = Path.Combine(root.FullName, "workspace.json");
            File.WriteAllText(personal, """
                { "Channels": { "qq": { "pluginId": "channel-qq", "enabled": true, "pluginActivationId": "personal" } } }
                """);
            File.WriteAllText(workspace, """
                { "channels": { "QQ": { "pluginId": "qq-community", "enabled": false } } }
                """);

            var selection = Assert.Single(AppConfig.LoadWithGlobalFallback(workspace, personal).Channels);
            Assert.Equal("QQ", selection.Name);
            Assert.Equal("qq-community", selection.PluginId);
            Assert.False(selection.Enabled);
            Assert.Null(selection.PluginActivationId);

            File.WriteAllText(workspace, "{}");
            Assert.Empty(AppConfig.LoadWithGlobalFallback(workspace, personal).Channels);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
