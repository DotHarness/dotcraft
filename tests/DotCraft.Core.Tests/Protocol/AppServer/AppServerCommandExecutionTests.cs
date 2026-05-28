using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

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
        var msg = _h.BuildRequest(AppServerMethods.CommandList, new { });
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
        Assert.DoesNotContain("/clear", commands);
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

            var msg = harness.BuildRequest(AppServerMethods.CommandList, new { includeBuiltins = false });
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
    public async Task CommandExecute_New_ReturnsSessionResetPayloadAndFreshThread()
    {
        var existing = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.CommandExecute, new
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
