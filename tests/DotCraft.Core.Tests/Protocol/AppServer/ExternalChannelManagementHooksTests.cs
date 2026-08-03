using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ExternalChannelManagementHooksTests : IDisposable
{
    private readonly string _workspaceCraftPath;
    private AppServerTestHarness? _h;

    public ExternalChannelManagementHooksTests()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ExternalChannelHooks_{Guid.NewGuid():N}");
        _workspaceCraftPath = Path.Combine(tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        _h?.Dispose();
        try
        {
            var root = Directory.GetParent(_workspaceCraftPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public async Task ExternalChannelUpsert_InvokesHookAfterPersist()
    {
        var hookCalls = 0;
        var persistedBeforeHook = false;
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            onExternalChannelUpserted: (entry, _) =>
            {
                hookCalls++;
                var configPath = Path.Combine(_workspaceCraftPath, "config.json");
                var channels = AppConfig.Load(configPath).ExternalChannels;
                persistedBeforeHook = channels.Any(c =>
                    string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            });

        await _h.InitializeAsync();
        var request = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "weixin",
                enabled = true,
                transport = "websocket"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        Assert.Equal(1, hookCalls);
        Assert.True(persistedBeforeHook);
    }

    [Fact]
    public async Task ExternalChannelRemove_InvokesHookAfterPersist()
    {
        var removedName = string.Empty;
        var removedFromConfigBeforeHook = false;
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            onExternalChannelRemoved: (name, _) =>
            {
                removedName = name;
                var configPath = Path.Combine(_workspaceCraftPath, "config.json");
                var channels = AppConfig.Load(configPath).ExternalChannels;
                removedFromConfigBeforeHook = channels.All(c =>
                    !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            });

        await _h.InitializeAsync();

        var upsert = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "weixin",
                enabled = true,
                transport = "websocket"
            }
        });
        await _h.ExecuteRequestAsync(upsert);
        _ = await _h.Transport.ReadNextSentAsync();

        var remove = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelRemove, new
        {
            name = "weixin"
        });
        await _h.ExecuteRequestAsync(remove);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        Assert.Equal("weixin", removedName);
        Assert.True(removedFromConfigBeforeHook);
    }

    [Fact]
    public async Task ExternalChannelUpsert_AllowsExternalCatalogChannelName()
    {
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            channelListContributor: new FixedChannelListContributor(
                new ChannelDescriptor { Name = "feishu", Category = "external" }));

        await _h.InitializeAsync();
        var request = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "feishu",
                enabled = true,
                transport = "subprocess",
                builtinModule = "channel-feishu"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var channels = AppConfig.Load(Path.Combine(_workspaceCraftPath, "config.json")).ExternalChannels;
        var feishu = Assert.Single(channels);
        Assert.Equal("feishu", feishu.Name);
        Assert.Equal("channel-feishu", feishu.BuiltinModule);
    }

    [Fact]
    public async Task ExternalChannelUpsert_AcceptsManagedWebsocketWithBuiltinModule()
    {
        _h = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);

        await _h.InitializeAsync();
        var request = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "feishu",
                enabled = true,
                transport = "managedWebsocket",
                builtinModule = "channel-feishu"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var channel = response.RootElement.GetProperty("result").GetProperty("channel");
        Assert.Equal("managedWebsocket", channel.GetProperty("transport").GetString());
        Assert.Equal("channel-feishu", channel.GetProperty("builtinModule").GetString());

        var channels = AppConfig.Load(Path.Combine(_workspaceCraftPath, "config.json")).ExternalChannels;
        var feishu = Assert.Single(channels);
        Assert.Equal(ExternalChannelTransport.ManagedWebsocket, feishu.Transport);
        Assert.Equal("channel-feishu", feishu.BuiltinModule);
    }

    [Fact]
    public async Task ExternalChannelUpsert_RejectsManagedWebsocketWithoutLauncher()
    {
        _h = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);

        await _h.InitializeAsync();
        var request = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "feishu",
                enabled = true,
                transport = "managedWebsocket"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(
            response,
            AppServerErrors.ExternalChannelValidationFailedCode);
    }

    [Fact]
    public async Task ExternalChannelUpsert_RejectsNativeChannelName()
    {
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            channelListContributor: new FixedChannelListContributor(
                new ChannelDescriptor { Name = "cli", Category = "builtin" }));

        await _h.InitializeAsync();
        var request = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "cli",
                enabled = true,
                transport = "subprocess",
                builtinModule = "channel-cli"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(
            response,
            AppServerErrors.ExternalChannelNameConflictCode);
    }

    private sealed class FixedChannelListContributor(params ChannelDescriptor[] channels)
        : IAppServerChannelListContributor
    {
        public void AppendBaseChannels(List<ChannelDescriptor> target, HashSet<string> seen)
        {
            foreach (var channel in channels)
            {
                if (seen.Add(channel.Name))
                    target.Add(channel);
            }
        }
    }
}
