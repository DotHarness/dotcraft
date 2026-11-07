using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerCommandExecutionTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerCommandExecutionTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task CommandList_HidesClientOnlyClearAndInitExpandsIntoAPrompt()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList, new { language = "zh" });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var commands = response.RootElement.GetProperty("result").GetProperty("commands").EnumerateArray().ToList();
        var names = commands.Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("/new", names);
        Assert.Contains("/init", names);
        Assert.DoesNotContain("/clear", names);
        var newCommand = commands.First(e => e.GetProperty("name").GetString() == "/new");
        Assert.Equal("cmd.new", newCommand.GetProperty("descriptionKey").GetString());
        Assert.Equal(
            newCommand.GetProperty("fallbackDescription").GetString(),
            newCommand.GetProperty("description").GetString());

        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute, new
        {
            threadId = thread.Id,
            command = "/init"
        }));
        var init = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(init);
        var result = init.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("handled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("expandedPrompt").GetString()));
    }

    [Fact]
    public async Task CommandList_HidesInitWhenWorkspaceInstructionsExist()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"command_list_{Guid.NewGuid():N}");
        var workspaceCraftPath = Path.Combine(tempRoot, ".craft");

        try
        {
            Directory.CreateDirectory(workspaceCraftPath);

            using var harness = new AppServerTestHarness(workspaceCraftPath: workspaceCraftPath);
            await harness.InitializeAsync();

            foreach (var fileName in new[] { "AGENTS.override.md", "AGENTS.md" })
            {
                var path = Path.Combine(tempRoot, fileName);
                await File.WriteAllTextAsync(path, string.Empty);
                Assert.DoesNotContain(await ListCommandsAsync(harness, new { }), c => c.Name == "/init");
                File.Delete(path);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task<List<(string? Name, string? Category)>> ListCommandsAsync(AppServerTestHarness harness, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList, parameters));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").GetProperty("commands").EnumerateArray()
            .Select(e => (e.GetProperty("name").GetString(), e.GetProperty("category").GetString()))
            .ToList();
    }

    [Fact]
    public async Task CommandExecute_New_ReturnsTheNewCurrentThreadAndArchivesTheOldOne()
    {
        var existing = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute, new
        {
            threadId = existing.Id,
            command = "/new"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("handled").GetBoolean());
        var newThread = result.GetProperty("thread");
        var newThreadId = newThread.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newThreadId));
        Assert.NotEqual(existing.Id, newThreadId);
        Assert.True(_h.Connection.HasSubscription(newThreadId!));
        Assert.Equal("active", newThread.GetProperty("status").GetString());

        var oldThread = await _h.Service.GetThreadAsync(existing.Id);
        Assert.Equal(ThreadStatus.Archived, oldThread.Status);
    }

    [Fact]
    public async Task CommandExecute_DeniesAdminCommandsToUserSendersAndReturnsModeChoices()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.ExecuteRequestAsync(_h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute, new
        {
            threadId = thread.Id,
            command = "/model",
            sender = new { senderId = "user-1", senderName = "User", senderRole = "user" }
        }));
        AppServerTestHarness.AssertIsErrorResponse(await _h.Transport.ReadNextSentAsync(), -32061);

        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute, new
        {
            threadId = thread.Id,
            command = "/mode"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var commands = response.RootElement.GetProperty("result").GetProperty("choices")
            .EnumerateArray()
            .Select(static choice => choice.GetProperty("command").GetString())
            .ToArray();
        Assert.Contains("/mode plan", commands);
    }
}
