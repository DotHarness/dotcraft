using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public class ExternalChannelConfigTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void AppConfig_Deserializes_ExternalChannels_ObjectMap_IntoStrongTypedList()
    {
        const string json = """
        {
          "ExternalChannels": {
            "telegram": {
              "enabled": true,
              "transport": "subprocess",
              "command": "python",
              "args": ["-m", "dotcraft_telegram"],
              "workingDirectory": "./adapters/telegram",
              "env": {
                "TELEGRAM_BOT_TOKEN": "secret"
              }
            },
            "weixin": {
              "enabled": true,
              "transport": "websocket"
            },
            "feishu": {
              "enabled": true,
              "transport": "managedWebsocket",
              "builtinModule": "channel-feishu"
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

        Assert.NotNull(config);
        Assert.Equal(3, config!.ExternalChannels.Count);

        var telegram = Assert.Single(config.ExternalChannels, c => c.Name == "telegram");
        Assert.True(telegram.Enabled);
        Assert.Equal(ExternalChannelTransport.Subprocess, telegram.Transport);
        Assert.Equal("python", telegram.Command);
        Assert.Equal(["-m", "dotcraft_telegram"], telegram.Args);
        Assert.Equal("./adapters/telegram", telegram.WorkingDirectory);
        Assert.Equal("secret", telegram.Env!["TELEGRAM_BOT_TOKEN"]);

        var weixin = Assert.Single(config.ExternalChannels, c => c.Name == "weixin");
        Assert.Equal(ExternalChannelTransport.Websocket, weixin.Transport);

        var feishu = Assert.Single(config.ExternalChannels, c => c.Name == "feishu");
        Assert.True(feishu.Enabled);
        Assert.Equal(ExternalChannelTransport.ManagedWebsocket, feishu.Transport);
        Assert.Equal("channel-feishu", feishu.BuiltinModule);
    }

    [Fact]
    public void AppConfig_Serializes_ExternalChannels_AsObjectMap_KeyedByName()
    {
        var config = new AppConfig
        {
            ExternalChannels =
            [
                new ExternalChannelEntry
                {
                    Name = "telegram",
                    Enabled = true,
                    Transport = ExternalChannelTransport.Subprocess,
                    Command = "python",
                    Args = ["-m", "dotcraft_telegram"],
                    WorkingDirectory = "./adapters/telegram",
                    Env = new Dictionary<string, string> { ["TELEGRAM_BOT_TOKEN"] = "secret" }
                },
                new ExternalChannelEntry
                {
                    Name = "weixin",
                    Enabled = true,
                    Transport = ExternalChannelTransport.Websocket
                },
                new ExternalChannelEntry
                {
                    Name = "feishu",
                    Enabled = true,
                    Transport = ExternalChannelTransport.ManagedWebsocket,
                    BuiltinModule = "channel-feishu"
                }
            ]
        };

        var node = JsonSerializer.SerializeToNode(config, SerializerOptions) as JsonObject;
        Assert.NotNull(node);

        var external = Assert.IsType<JsonObject>(node!["ExternalChannels"]);
        Assert.NotNull(external["telegram"]);
        Assert.NotNull(external["weixin"]);
        Assert.NotNull(external["feishu"]);

        var telegram = Assert.IsType<JsonObject>(external["telegram"]);
        Assert.False(telegram.ContainsKey("Name"));
        Assert.Equal("python", telegram["Command"]?.GetValue<string>());

        var weixin = Assert.IsType<JsonObject>(external["weixin"]);
        Assert.False(weixin.ContainsKey("Name"));
        Assert.Equal("Websocket", weixin["Transport"]?.GetValue<string>());

        var feishu = Assert.IsType<JsonObject>(external["feishu"]);
        Assert.False(feishu.ContainsKey("Name"));
        Assert.Equal("ManagedWebsocket", feishu["Transport"]?.GetValue<string>());
        Assert.Equal("channel-feishu", feishu["BuiltinModule"]?.GetValue<string>());
    }

    [Fact]
    public void ExternalChannels_CaseInsensitiveDuplicateKeys_LastEntryWinsInMap()
    {
        const string json = """
        {
          "ExternalChannels": {
            "Telegram": {
              "enabled": false,
              "transport": "websocket"
            },
            "telegram": {
              "enabled": true,
              "transport": "subprocess",
              "command": "python"
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);

        Assert.NotNull(config);
        Assert.Equal(2, config!.ExternalChannels.Count);

        var map = ExternalChannelEntryMap.ToDictionaryByNameLastWins(config.ExternalChannels);

        Assert.Single(map);
        var entry = map["telegram"];
        Assert.True(entry.Enabled);
        Assert.Equal(ExternalChannelTransport.Subprocess, entry.Transport);
        Assert.Equal("python", entry.Command);
    }
}
