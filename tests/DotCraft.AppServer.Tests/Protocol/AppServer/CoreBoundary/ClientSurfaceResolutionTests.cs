using DotCraft.Channels;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ClientSurfaceResolutionTests : IDisposable
{
    private readonly CoreAppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("dotcraft-desktop", false, "dotcraft-desktop", ClientSurfaceKind.DesktopApp, false)]
    [InlineData("dotcraft-desktop", true, "dotcraft-desktop", ClientSurfaceKind.DesktopApp, true)]
    [InlineData("dotcraft-mobile", false, "dotcraft-mobile", ClientSurfaceKind.MobileApp, true)]
    [InlineData("dotcraft-cli", false, "cli", ClientSurfaceKind.Terminal, false)]
    [InlineData("oratorio", false, "oratorio", ClientSurfaceKind.Service, false)]
    [InlineData("third-party-tool", false, "api", ClientSurfaceKind.Api, false)]
    public async Task TurnStart_RunsUnderTheSurfaceResolvedFromInitialize(
        string clientName,
        bool remoteHost,
        string channel,
        ClientSurfaceKind kind,
        bool expectedRemoteHost)
    {
        await InitializeAsync(clientName, remoteHost);
        var threadId = await StartThreadAsync();
        ChannelSessionInfo? observed = null;
        _h.Service.SubmitInputHandler = (id, _, _) =>
        {
            observed = ChannelSessionScope.Current;
            return CoreAppServerTestHarness.BuildTurnEventSequence(id);
        };

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "hello" } }
        }));
        await _h.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(10));

        Assert.NotNull(observed);
        Assert.Equal(channel, observed!.Channel);
        Assert.Equal("local", observed.UserId);
        Assert.Equal(new ClientSurface(channel, kind, expectedRemoteHost), observed.Surface);
    }

    private async Task InitializeAsync(string clientName, bool remoteHost)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = clientName, version = "1.0.0" },
            capabilities = new { remoteHost }
        }));
        using (var initialized = await _h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(initialized);
        _h.Handler.HandleNotification(_h.BuildNotification(DotCraft.Protocol.AppServer.AppServerRpc.Initialized.Name, new { }));
    }

    private async Task<string> StartThreadAsync()
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = _h.Identity.WorkspacePath }
        }));
        using var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        using (var started = await _h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsNotification(started, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        return response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
    }
}
