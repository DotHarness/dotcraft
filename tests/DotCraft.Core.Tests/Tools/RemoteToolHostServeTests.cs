using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostServeTests
{
    [Fact]
    public async Task Serve_UsesGlobalAndWorkspaceConfig_EnablesLsp()
    {
        using var home = new TemporaryDirectory();
        using var shared = new TemporaryDirectory();
        using var optedOut = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.WriteConfig(
            storage.GlobalConfigPath,
            new { Tools = new { Lsp = new { Enabled = true } } });
        RemoteToolHostTestHost.WriteConfig(
            Path.Combine(optedOut.Path, ".craft", "config.json"),
            new { Tools = new { Lsp = new { Enabled = false } } });
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["shared"] = shared.Path,
                ["opted-out"] = optedOut.Path
            });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(
            shared.Path,
            home.Path,
            enableLsp: true);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);

        var withLsp = await client.ConnectAsync("thread-shared", server.PeerId, "shared");
        var withoutLsp = await client.ConnectAsync("thread-opted-out", server.PeerId, "opted-out");

        Assert.Contains("LSP", withLsp.MatchedTools);
        Assert.DoesNotContain("LSP", withoutLsp.MatchedTools);
        Assert.Contains("LSP", withoutLsp.UnavailableTools);
    }

    [Fact]
    public async Task Serve_WithoutAnyConfigFile_UsesDefaults()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);

        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");

        Assert.Contains("ReadFile", connected.MatchedTools);
        Assert.Contains("Exec", connected.MatchedTools);
        Assert.Empty(connected.UnavailableTools);
        Assert.False(File.Exists(storage.GlobalConfigPath));
    }

    [Fact]
    public async Task RemoteReadFile_RespectsBlacklist()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var secretDirectory = Path.Combine(workspace.Path, "secret");
        Directory.CreateDirectory(secretDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretDirectory, "keys.txt"), "top-secret");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "open.txt"), "open-value");
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.WriteConfig(
            Path.Combine(workspace.Path, ".craft", "config.json"),
            new { Security = new { BlacklistedPaths = new[] { secretDirectory } } });
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile");

        var blocked = await InvokeAsync(client, connected.Route, read, "blocked", new JsonObject
        {
            ["path"] = Path.Combine("secret", "keys.txt")
        });
        var allowed = await InvokeAsync(client, connected.Route, read, "allowed", new JsonObject
        {
            ["path"] = "open.txt"
        });

        Assert.DoesNotContain("top-secret", blocked.Content, StringComparison.Ordinal);
        Assert.Contains("blacklist", blocked.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("open-value", allowed.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serve_FailsFast_WhenArtifactRootBlacklisted()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.WriteConfig(
            storage.GlobalConfigPath,
            new { Security = new { BlacklistedPaths = new[] { storage.RootPath } } });
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        var error = Assert.Throws<InvalidOperationException>(
            () => new RemoteToolHostOutboundHost(storage).Prepare());

        Assert.Contains(storage.ArtifactsRootPath, error.Message, StringComparison.Ordinal);
        Assert.Contains("blacklisted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public Task WriteStdin_DeniedForSessionNotCreatedByThisLease() =>
        AssertUnboundWriteStdinIsDeniedAsync(policyIsAllow: false);

    [Fact]
    public Task WriteStdin_DeniedEvenWhenPolicyIsAllow() =>
        AssertUnboundWriteStdinIsDeniedAsync(policyIsAllow: true);

    private static async Task AssertUnboundWriteStdinIsDeniedAsync(bool policyIsAllow)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        var state = RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });
        if (policyIsAllow)
        {
            storage.SaveHostState(state with
            {
                ToolPolicies = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["WriteStdin"] = "allow"
                }
            });
        }

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var writeStdin = registrations.Single(item => item.Definition.Name.Name == "WriteStdin");

        var result = await InvokeAsync(client, connected.Route, writeStdin, "stdin", new JsonObject
        {
            ["sessionId"] = "bgt_not_mine",
            ["input"] = "whoami\n"
        });

        Assert.False(result.Success);
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, result.Error?.Code);
    }

    [Fact]
    public async Task WriteStdin_AllowedForOwnApprovedExecSession()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var exec = registrations.Single(item => item.Definition.Name.Name == "Exec");
        var writeStdin = registrations.Single(item => item.Definition.Name.Name == "WriteStdin");

        var started = await InvokeAsync(client, connected.Route, exec, "exec", new JsonObject
        {
            ["command"] = "echo bound",
            ["runInBackground"] = true,
            ["interactive"] = true
        });
        Assert.True(started.Success, started.Error?.Message);
        var sessionId = ReadSessionId(started.Content);

        var result = await InvokeAsync(client, connected.Route, writeStdin, "stdin", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["input"] = string.Empty
        });

        Assert.NotEqual(RemoteToolErrorCodes.RemotePolicyDenied, result.Error?.Code);
        Assert.True(result.Success, result.Error?.Message);
    }

    [Fact]
    public async Task ListTools_UsesLeasedWorkspace_NotFirstRegistered()
    {
        using var home = new TemporaryDirectory();
        using var plain = new TemporaryDirectory();
        using var withLsp = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.WriteConfig(
            Path.Combine(withLsp.Path, ".craft", "config.json"),
            new { Tools = new { Lsp = new { Enabled = true } } });
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a-plain"] = plain.Path,
                ["b-lsp"] = withLsp.Path
            });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(
            withLsp.Path,
            home.Path,
            enableLsp: true);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);

        var connected = await client.ConnectAsync("thread", server.PeerId, "b-lsp");

        Assert.Contains("LSP", connected.MatchedTools);
    }

    [Fact]
    public async Task ListTools_WithoutLeaseMeta_FailsClosed()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var raw = await server.ConnectRawAsync();

        await Assert.ThrowsAnyAsync<McpException>(() =>
            raw.ListToolsAsync(new ListToolsRequestParams()).AsTask());
    }

    private static ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolHostClient client,
        RemoteToolRoute route,
        ToolRegistration registration,
        string callId,
        JsonObject arguments) => client.InvokeAsync(
            route,
            registration.Definition,
            RemoteToolContractHasher.Compute(registration.Definition),
            new ToolInvocationContext(
                "thread",
                "turn",
                callId,
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                1,
                DateTimeOffset.UtcNow),
            arguments);

    private static string ReadSessionId(string? content)
    {
        var line = (content ?? string.Empty)
            .Split('\n')
            .FirstOrDefault(item => item.StartsWith("Session ID:", StringComparison.Ordinal));
        Assert.NotNull(line);
        return line!["Session ID:".Length..].Trim();
    }
}
