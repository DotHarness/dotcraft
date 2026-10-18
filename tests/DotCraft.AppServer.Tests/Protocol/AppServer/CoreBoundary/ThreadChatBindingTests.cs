using DotCraft.AppServer;
using DotCraft.Channels;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ThreadChatBindingTests : IDisposable
{
    private readonly CoreAppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task ThreadStartAndResume_BindTheChatToTheAdapterConnection()
    {
        await InitializeAdapterAsync();

        var threadId = await StartThreadAsync(new { kind = "group", label = "Release team", membership = "sender" });
        Assert.True(_h.Connection.TryGetThreadChat(threadId, out var started));
        Assert.Equal(new ChannelChatDescriptor("group", "Release team", "sender"), started);

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadResume, new
        {
            threadId,
            chat = new { kind = "group", label = "Launch team", membership = "sender" }
        }));
        using (var resumed = await _h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(resumed);

        Assert.True(_h.Connection.TryGetThreadChat(threadId, out var replaced));
        Assert.Equal("Launch team", replaced!.Label);
    }

    [Fact]
    public async Task TurnStartFromAnAdapter_RunsInTheThreadsChannelScope()
    {
        await InitializeAdapterAsync();
        var threadId = await StartThreadAsync(new { kind = "group", label = "Release team", membership = "shared" });
        ChannelSessionInfo? observed = null;
        _h.Service.SubmitInputHandler = (id, _, _) =>
        {
            observed = ChannelSessionScope.Current;
            return CoreAppServerTestHarness.BuildTurnEventSequence(id);
        };

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId,
            input = new[] { new { type = "text", text = "remind us every morning" } },
            sender = new { senderId = "alice", senderName = "Alice", senderRole = "user", groupId = "-100" }
        }));
        await _h.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(10));

        Assert.NotNull(observed);
        Assert.Equal("telegram", observed!.Channel);
        Assert.Equal("alice", observed.UserId);
        Assert.Equal("-100", observed.DefaultDeliveryTarget);
        Assert.Equal("Release team", observed.Chat?.Label);
    }

    [Fact]
    public async Task ChatFromANonAdapterConnection_IsRejected()
    {
        await _h.InitializeAsync();

        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "u", workspacePath = _h.Identity.WorkspacePath },
            chat = new { kind = "direct", membership = "shared" }
        }));

        using var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    private async Task InitializeAdapterAsync()
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "telegram-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "telegram",
                    displayName = "Telegram",
                    deliveryCapabilities = new
                    {
                        text = new { format = "markdown" },
                        targets = new { group = true, direct = "knownOnly" }
                    }
                }
            }
        }));
        using (var initialized = await _h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsSuccessResponse(initialized);
        _h.Handler.HandleNotification(_h.BuildNotification(DotCraft.Protocol.AppServer.AppServerRpc.Initialized.Name, new { }));
    }

    private async Task<string> StartThreadAsync(object chat)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, new
        {
            identity = new { channelName = "telegram", userId = "u", channelContext = "-100", workspacePath = _h.Identity.WorkspacePath },
            chat
        }));
        using var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        using (var started = await _h.Transport.ReadNextSentAsync())
            CoreAppServerTestHarness.AssertIsNotification(started, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted);
        return response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
    }
}
