using DotCraft.Commands.Core;
using DotCraft.Contributions;
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
    public async Task CommandList_ListsCustomCommandsAndHidesInitWhenWorkspaceInstructionsExist()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"command_list_custom_{Guid.NewGuid():N}");
        var workspaceCraftPath = Path.Combine(tempRoot, ".craft");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceCraftPath, "commands"));
            await File.WriteAllTextAsync(
                Path.Combine(workspaceCraftPath, "commands", "code-review.md"),
                """
                ---
                description: Review changed files
                ---
                Review these files: $ARGUMENTS
                """);

            using var harness = new AppServerTestHarness(workspaceCraftPath: workspaceCraftPath);
            await harness.InitializeAsync();

            var custom = await ListCommandsAsync(harness, new { includeBuiltins = false });
            Assert.Contains(custom, c => c.Name == "/code-review" && c.Category == "custom");
            Assert.DoesNotContain(custom, c => c.Name is "/new" or "/help" or "/stop" or "/debug");

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
    public async Task ContributedCommand_ReachesTheClientThroughTheExistingCommandMethods()
    {
        var registry = new ContributionRegistry();
        using var harness = new AppServerTestHarness(contributions: registry);
        await harness.InitializeAsync();
        var handle = registry.Add<ICodeCommand>(new TriageCommand());
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList,
            new { includeBuiltins = false }));
        var listed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listed);
        var entry = listed.RootElement.GetProperty("result").GetProperty("commands")
            .EnumerateArray().Single(item => item.GetProperty("name").GetString() == "/triage");
        Assert.Equal("custom", entry.GetProperty("category").GetString());
        Assert.Equal("Triage the inbox", entry.GetProperty("description").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute,
            new { threadId = thread.Id, command = "/triage", arguments = new[] { "now" } }));
        var executed = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(executed);
        Assert.Equal("TRIAGE:now", executed.RootElement.GetProperty("result").GetProperty("expandedPrompt").GetString());

        handle.Dispose();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList,
            new { includeBuiltins = false }));
        var afterRevoke = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(afterRevoke);
        Assert.DoesNotContain(
            afterRevoke.RootElement.GetProperty("result").GetProperty("commands").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "/triage");
    }

    private sealed class TriageCommand : ICodeCommand
    {
        public string Name => "triage";

        public string Description => "Triage the inbox";

        public string? Expand(CommandInvocation invocation) => $"TRIAGE:{invocation.Arguments}";
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
