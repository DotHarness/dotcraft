using System.Text.Json.Nodes;
using Xunit;
using static DotCraft.Tests.CLI.ExecWire;

namespace DotCraft.Tests.CLI;

public sealed class ExecOutputProcessTests : ExecProcessTestBase
{
    private const string ImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Exec_FailedTurn_LeavesStdoutEmptyAndExitsOne()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                ItemEvent("item/completed", threadId, AgentMessage("partial answer")),
                Failed(threadId, "provider exploded", AgentMessage("partial answer"))
            ]
        });

        var result = await RunExecAsync(server, ["hello"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Contains("provider exploded", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exec_OutputLastMessage_WritesTheFinalMessageWithoutByteOrderMark()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId => [Completed(threadId, "final answer ✓")]
        });
        var output = Path.Combine(Root, "last.txt");
        await File.WriteAllTextAsync(output, "previous content that is longer");

        var result = await RunExecAsync(server, ["-o", output, "hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("final answer ✓", result.StandardOutput.Trim());
        Assert.Equal("final answer ✓"u8.ToArray(), await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task Exec_OutputLastMessageWithoutMessage_WritesEmptyFileAndWarns()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId => [Notification("turn/completed", new JsonObject { ["turn"] = Turn(threadId, "completed") })]
        });
        var output = Path.Combine(Root, "last.txt");

        var result = await RunExecAsync(server, ["--output-last-message", output, "hello"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Empty(await File.ReadAllBytesAsync(output));
        Assert.Contains(output, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exec_OutputLastMessageWhenTurnFails_LeavesTheFileUntouched()
    {
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId => [Failed(threadId, "provider exploded", AgentMessage("partial answer"))]
        });
        var output = Path.Combine(Root, "last.txt");
        await File.WriteAllTextAsync(output, "previous");

        var result = await RunExecAsync(server, ["-o", output, "hello"]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("previous", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Exec_GeneratedImage_ReportsSavedPathOnStderrWithoutImageBytes()
    {
        var savedPath = Path.Combine(Workspace, ".craft", "generated_images", "thread_1", "call_1.png");
        var image = GeneratedImage(savedPath, ImageBase64);
        var failedImage = Item("imageGeneration", new JsonObject
        {
            ["callId"] = "call_2",
            ["status"] = "failed",
            ["mediaType"] = "image/png",
            ["errorMessage"] = "content policy refused"
        }, id: "item_image_2");
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                ItemEvent("item/completed", threadId, image),
                ItemEvent("item/completed", threadId, failedImage),
                Notification("turn/completed", new JsonObject
                {
                    ["turn"] = Turn(threadId, "completed", image, failedImage, AgentMessage("here is the image"))
                })
            ]
        });

        var result = await RunExecAsync(server, ["draw a cat"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("here is the image", result.StandardOutput.Trim());
        Assert.Contains(savedPath, result.StandardError, StringComparison.Ordinal);
        Assert.Contains("content policy refused", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageBase64, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exec_TurnNotificationLargerThanFourMegabytes_Completes()
    {
        var largeImage = Convert.ToBase64String(new byte[6 * 1024 * 1024]);
        var image = GeneratedImage(Path.Combine(Workspace, "large.png"), largeImage);
        await using var server = await ExecFakeAppServer.StartAsync(new ExecFakeAppServerOptions
        {
            TurnNotifications = threadId =>
            [
                ItemEvent("item/completed", threadId, image),
                Notification("turn/completed", new JsonObject
                {
                    ["turn"] = Turn(threadId, "completed", image, AgentMessage("large image done"))
                })
            ]
        });

        var result = await RunExecAsync(server, ["draw a large cat"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("large image done", result.StandardOutput.Trim());
    }

    private static JsonObject GeneratedImage(string savedPath, string base64) => Item("imageGeneration", new JsonObject
    {
        ["callId"] = "call_1",
        ["status"] = "completed",
        ["revisedPrompt"] = "a cat",
        ["result"] = base64,
        ["mediaType"] = "image/png",
        ["savedPath"] = savedPath,
        ["saveStatus"] = "saved"
    }, id: "item_image_1");

    private static JsonObject Failed(string threadId, string error, params JsonObject[] items) =>
        Notification("turn/failed", new JsonObject
        {
            ["turn"] = Turn(threadId, "failed", items),
            ["error"] = error
        });
}
