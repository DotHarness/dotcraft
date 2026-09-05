using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostCatalogTests
{
    [Fact]
    public async Task Connect_ReportsContractMismatchReason()
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
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile").Definition;
        client.UpdateRemoteToolDefinitions(
        [
            new ToolDefinition(
                read.Id,
                read.Name,
                read.Description + " (drifted)",
                read.InputSchema,
                read.OutputSchema,
                read.Annotations)
        ]);

        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");

        var reason = Assert.Single(connected.UnavailableReasons);
        Assert.Equal("ReadFile", reason.ToolName);
        Assert.Equal(RemoteToolErrorCodes.ToolContractMismatch, reason.Code);
        Assert.Contains(RemoteToolContractHasher.Compute(read), reason.Detail, StringComparison.Ordinal);
        Assert.Contains("agent build", reason.Detail, StringComparison.Ordinal);
        Assert.Contains("ReadFile", connected.UnavailableTools);
        Assert.Empty(connected.MatchedTools);
    }

    [Fact]
    public async Task Connect_ReportsMissingToolReason()
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
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile").Definition;
        client.UpdateRemoteToolDefinitions(
        [
            read,
            new ToolDefinition(
                new ToolDefinitionId(ToolSourceKind.CoreNative, "core-native", new SourceToolId("HostOnlyTool")),
                new ToolName(null, "HostOnlyTool"),
                "A tool the Host does not export.",
                read.InputSchema)
        ]);

        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");

        Assert.Contains("ReadFile", connected.MatchedTools);
        var reason = Assert.Single(connected.UnavailableReasons);
        Assert.Equal("HostOnlyTool", reason.ToolName);
        Assert.Equal(RemoteToolErrorCodes.RemoteToolUnavailable, reason.Code);
        Assert.Contains("host build", reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_ProtocolMismatchMessageIncludesBothBuildVersions()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(
            storage,
            peerId: "sat_paired",
            reportedPeerId: "sat_moved_elsewhere");
        await using var client = server.CreateClient(new ApproveService());

        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ConnectAsync("thread", "sat_paired", "repo"));

        Assert.Equal(RemoteToolErrorCodes.ProtocolMismatch, error.Code);
        Assert.Contains($"host build {RemoteToolHostProtocol.BuildVersion}", error.Message, StringComparison.Ordinal);
        Assert.Contains($"agent build {RemoteToolHostProtocol.BuildVersion}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_ReportsSelfBusyOwnerAndExpiry_ForOwnLease()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        await client.ConnectAsync("thread", server.PeerId, "repo");

        var catalog = await client.ListAsync("thread");

        var workspaceDescriptor = catalog.Hosts.Single().Workspaces.Single();
        Assert.False(workspaceDescriptor.Available);
        Assert.Equal("self", workspaceDescriptor.BusyOwner);
        Assert.NotNull(workspaceDescriptor.LeaseExpiresAt);
        Assert.True(workspaceDescriptor.LeaseExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task List_ReportsOtherBusyOwner_WithoutLeakingOwnerId()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        await using var owner = server.CreateClient(new ApproveService());
        await owner.ConnectAsync("thread", server.PeerId, "repo");
        await using var observer = server.CreateClient(new ApproveService());

        var catalog = await observer.ListAsync("observer-thread");

        var workspaceDescriptor = catalog.Hosts.Single().Workspaces.Single();
        Assert.Equal("other", workspaceDescriptor.BusyOwner);
        Assert.False(workspaceDescriptor.Available);
        Assert.DoesNotContain(
            "agent_",
            JsonSerializer.Serialize(catalog, JsonSerializerOptions.Web),
            StringComparison.Ordinal);

        var busy = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await observer.ConnectAsync("observer-thread", server.PeerId, "repo"));
        Assert.Equal(RemoteToolErrorCodes.WorkspaceBusy, busy.Code);
    }

    [Fact]
    public async Task RemoteReadFile_CanReadSpilledArtifactByAbsolutePath()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var oversized = string.Join(
            '\n',
            Enumerable.Range(0, 12_000).Select(index => $"line-{index:D6}-value"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "large.txt"), oversized);
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path });

        await using var server = new RemoteToolHostTestServer(storage);
        var approvals = new ApproveService();
        await using var client = server.CreateClient(approvals);
        var registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);
        var route = (await client.ConnectAsync("thread", server.PeerId, "repo")).Route;
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile");

        var spilled = await InvokeReadAsync(client, route, read, "spill", "large.txt", limit: 0);
        var artifactPath = ReadProvenanceArtifactPath(spilled);
        var reread = await InvokeReadAsync(client, route, read, "reread", artifactPath, limit: 3);

        Assert.StartsWith(storage.ArtifactsRootPath, artifactPath, StringComparison.Ordinal);
        Assert.True(File.Exists(artifactPath));
        Assert.DoesNotContain(workspace.Path, artifactPath, StringComparison.Ordinal);
        Assert.True(reread.Success, reread.Error?.Message);
        Assert.Contains("line-000000-value", reread.Content, StringComparison.Ordinal);
        Assert.Equal(0, approvals.RequestCount);
    }

    private static ValueTask<ToolExecutionResult> InvokeReadAsync(
        RemoteToolHostClient client,
        RemoteToolRoute route,
        ToolRegistration registration,
        string callId,
        string path,
        int limit) => client.InvokeAsync(
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
            limit > 0
                ? new JsonObject { ["path"] = path, ["limit"] = limit }
                : new JsonObject { ["path"] = path });

    private static string ReadProvenanceArtifactPath(ToolExecutionResult result)
    {
        Assert.True(result.Success, result.Error?.Message);
        var provenance = result.Meta ?? throw new InvalidOperationException("Remote provenance is missing.");
        var path = provenance.GetProperty("remoteArtifactPath").GetString();
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path!;
    }
}
