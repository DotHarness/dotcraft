using DotCraft.Configuration;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Tests.Hub;

/// <summary>
/// An in-process Hub, a paired Remote Tool Host running in outbound mode, and the Agent-side
/// directory that reaches it through the Hub bridge.
/// </summary>
internal sealed class SatelliteScenario : IAsyncDisposable
{
    private readonly RemoteToolHostStorage _storage;
    private readonly HubRemoteToolHostDirectory _directory;

    private SatelliteScenario(
        SatelliteHubFixture hub,
        RemoteToolHostRuntime runtime,
        RemoteToolHostStorage storage,
        HubRemoteToolHostDirectory directory,
        Task running,
        string peerId,
        string workspaceId,
        string workspacePath)
    {
        Hub = hub;
        Runtime = runtime;
        _storage = storage;
        _directory = directory;
        Running = running;
        PeerId = peerId;
        WorkspaceId = workspaceId;
        WorkspacePath = workspacePath;
    }

    public SatelliteHubFixture Hub { get; }
    public RemoteToolHostRuntime Runtime { get; }
    public IRemoteToolHostDirectory Directory => _directory;
    public Task Running { get; }
    public string PeerId { get; }
    public string WorkspaceId { get; }
    public string WorkspacePath { get; }
    public RemoteToolHostStorage Storage => _storage;
    public string ArtifactsRootPath => _storage.ArtifactsRootPath;
    public string HostCraftHome => _storage.CraftHomePath;

    public static async Task<SatelliteScenario> StartAsync(
        string userProfile,
        int satellitePort = 0,
        TimeSpan? heartbeatInterval = null,
        MemoryCredentialStore? credentials = null)
    {
        var workspacePath = Path.Combine(userProfile, "workspace");
        System.IO.Directory.CreateDirectory(workspacePath);
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "remote.txt"), "remote-value");
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "large.txt"),
            string.Join('\n', Enumerable.Range(0, 12_000).Select(index => $"line-{index:D6}-value")));

        var hub = await SatelliteHubFixture.StartAsync(userProfile, satellitePort);
        var storage = new RemoteToolHostStorage(
            Path.Combine(userProfile, "host-craft"),
            credentials ?? new MemoryCredentialStore());
        var runtime = new RemoteToolHostRuntime(storage, "host-machine", heartbeatInterval);
        var invite = await hub.CreateInviteAsync("Ann");
        var peer = await runtime.AcceptInviteAsync(
            new RemoteToolJoinDecision(RemoteToolHostRuntime.ParseInvite(invite.Url), workspacePath));

        var running = runtime.RunAsync();
        var directory = new HubRemoteToolHostDirectory(new FixedHubEndpointProvider(hub.ApiBaseUrl, hub.Token));
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(async () =>
            (await hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Any(item => item.Online));

        return new SatelliteScenario(
            hub,
            runtime,
            storage,
            directory,
            running,
            peer.PeerId,
            peer.WorkspaceId,
            workspacePath);
    }

    public void DenyTool(string toolName)
    {
        var state = _storage.LoadHostState()!;
        _storage.SaveHostState(state with
        {
            ToolPolicies = new Dictionary<string, string>(state.ToolPolicies, StringComparer.Ordinal)
            {
                [toolName] = "deny"
            }
        });
    }

    public async Task<IReadOnlyList<ToolRegistration>> AgentRegistrationsAsync()
    {
        var config = new AppConfig();
        await using var terminals = new BackgroundTerminalService(
            Path.Combine(HostCraftHome, "agent-terminals"),
            config.Tools.Shell.Background);
        var source = new WorkspaceExecutionToolSource(config, terminals);
        return await source.GetRegistrationsAsync(new ToolPlanningContext(
            "agent-thread",
            null,
            WorkspacePath,
            HostCraftHome,
            "agent",
            null,
            [],
            1,
            workspaceRoots: [WorkspacePath]));
    }

    public async ValueTask DisposeAsync()
    {
        _directory.Dispose();
        await Runtime.DisposeAsync();
        await Hub.DisposeAsync();
    }

    private sealed class FixedHubEndpointProvider(string baseUrl, string token) : IHubEndpointProvider
    {
        public HubEndpoint? TryResolve() => new(new Uri(baseUrl), token);
    }
}

internal sealed class MemoryCredentialStore : IRemoteToolCredentialStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Values => _values;
    public void Write(string reference, string secret) => _values[reference] = secret;
    public string? Read(string reference) => _values.GetValueOrDefault(reference);
    public void Delete(string reference) => _values.Remove(reference);
}
