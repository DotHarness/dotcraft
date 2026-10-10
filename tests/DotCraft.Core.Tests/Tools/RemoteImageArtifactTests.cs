using DotCraft.Configuration;
using DotCraft.RemoteTools;
using DotCraft.Security;
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
    public async Task Imagegen_PinsDestinationAndNeverFallsBackToLocal(string mode)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var agent = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace.Path });
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var provider = new RouteChangingImageProvider(async () =>
        {
            if (mode != "success") await client.DisconnectAsync("thread");
            if (mode == "reconnect") await client.ConnectAsync("thread", server.PeerId, "repo");
        });
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Providers["openai"].SupportsImageGeneration = true;
        var source = new ImageGenerationToolSource(
            config, new ChatClientRegistry(provider), new AutoApproveApprovalService(), remoteToolHostClient: client);
        var registration = Assert.Single(await source.GetRegistrationsAsync(
            new ToolPlanningContext("thread", "turn", workspace.Path, agent.Path, "agent", null, null, 1)));
        var arguments = new JsonObject { ["prompt"] = "pin" };

        var result = await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext("thread", "turn", "ig_pinned", ToolInvocationAudience.Model,
                registration.Definition.Name, registration.Definition.Id, registration.Binding.Id, 1, DateTimeOffset.UtcNow),
            arguments);
        var image = ImageGenerationProjection.Completed(ImageGenerationProjection.Started("ig_pinned", arguments), result);

        Assert.Equal("completed", image.Status);
        Assert.Equal(Convert.ToBase64String(Png), image.Result);
        Assert.Equal(mode == "success" ? "saved" : "failed", image.SaveStatus);
        Assert.Equal(server.PeerId, image.SavedHostId);
        Assert.Equal("repo", image.SavedWorkspaceId);
        Assert.False(Directory.Exists(Path.Combine(agent.Path, "generated_images")));
        if (mode == "success") Assert.Equal(Png, await File.ReadAllBytesAsync(image.SavedPath!));
        else Assert.Null(image.SavedPath);
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("deny")]
    public async Task Imagegen_ReadsReferencedImagesThroughRemoteRoute(string policy)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var agent = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        var state = RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace.Path });
        if (policy == "deny")
        {
            state.ToolPolicies["ReadFile"] = "deny";
            storage.SaveHostState(state);
        }
        var reference = ImageFixture.Read("color-3.png");
        await File.WriteAllBytesAsync(Path.Combine(workspace.Path, "reference.png"), reference);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var provider = new RouteChangingImageProvider(() => Task.CompletedTask);
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Providers["openai"].SupportsImageGeneration = true;
        var source = new ImageGenerationToolSource(
            config, new ChatClientRegistry(provider), new AutoApproveApprovalService(), remoteToolHostClient: client);
        var registration = Assert.Single(await source.GetRegistrationsAsync(
            new ToolPlanningContext("thread", "turn", agent.Path, agent.Path, "agent", null, null, 1)));

        var result = await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext("thread", "turn", "ig_edit", ToolInvocationAudience.Model,
                registration.Definition.Name, registration.Definition.Id, registration.Binding.Id, 1, DateTimeOffset.UtcNow),
            new JsonObject { ["prompt"] = "edit", ["referenced_image_paths"] = new JsonArray("reference.png") });

        if (policy == "allow")
        {
            Assert.True(result.Success, result.Content);
            Assert.Equal(
                "data:image/png;base64," + Convert.ToBase64String(reference),
                Assert.Single(Assert.Single(provider.Requests).ReferenceImageUrls));
        }
        else
        {
            Assert.False(result.Success);
            Assert.Contains("Host policy denied", result.Content, StringComparison.Ordinal);
            Assert.Empty(provider.Requests);
        }
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

    private sealed class RouteChangingImageProvider(Func<Task> onGenerate) : IModelProvider, IProviderImageGeneration
    {
        public IReadOnlyCollection<string> Protocols { get; } =
            [ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.OpenAIResponses];

        public List<ProviderImageRequest> Requests { get; } = [];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

        public async Task<ProviderImageResult> GenerateImageAsync(
            EffectiveModelRuntime runtime,
            ProviderImageRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await onGenerate();
            return new ProviderImageResult(Png);
        }
    }
}
