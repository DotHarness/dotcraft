using DotCraft.Configuration;
using DotCraft.AppServer;
using Xunit;

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
        }
    }

    [Fact]
    public async Task UpsertAndRemove_InvokeTheirHooksAfterPersisting()
    {
        var upserts = 0;
        var persistedBeforeHook = false;
        var removedName = string.Empty;
        var removedFromConfigBeforeHook = false;
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            onExternalChannelUpserted: (entry, _) =>
            {
                upserts++;
                persistedBeforeHook = LoadChannels().Any(c => string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            },
            onExternalChannelRemoved: (name, _) =>
            {
                removedName = name;
                removedFromConfigBeforeHook = LoadChannels().All(c => !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            });
        await _h.InitializeAsync();

        AppServerTestHarness.AssertIsSuccessResponse(await UpsertAsync(new { name = "weixin", enabled = true, transport = "websocket" }));
        Assert.Equal(1, upserts);
        Assert.True(persistedBeforeHook);

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelRemove, new { name = "weixin" }));
        AppServerTestHarness.AssertIsSuccessResponse(await _h.Transport.ReadNextSentAsync());
        Assert.Equal("weixin", removedName);
        Assert.True(removedFromConfigBeforeHook);
    }

    [Fact]
    public async Task Upsert_AllowsExternalCatalogNamesButRejectsNativeChannelNames()
    {
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            channelListContributor: new FixedChannelListContributor(
                new ChannelDescriptor { Name = "feishu", Category = "external" },
                new ChannelDescriptor { Name = "cli", Category = "builtin" }));
        await _h.InitializeAsync();

        AppServerTestHarness.AssertIsSuccessResponse(await UpsertAsync(new
        {
            name = "feishu",
            enabled = true,
            transport = "subprocess",
            builtinModule = "channel-feishu"
        }));
        var feishu = Assert.Single(LoadChannels());
        Assert.Equal("feishu", feishu.Name);
        Assert.Equal("channel-feishu", feishu.BuiltinModule);

        AppServerTestHarness.AssertIsErrorResponse(
            await UpsertAsync(new { name = "cli", enabled = true, transport = "subprocess", builtinModule = "channel-cli" }),
            AppServerErrors.ExternalChannelNameConflictCode);
    }

    [Fact]
    public async Task Upsert_RequiresALauncherForManagedWebsocketChannels()
    {
        _h = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await _h.InitializeAsync();

        AppServerTestHarness.AssertIsErrorResponse(
            await UpsertAsync(new { name = "feishu", enabled = true, transport = "managedWebsocket" }),
            AppServerErrors.ExternalChannelValidationFailedCode);

        using var response = await UpsertAsync(new
        {
            name = "feishu",
            enabled = true,
            transport = "managedWebsocket",
            builtinModule = "channel-feishu"
        });
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var channel = response.RootElement.GetProperty("result").GetProperty("channel");
        Assert.Equal("managedWebsocket", channel.GetProperty("transport").GetString());
        Assert.Equal("channel-feishu", channel.GetProperty("builtinModule").GetString());
        var feishu = Assert.Single(LoadChannels());
        Assert.Equal(ExternalChannelTransport.ManagedWebsocket, feishu.Transport);
        Assert.Equal("channel-feishu", feishu.BuiltinModule);
    }

    private async Task<System.Text.Json.JsonDocument> UpsertAsync(object channel)
    {
        await _h!.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert, new { channel }));
        return await _h.Transport.ReadNextSentAsync();
    }

    private List<ExternalChannelEntry> LoadChannels() =>
        AppConfig.Load(Path.Combine(_workspaceCraftPath, "config.json")).ExternalChannels;

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
