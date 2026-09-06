using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostInfrastructureTests
{
    [Fact]
    public void ServeLock_SecondAcquisitionFails_AndRecoversStaleFile()
    {
        using var home = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(home.Path, new MemoryCredentialStore());

        Assert.True(RemoteToolHostServeLock.TryAcquire(storage, out var held));
        Assert.False(RemoteToolHostServeLock.TryAcquire(storage, out var blocked));
        Assert.Null(blocked);
        held!.Dispose();
        Assert.False(File.Exists(storage.ServeLockPath));

        Directory.CreateDirectory(storage.RootPath);
        File.WriteAllText(storage.ServeLockPath, "4242");
        Assert.True(RemoteToolHostServeLock.TryAcquire(storage, out var recovered));
        recovered!.Dispose();
    }

    [Fact]
    public void CleanupLeaseArtifacts_RemovesOnlyThatLease()
    {
        using var hostState = new TemporaryDirectory();
        var artifactsRoot = Path.Combine(hostState.Path, "artifacts");
        var fullText = string.Join('\n', Enumerable.Range(0, 200).Select(index => $"line-{index:D3}"));
        var first = RemoteToolArtifactStore.Materialize(
            artifactsRoot,
            NewInvocation("lease_first", "first"),
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));
        var second = RemoteToolArtifactStore.Materialize(
            artifactsRoot,
            NewInvocation("lease_second", "second"),
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));

        Assert.True(RemoteToolArtifactStore.CleanupLeaseArtifacts(artifactsRoot, "lease_first"));

        Assert.False(File.Exists(first.Artifact!.Path));
        Assert.True(File.Exists(second.Artifact!.Path));
    }

    [Fact]
    public void ParseRemoteArtifact_RejectsControlCharacters()
    {
        var valid = JsonSerializer.SerializeToNode(
            new RemoteToolArtifactMeta(Path.Combine(Path.GetTempPath(), "artifact.txt"), 10),
            RemoteToolHostProtocol.JsonOptions);
        Assert.NotNull(RemoteToolHostClient.ParseRemoteArtifact(valid));

        var control = JsonSerializer.SerializeToNode(
            new RemoteToolArtifactMeta("artifacts/lease\u0007/result.txt", 10),
            RemoteToolHostProtocol.JsonOptions);
        Assert.Throws<JsonException>(() => RemoteToolHostClient.ParseRemoteArtifact(control));

        var overlong = JsonSerializer.SerializeToNode(
            new RemoteToolArtifactMeta(new string('a', 1025), 10),
            RemoteToolHostProtocol.JsonOptions);
        Assert.Throws<JsonException>(() => RemoteToolHostClient.ParseRemoteArtifact(overlong));
    }

    [Fact]
    public void WorkspaceLeaseManager_SharesWithinOwnerAndRejectsOtherOwner()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var leases = new WorkspaceLeaseManager(clock);

        var first = leases.Acquire("agent-a", "workspace", "workspace-root", "host", 1);
        var shared = leases.Acquire("agent-a", "workspace", "workspace-root", "host", 1);

        Assert.Equal(first.LeaseId, shared.LeaseId);
        var error = Assert.Throws<RemoteToolHostException>(() =>
            leases.Acquire("agent-b", "workspace", "workspace-root", "host", 1));
        Assert.Equal(RemoteToolErrorCodes.WorkspaceBusy, error.Code);

        clock.Advance(TimeSpan.FromSeconds(61));
        var reclaimed = leases.Acquire("agent-b", "workspace", "workspace-root", "host", 1);
        Assert.NotEqual(first.LeaseId, reclaimed.LeaseId);
    }

    [Fact]
    public void Storage_PeerCredential_IsNotWrittenToHostJson()
    {
        using var directory = new TemporaryDirectory();
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(directory.Path, credentials);
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string>(StringComparer.Ordinal));
        const string credential = "raw-secret-credential";

        var peer = storage.AddPeer(NewPeer("sat_alpha"), credential);

        var persisted = File.ReadAllText(storage.HostStatePath);
        Assert.DoesNotContain(credential, persisted, StringComparison.Ordinal);
        Assert.Contains(peer.CredentialReference, persisted, StringComparison.Ordinal);
        Assert.Equal(credential, storage.GetPeerCredential(peer));
    }

    [Fact]
    public void Storage_RemovePeer_DeletesCredential()
    {
        using var directory = new TemporaryDirectory();
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(directory.Path, credentials);
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string>(StringComparer.Ordinal));
        storage.AddPeer(NewPeer("sat_alpha"), "credential-alpha");
        storage.AddPeer(NewPeer("sat_beta"), "credential-beta");

        Assert.True(storage.RemovePeer("sat_alpha"));
        Assert.False(storage.RemovePeer("sat_alpha"));

        var remaining = Assert.Single(storage.LoadHostState()!.Peers);
        Assert.Equal("sat_beta", remaining.PeerId);
        Assert.DoesNotContain(
            credentials.Values,
            pair => pair.Key.EndsWith("sat_alpha", StringComparison.Ordinal));
        Assert.Equal("credential-beta", storage.GetPeerCredential(remaining));
    }

    [Fact]
    public async Task Empty_pairing_store_keeps_control_operations_live()
    {
        using var directory = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(directory.Path, new MemoryCredentialStore());
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());

        var catalog = await client.ListAsync("thread");
        Assert.Empty(catalog.Hosts);
        Assert.Null(catalog.ConnectedRoute);

        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ConnectAsync("thread", "missing-host", "workspace"));
        Assert.Equal(RemoteToolErrorCodes.HostNotRegistered, error.Code);
        Assert.False((await client.DisconnectAsync("thread")).Disconnected);

        RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = directory.Path });

        var updatedCatalog = await client.ListAsync("thread");
        var registered = Assert.Single(updatedCatalog.Hosts);
        Assert.Equal(server.PeerId, registered.HostId);
        Assert.True(registered.Online);
    }

    [Fact]
    public async Task WorkspaceExecutionSource_ExportsOnlyRpcEligibleTools()
    {
        using var directory = new TemporaryDirectory();
        var config = new AppConfig();
        await using var terminals = new BackgroundTerminalService(
            directory.Path,
            config.Tools.Shell.Background);
        var source = new WorkspaceExecutionToolSource(config, terminals);
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "host-thread",
            null,
            directory.Path,
            directory.Path,
            "remote-tool-host",
            null,
            [],
            1,
            workspaceRoots: [directory.Path]));

        Assert.NotEmpty(registrations);
        Assert.All(registrations, registration =>
            Assert.True(RemoteToolMetadata.IsRpcEligible(registration.Definition)));
    }

    [Fact]
    public void RemoteResultArtifacts_FollowLeaseLifetime()
    {
        using var hostState = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var artifactsRoot = Path.Combine(hostState.Path, "artifacts");
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var leases = new WorkspaceLeaseManager(
            clock,
            released => RemoteToolArtifactStore.CleanupLeaseArtifacts(artifactsRoot, released.LeaseId));
        var lease = leases.Acquire("agent", "workspace", workspace.Path, "host", 1);
        leases.Acquire("agent", "workspace", workspace.Path, "host", 1);
        var fullText = string.Join('\n', Enumerable.Range(0, 100).Select(index => $"line-{index:D3}-value"));
        var invocation = new RemoteInvocationMeta(
            lease.LeaseId,
            lease.WorkspaceId,
            "invocation",
            "definition",
            "contract",
            "thread",
            "turn",
            512,
            1);

        var materialized = RemoteToolArtifactStore.Materialize(
            artifactsRoot,
            invocation,
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));

        var artifact = Assert.IsType<RemoteToolArtifactMeta>(materialized.Artifact);
        var artifactPath = artifact.Path;
        Assert.Equal(fullText.Length, artifact.CharacterCount);
        Assert.Contains(artifact.Path, materialized.Result.Content, StringComparison.Ordinal);
        Assert.True(materialized.Result.Content?.Length <= 512);
        Assert.Equal(fullText, File.ReadAllText(artifactPath).TrimStart('\uFEFF'));
        Assert.False(leases.Release("agent", lease.LeaseId, lease.WorkspaceId));
        Assert.True(File.Exists(artifactPath));
        Assert.True(leases.Release("agent", lease.LeaseId, lease.WorkspaceId));
        Assert.False(File.Exists(artifactPath));

        var expiring = leases.Acquire("agent", "workspace", workspace.Path, "host", 1);
        var expiringResult = RemoteToolArtifactStore.Materialize(
            artifactsRoot,
            invocation with { LeaseId = expiring.LeaseId, InvocationId = "expiring" },
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));
        var expiringArtifact = Assert.IsType<RemoteToolArtifactMeta>(expiringResult.Artifact);
        var expiringPath = expiringArtifact.Path;
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(leases.GetStatus("workspace"));
        Assert.False(File.Exists(expiringPath));
    }

    [Fact]
    public async Task RemoteRoundTrip_EnforcesPolicyAndDisconnects()
    {
        using var hostDirectory = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "remote.txt"), "remote-value");
        var storage = new RemoteToolHostStorage(hostDirectory.Path, new MemoryCredentialStore());
        var state = RemoteToolHostTestHost.Setup(
            storage,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["repo"] = workspace.Path },
            hostId: "rth_e2e");

        await using var server = new RemoteToolHostTestServer(storage);
        var approvals = new ApproveService();
        await using var client = server.CreateClient(approvals);

        var config = new AppConfig();
        await using var terminals = new BackgroundTerminalService(
            Path.Combine(hostDirectory.Path, "agent-terminals"),
            config.Tools.Shell.Background);
        var source = new WorkspaceExecutionToolSource(config, terminals);
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "agent-thread", null, workspace.Path, hostDirectory.Path, "agent", null, [], 1,
            workspaceRoots: [workspace.Path]));
        client.UpdateRemoteToolDefinitions(registrations.Select(item => item.Definition).ToArray());

        var connected = await client.ConnectAsync("agent-thread", server.PeerId, "repo");
        Assert.Contains("ReadFile", connected.MatchedTools);
        Assert.True(client.TryGetConnectionSnapshot("agent-thread", out var connection));
        Assert.Equal(
            new RemoteToolConnectionSnapshot(
                RemoteToolConnectionStatus.Connected,
                server.PeerId,
                "repo",
                connected.Environment),
            connection);
        Assert.True(client.TryForkRoute("agent-thread", "child-thread"));
        Assert.True(client.TryGetConnectionSnapshot("child-thread", out var childConnection));
        Assert.Equal(connection, childConnection);
        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile");
        var invocationContext = new ToolInvocationContext(
            "agent-thread", "turn", "call", ToolInvocationAudience.Model,
            read.Definition.Name, read.Definition.Id, read.Binding.Id, 1, DateTimeOffset.UtcNow,
            WorkspacePath: workspace.Path);
        var result = await client.InvokeAsync(
            connected.Route,
            read.Definition,
            RemoteToolContractHasher.Compute(read.Definition),
            invocationContext,
            new JsonObject { ["path"] = "remote.txt" });

        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("remote-value", result.Content, StringComparison.Ordinal);

        var write = registrations.Single(item => item.Definition.Name.Name == "WriteFile");
        var writeContext = invocationContext with
        {
            CallId = "write-call",
            ToolName = write.Definition.Name,
            DefinitionId = write.Definition.Id,
            RuntimeBindingId = write.Binding.Id
        };
        var writeResult = await client.InvokeAsync(
            connected.Route,
            write.Definition,
            RemoteToolContractHasher.Compute(write.Definition),
            writeContext,
            new JsonObject { ["path"] = "approved.txt", ["content"] = "approved" });
        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.Equal(1, approvals.RequestCount);
        Assert.Equal("approved", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "approved.txt")));

        storage.SaveHostState(state with
        {
            ToolPolicies = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WriteFile"] = "deny"
            }
        });
        var denied = await client.InvokeAsync(
            connected.Route,
            write.Definition,
            RemoteToolContractHasher.Compute(write.Definition),
            writeContext with { CallId = "denied-call" },
            new JsonObject { ["path"] = "denied.txt", ["content"] = "denied" });
        Assert.False(denied.Success);
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, denied.Error?.Code);
        Assert.Equal(1, approvals.RequestCount);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "denied.txt")));

        Assert.True((await client.DisconnectAsync("agent-thread")).Disconnected);
        Assert.False(client.TryGetRoute("agent-thread", out _));
        Assert.False(client.TryGetConnectionSnapshot("agent-thread", out _));
        Assert.True(client.TryGetConnectionSnapshot("child-thread", out _));
        Assert.True((await client.DisconnectAsync("child-thread")).Disconnected);
        Assert.False(client.TryGetConnectionSnapshot("child-thread", out _));
    }


    private static RemoteToolHubPeer NewPeer(string peerId) => new()
    {
        PeerId = peerId,
        HubHost = "127.0.0.1",
        HubPort = 47600,
        CredentialReference = RemoteToolHostStorage.PeerCredentialReference(peerId),
        HubLabel = "test-hub",
        WorkspaceId = "repo",
        PairedAt = DateTimeOffset.UtcNow
    };

    private static RemoteInvocationMeta NewInvocation(string leaseId, string invocationId) => new(
        leaseId,
        "workspace",
        invocationId,
        "definition",
        "contract",
        "thread",
        "turn",
        512,
        1);
}
