using DotCraft.RemoteTools;
using DotCraft.Tools;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using System.Text.Json.Nodes;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteImageArtifactTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1cAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("success")]
    [InlineData("disconnect")]
    [InlineData("reconnect")]
    public async Task Lifecycle_PinsDestinationAndNeverFallsBackToLocal(string mode)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var agent = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace.Path });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var turn = new SessionTurn { Id = "turn", ThreadId = "thread" };
        var events = new SessionEventChannel("thread", "turn");
        var seq = 0;
        var lifecycle = new ImageGenerationLifecycle(agent.Path, null, client, "thread", turn, events, () => ++seq);
        lifecycle.Start(new ImageGenerationToolCallContent("ig_pinned"));
        if (mode != "success") await client.DisconnectAsync("thread");
        if (mode == "reconnect") await client.ConnectAsync("thread", server.PeerId, "repo");
        await lifecycle.CompleteAsync(new HostedImageGenerationContent { Id = "ig_pinned", ImageBytes = Png }, default);
        var image = Assert.Single(turn.Items).AsImageGeneration!;
        Assert.Equal("completed", image.Status);
        Assert.Equal(Convert.ToBase64String(Png), image.Result);
        Assert.Equal(mode == "success" ? "saved" : "failed", image.SaveStatus);
        Assert.Equal(server.PeerId, image.SavedHostId);
        Assert.Equal("repo", image.SavedWorkspaceId);
        Assert.Equal(ItemStatus.Completed, Assert.Single(turn.Items).Status);
        Assert.False(Directory.Exists(Path.Combine(agent.Path, "generated_images")));
        if (mode == "success") Assert.Equal(Png, await File.ReadAllBytesAsync(image.SavedPath!));
        else Assert.Null(image.SavedPath);
    }

    [Fact]
    public async Task Write_UsesRemoteWorkspaceAndSurvivesDisconnect()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace.Path });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        var route = (await client.ConnectAsync("thread", server.PeerId, "repo")).Route;
        var path = await client.WriteImageAsync(route, "thread", "ig_test", Png);
        Assert.Equal(Path.Combine(workspace.Path, ".craft", "generated_images", "thread", "ig_test.png"), path);
        Assert.Equal(Png, await File.ReadAllBytesAsync(path));
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile");
        var readResult = await client.InvokeAsync(route, read.Definition, RemoteToolContractHasher.Compute(read.Definition),
            new ToolInvocationContext("thread", "turn", "read_image", ToolInvocationAudience.Model,
                read.Definition.Name, read.Definition.Id, read.Binding.Id, 1, DateTimeOffset.UtcNow),
            new JsonObject { ["path"] = path });
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Contains(readResult.ContentItems!, item => item is DataContent image && image.MediaType == "image/png");
        Assert.False(Directory.Exists(Path.Combine(home.Path, "generated_images")));
        await Assert.ThrowsAsync<RemoteToolHostException>(async () => await client.WriteImageAsync(route, "thread", "ig_test", Png));
        Assert.Equal(Png, await File.ReadAllBytesAsync(path));
        await client.DisconnectAsync("thread");
        Assert.True(File.Exists(path));
        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () => await client.WriteImageAsync(route, "thread", "ig_late", Png));
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, error.Code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ig_late.png")));
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("size")]
    [InlineData("path")]
    public async Task Write_EnforcesHostPolicy(string mode)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        var state = RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace.Path });
        if (mode == "deny")
        {
            state.ToolPolicies["WriteFile"] = "deny";
            storage.SaveHostState(state);
        }
        if (mode == "size")
            RemoteToolHostTestHost.WriteConfig(Path.Combine(workspace.Path, ".craft", "config.json"), new { Tools = new { File = new { MaxFileSize = 8 } } });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        var route = (await client.ConnectAsync("thread", server.PeerId, "repo")).Route;
        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.WriteImageAsync(route, "thread", mode == "path" ? "../escape" : "ig_denied", Png));
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, error.Code);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".craft", "generated_images")));
    }
}
