using DotCraft.AppBinding;
using DotCraft.Sessions;
using DotCraft.Text;
using Xunit;

namespace DotCraft.Tests.Commands;

public sealed class CommandConversationTests
{
    [Fact]
    public async Task SessionsAndResume_KeepPointingAtTheListedThreadAndKeepOneActiveThread()
    {
        await using var harness = CommandSessionHarness.Create();
        var first = await harness.CreateThreadAsync();
        var second = (await harness.ExecuteAsync(first.Id, "/new")).ThreadId!;
        var choice = Assert.Single((await harness.ExecuteAsync(second, "/sessions")).Choices!);

        var third = (await harness.ExecuteAsync(second, "/new")).ThreadId!;
        var byChoice = await harness.ExecuteAsync(third, choice.Command);
        var byNumber = await harness.ExecuteAsync(first.Id, "/resume 2");

        Assert.Equal(first.Id, byChoice.ThreadId);
        Assert.Equal(second, byNumber.ThreadId);
        var conversation = await harness.Service.FindThreadsAsync(harness.Identity, includeArchived: true);
        Assert.Equal(second, Assert.Single(conversation, static thread => thread.Status == ThreadStatus.Active).Id);
        Assert.Equal(2, conversation.Count(static thread => thread.Status == ThreadStatus.Archived));
    }

    [Fact]
    public async Task ConversationCommands_AreRefusedInAChatBoundToAThread()
    {
        await using var harness = CommandSessionHarness.Create();
        var bound = await harness.CreateThreadAsync();
        var request = harness.AppBindings.CreateChannelRequest(harness.CraftPath, bound.Id, "chat", "local");
        harness.AppBindings.AcceptChannel(harness.CraftPath, "chat", new ChannelBindingAcceptCommand
        {
            Code = request.Code,
            Target = new ChannelTarget
            {
                ChannelName = "chat",
                ConversationKind = "group",
                ConversationId = "1",
                DeliveryTarget = "group:1"
            }
        });

        foreach (var command in new[] { "/new", "/sessions", "/resume 1" })
        {
            var result = await harness.ExecuteAsync(bound.Id, command);

            Assert.Null(result.ThreadId);
            Assert.Null(result.Choices);
            Assert.Equal(FallbackText.CommandConversationBound, result.Message);
        }

        Assert.Equal(ThreadStatus.Active, (await harness.Service.GetThreadAsync(bound.Id)).Status);
        Assert.Single(await harness.Service.FindThreadsAsync(harness.Identity, includeArchived: true));
    }

    [Fact]
    public async Task ModelModeAndHelp_RespectAdminRightsAndUpdateTheThread()
    {
        await using var harness = CommandSessionHarness.Create("model-a", "model-b");
        var thread = await harness.CreateThreadAsync();

        await harness.ExecuteAsync(thread.Id, "/model model-b");
        Assert.NotEqual("model-b", (await harness.Service.GetThreadAsync(thread.Id)).Configuration?.Model);

        var listed = await harness.ExecuteAsync(thread.Id, "/model", isAdmin: true);
        Assert.Equal(["/model model-a", "/model model-b"], listed.Choices!.Select(static choice => choice.Command));

        await harness.ExecuteAsync(thread.Id, "/model MODEL-B", isAdmin: true);
        Assert.Equal("model-b", (await harness.Service.GetThreadAsync(thread.Id)).Configuration?.Model);

        await harness.ExecuteAsync(thread.Id, "/model model-z", isAdmin: true);
        Assert.Equal("model-b", (await harness.Service.GetThreadAsync(thread.Id)).Configuration?.Model);

        var shown = await harness.ExecuteAsync(thread.Id, "/mode");
        Assert.Contains(shown.Choices!, static choice => choice.Command == "/mode plan");
        await harness.ExecuteAsync(thread.Id, "/mode plan");
        Assert.Equal("plan", (await harness.Service.GetThreadAsync(thread.Id)).Configuration?.Mode);

        var user = await harness.ExecuteAsync(thread.Id, "/help");
        var admin = await harness.ExecuteAsync(thread.Id, "/help", isAdmin: true);

        Assert.Contains("/stop", user.Message);
        Assert.DoesNotContain("/model", user.Message);
        Assert.DoesNotContain("/debug", user.Message);
        Assert.Contains("/model", admin.Message);
    }
}
