using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.RemoteTools;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolHostInfrastructureTests
{
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
    public void Registration_PersistsOnlyCredentialReference()
    {
        using var directory = new TemporaryDirectory();
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(directory.Path, credentials);
        const string token = "raw-secret-token";

        storage.Register(new RemoteToolPairingBundle
        {
            HostId = "rth_test",
            DisplayName = "test-host",
            Endpoint = "https://127.0.0.1:7443",
            CertificateFingerprint = new string('A', 64),
            Token = token
        });

        var persisted = File.ReadAllText(storage.RegistrationsPath);
        Assert.DoesNotContain(token, persisted, StringComparison.Ordinal);
        var registration = Assert.Single(storage.LoadRegistrations());
        Assert.Equal(token, storage.GetToken(registration));
    }

    [Fact]
    public async Task Empty_registration_store_keeps_control_operations_live()
    {
        using var directory = new TemporaryDirectory();
        var storage = new RemoteToolHostStorage(directory.Path, new MemoryCredentialStore());
        await using var client = new RemoteToolHostClient(storage, new ApproveService());

        var catalog = await client.ListAsync("thread");
        Assert.Empty(catalog.Hosts);
        Assert.Null(catalog.ConnectedRoute);

        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ConnectAsync("thread", "missing-host", "workspace"));
        Assert.Equal(RemoteToolErrorCodes.HostNotRegistered, error.Code);
        Assert.False((await client.DisconnectAsync("thread")).Disconnected);

        storage.Register(new RemoteToolPairingBundle
        {
            HostId = "rth_added_later",
            DisplayName = "added-later",
            Endpoint = "https://127.0.0.1:1",
            CertificateFingerprint = new string('A', 64),
            Token = "test-token"
        });

        var updatedCatalog = await client.ListAsync("thread");
        var registered = Assert.Single(updatedCatalog.Hosts);
        Assert.Equal("rth_added_later", registered.HostId);
        Assert.False(registered.Online);
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
        using var workspace = new TemporaryDirectory();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var leases = new WorkspaceLeaseManager(
            clock,
            released => RemoteToolArtifactStore.CleanupLeaseArtifacts(
                released.WorkspacePath,
                released.LeaseId));
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
            workspace.Path,
            invocation,
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));

        var artifact = Assert.IsType<RemoteToolArtifactMeta>(materialized.Artifact);
        var artifactPath = Path.Combine(
            workspace.Path,
            artifact.Path.Replace('/', Path.DirectorySeparatorChar));
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
            workspace.Path,
            invocation with { LeaseId = expiring.LeaseId, InvocationId = "expiring" },
            "ReadFile",
            ToolExecutionResult.Succeeded(fullText));
        var expiringArtifact = Assert.IsType<RemoteToolArtifactMeta>(expiringResult.Artifact);
        var expiringPath = Path.Combine(
            workspace.Path,
            expiringArtifact.Path.Replace('/', Path.DirectorySeparatorChar));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(leases.IsBusy("workspace"));
        Assert.False(File.Exists(expiringPath));
    }

    [Fact]
    public async Task RealHttpsMcpRoundTrip_EnforcesPolicyAndDisconnects()
    {
        using var hostDirectory = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "remote.txt"), "remote-value");
        var credentials = new MemoryCredentialStore();
        var storage = new RemoteToolHostStorage(hostDirectory.Path, credentials);
        var port = GetAvailablePort();
        var endpoint = $"https://127.0.0.1:{port}";
        using var certificate = RemoteToolCertificate.Create(endpoint, storage.CertificatePath);
        var token = TokenUtilities.GenerateToken();
        var state = new RemoteToolHostState
        {
            HostId = "rth_e2e",
            DisplayName = "e2e",
            ListenEndpoint = endpoint,
            CertificatePath = storage.CertificatePath,
            CertificateFingerprint = RemoteToolCertificate.Fingerprint(certificate),
            TokenHash = TokenUtilities.HashToken(token),
            Workspaces = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repo"] = workspace.Path
            }
        };
        storage.SaveHostState(state);
        storage.Register(new RemoteToolPairingBundle
        {
            HostId = state.HostId,
            DisplayName = state.DisplayName,
            Endpoint = endpoint,
            CertificateFingerprint = state.CertificateFingerprint,
            Token = token
        });

        using var serverCts = new CancellationTokenSource();
        var serverTask = RemoteToolHostServer.RunAsync(
            storage,
            new AppConfig(),
            cancellationToken: serverCts.Token);
        var approvals = new ApproveService();
        await using var client = new RemoteToolHostClient(storage, approvals);
        await WaitUntilOnlineAsync(client, serverTask);

        var config = new AppConfig();
        await using var terminals = new BackgroundTerminalService(
            Path.Combine(hostDirectory.Path, "agent-terminals"),
            config.Tools.Shell.Background);
        var source = new WorkspaceExecutionToolSource(config, terminals);
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "agent-thread", null, workspace.Path, hostDirectory.Path, "agent", null, [], 1,
            workspaceRoots: [workspace.Path]));
        client.UpdateRemoteToolDefinitions(registrations.Select(item => item.Definition).ToArray());

        var connected = await client.ConnectAsync("agent-thread", state.HostId, "repo");
        Assert.Contains("ReadFile", connected.MatchedTools);
        Assert.True(client.TryGetConnectionSnapshot("agent-thread", out var connection));
        Assert.Equal(
            new RemoteToolConnectionSnapshot(
                RemoteToolConnectionStatus.Connected,
                state.HostId,
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

        serverCts.Cancel();
        await serverTask;
    }

    private static async Task WaitUntilOnlineAsync(RemoteToolHostClient client, Task serverTask)
    {
        RemoteToolHostDescriptor? last = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (serverTask.IsFaulted)
                await serverTask;
            var catalog = await client.ListAsync("probe");
            last = catalog.Hosts.Single();
            if (last.Online)
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Remote Tool Host did not become ready: {last?.ErrorCode}");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class MemoryCredentialStore : IRemoteToolCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public void Write(string reference, string secret) => _values[reference] = secret;
        public string? Read(string reference) => _values.GetValueOrDefault(reference);
        public void Delete(string reference) => _values.Remove(reference);
    }

    private sealed class ApproveService : IApprovalService
    {
        public int RequestCount { get; private set; }
        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) => Task.FromResult(true);
        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) => Task.FromResult(true);
        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            RequestCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotcraft-rth-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
