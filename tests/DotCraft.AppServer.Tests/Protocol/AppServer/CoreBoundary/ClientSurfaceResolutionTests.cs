using DotCraft.Channels;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ClientSurfaceResolutionTests
{
    [Fact]
    public async Task TurnStart_RunsUnderTheSurfaceResolvedFromInitialize()
    {
        foreach (var (clientName, remoteHost, channel, kind, expectedRemoteHost) in new (string, bool, string, ClientSurfaceKind, bool)[]
                 {
                     ("dotcraft-desktop", false, "dotcraft-desktop", ClientSurfaceKind.DesktopApp, false),
                     ("dotcraft-desktop", true, "dotcraft-desktop", ClientSurfaceKind.DesktopApp, true),
                     ("dotcraft-mobile", false, "dotcraft-mobile", ClientSurfaceKind.MobileApp, true),
                     ("dotcraft-cli", false, "cli", ClientSurfaceKind.Terminal, false),
                     ("oratorio", false, "oratorio", ClientSurfaceKind.Service, false),
                     ("third-party-tool", false, "api", ClientSurfaceKind.Api, false)
                 })
        {
            using var h = new CoreAppServerTestHarness();
            await InitializeAsync(h, clientName, remoteHost);
            var threadId = await StartThreadAsync(h);
            ChannelSessionInfo? observed = null;
            h.Service.SubmitInputHandler = (id, _, _) =>
            {
                observed = ChannelSessionScope.Current;
                return CoreAppServerTestHarness.BuildTurnEventSequence(id);
            };

            await h.ExecuteRequestAsync(h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = "hello" } }
            }));
            await h.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(10));

            Assert.NotNull(observed);
            Assert.Equal(channel, observed!.Channel);
            Assert.Equal("local", observed.UserId);
            Assert.Equal(new ClientSurface(channel, kind, expectedRemoteHost), observed.Surface);
        }
    }

    private static async Task InitializeAsync(CoreAppServerTestHarness h, string clientName, bool remoteHost)
    {
        await h.ExecuteRequestAsync(h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = clientName, version = "1.0.0" },
            capabilities = new { remoteHost }
        }));
        using (var initialized = await h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(initialized);
        h.Handler.HandleNotification(h.BuildNotification(DotCraft.Protocol.AppServer.AppServerRpc.Initialized.Name, new { }));
    }

    private static async Task<string> StartThreadAsync(CoreAppServerTestHarness h)
    {
        await h.ExecuteRequestAsync(h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "dotcraft-desktop", userId = "local", workspacePath = h.Identity.WorkspacePath }
        }));
        using var response = await h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        using (var started = await h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsNotification(started, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        return response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
    }
}
