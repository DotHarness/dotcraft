using DotCraft.Commands.Core;
using DotCraft.Contributions;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for command/* methods that mutate thread state.
/// </summary>
public sealed class AppServerCommandExecutionTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerCommandExecutionTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task CommandList_DoesNotExposeClientOnlyClearCommand()
    {
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList, new { language = "zh" });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var commands = response.RootElement
            .GetProperty("result")
            .GetProperty("commands")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        Assert.Contains("/new", commands);
        Assert.Contains("/init", commands);
        Assert.DoesNotContain("/clear", commands);

        var newCommand = response.RootElement
            .GetProperty("result")
            .GetProperty("commands")
            .EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "/new");
        Assert.Equal("cmd.new", newCommand.GetProperty("descriptionKey").GetString());
        Assert.Equal("Create a new session", newCommand.GetProperty("fallbackDescription").GetString());
        Assert.Equal("Create a new session", newCommand.GetProperty("description").GetString());
    }

    [Fact]
    public async Task CommandExecute_Init_ReturnsPromptExpansion()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var msg = _h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandExecute, new
        {
            threadId = thread.Id,
            command = "/init"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("handled").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("expandedPrompt").GetString()));
    }

    [Fact]
    public async Task CommandList_IncludeBuiltinsFalse_ReturnsCustomCommandsOnly()
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

            var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList, new { includeBuiltins = false });
            await harness.ExecuteRequestAsync(msg);

            var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);

            var commands = response.RootElement
                .GetProperty("result")
                .GetProperty("commands")
                .EnumerateArray()
                .Select(e => new
                {
                    Name = e.GetProperty("name").GetString(),
                    Category = e.GetProperty("category").GetString()
                })
                .ToList();

            Assert.Contains(commands, c => c.Name == "/code-review" && c.Category == "custom");
            Assert.DoesNotContain(commands, c => c.Name == "/new");
            Assert.DoesNotContain(commands, c => c.Name == "/help");
            Assert.DoesNotContain(commands, c => c.Name == "/stop");
            Assert.DoesNotContain(commands, c => c.Name == "/debug");
            Assert.DoesNotContain(commands, c => c.Name == "/heartbeat");
            Assert.DoesNotContain(commands, c => c.Name == "/cron");
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
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task CommandList_HidesInitWhenWorkspaceAgentsFileExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"command_list_init_{Guid.NewGuid():N}");
        var workspaceCraftPath = Path.Combine(tempRoot, ".craft");
        try
        {
            Directory.CreateDirectory(workspaceCraftPath);
            await File.WriteAllTextAsync(Path.Combine(workspaceCraftPath, "AGENTS.md"), string.Empty);
            using var harness = new AppServerTestHarness(workspaceCraftPath: workspaceCraftPath);
            await harness.InitializeAsync();

            await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandList, new { }));
            var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var names = response.RootElement.GetProperty("result").GetProperty("commands")
                .EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();

            Assert.DoesNotContain("/init", names);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
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
    public async Task CommandExecute_New_ReturnsSessionResetPayloadAndFreshThread()
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
        Assert.True(result.GetProperty("sessionReset").GetBoolean());
        Assert.True(result.GetProperty("createdLazily").GetBoolean());

        var archived = result.GetProperty("archivedThreadIds");
        Assert.Contains(archived.EnumerateArray().Select(e => e.GetString()), id => id == existing.Id);

        var newThread = result.GetProperty("thread");
        var newThreadId = newThread.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newThreadId));
        Assert.NotEqual(existing.Id, newThreadId);
        Assert.Equal("active", newThread.GetProperty("status").GetString());

        var oldThread = await _h.Service.GetThreadAsync(existing.Id);
        Assert.Equal(ThreadStatus.Archived, oldThread.Status);
    }
}
