using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

public sealed class RemoteToolHostCoreTests
{
    [Fact]
    public void Contract_hash_is_canonical_and_changes_with_semantics()
    {
        var first = Definition("description", """{"type":"object","properties":{"b":{"type":"string"},"a":{"type":"integer"}}}""");
        var reordered = Definition("description", """{"properties":{"a":{"type":"integer"},"b":{"type":"string"}},"type":"object"}""");
        var changed = Definition("changed", """{"type":"object","properties":{"b":{"type":"string"},"a":{"type":"integer"}}}""");

        Assert.Equal(RemoteToolContractHasher.Compute(first), RemoteToolContractHasher.Compute(reordered));
        Assert.NotEqual(RemoteToolContractHasher.Compute(first), RemoteToolContractHasher.Compute(changed));
    }

    [Fact]
    public async Task Routed_runtime_switches_immediately_and_never_calls_local_while_connected()
    {
        var definition = Definition("description", """{"type":"object"}""");
        var local = new RecordingRuntime("local");
        var client = new FakeRemoteClient();
        var runtime = new RemoteRoutableToolRuntime(definition, local, client);
        var context = Invocation(definition);

        var localResult = await runtime.InvokeAsync(context, new JsonObject());
        Assert.Equal("local", localResult.Content);
        Assert.Equal(1, local.Calls);

        client.SetRoute(context.ThreadId);
        var remoteResult = await runtime.InvokeAsync(context, new JsonObject());
        Assert.Equal("remote", remoteResult.Content);
        Assert.Equal(1, local.Calls);
        Assert.Equal(1, client.RemoteCalls);

        await client.DisconnectAsync(context.ThreadId);
        var localAgain = await runtime.InvokeAsync(context, new JsonObject());
        Assert.Equal("local", localAgain.Content);
        Assert.Equal(2, local.Calls);
    }

    [Fact]
    public void Registration_router_wraps_only_rpc_eligible_tools_and_preserves_identity()
    {
        var client = new FakeRemoteClient();
        var eligible = Registration(Definition("remote", """{"type":"object"}""", rpcEligible: true));
        var localOnly = Registration(Definition("local", """{"type":"object"}""", name: "Local", rpcEligible: false));
        var eligibleRuntime = eligible.Binding.Runtime;
        var localRuntime = localOnly.Binding.Runtime;

        var wrapped = RemoteToolRegistrationRouter.Wrap([eligible, localOnly], client);

        Assert.NotSame(eligibleRuntime, wrapped[0].Binding.Runtime);
        Assert.Same(localRuntime, wrapped[1].Binding.Runtime);
        Assert.Same(eligible.Definition, wrapped[0].Definition);
        Assert.Equal(eligible.Binding.Id, wrapped[0].Binding.Id);
    }

    [Fact]
    public async Task Control_source_exposes_canonical_tool_names()
    {
        var source = new RemoteToolHostControlSource(new FakeRemoteClient());
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread-1",
            "turn-1",
            Directory.GetCurrentDirectory(),
            Directory.GetCurrentDirectory(),
            "agent",
            null,
            [],
            1));

        Assert.Equal(
            new[] { "RemoteToolHost.Connect", "RemoteToolHost.Disconnect", "RemoteToolHost.List" },
            registrations.Select(item => item.Definition.Name.ToString()).OrderBy(name => name, StringComparer.Ordinal));
    }

    private static ToolDefinition Definition(
        string description,
        string schema,
        string name = "ReadFile",
        bool rpcEligible = true)
    {
        var annotations = rpcEligible
            ? new Dictionary<string, JsonElement>
            {
                [RemoteToolMetadata.RpcEligibleAnnotation] = JsonSerializer.SerializeToElement(true)
            }
            : null;
        return new ToolDefinition(
            new ToolDefinitionId(ToolSourceKind.CoreNative, "core-native", new SourceToolId(name)),
            new ToolName(null, name),
            description,
            JsonSerializer.Deserialize<JsonElement>(schema),
            annotations: annotations);
    }

    private static ToolRegistration Registration(ToolDefinition definition) =>
        new(
            definition,
            new ToolRuntimeBinding(
                new RuntimeBindingId($"binding:{definition.Name}"),
                definition.Id,
                new RecordingRuntime(definition.Name.Name),
                ToolBindingLeases.AlwaysAvailable,
                "test",
                1),
            ToolProjectionShape.StandardPair);

    private static ToolInvocationContext Invocation(ToolDefinition definition) =>
        new(
            "thread-1",
            "turn-1",
            "call-1",
            ToolInvocationAudience.Model,
            definition.Name,
            definition.Id,
            new RuntimeBindingId("binding"),
            1,
            DateTimeOffset.UtcNow);

    private sealed class RecordingRuntime(string result) : IToolRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(ToolExecutionResult.Succeeded(result));
        }
    }

    private sealed class FakeRemoteClient : IRemoteToolHostClient
    {
        private readonly Dictionary<string, RemoteToolRoute> _routes = new(StringComparer.Ordinal);
        public int RemoteCalls { get; private set; }
        public bool HasRegistrations => true;
        public string GetPlanningSummary() => "Available Remote Tool Hosts: host-1.";
        public void UpdateRemoteToolDefinitions(IReadOnlyList<ToolDefinition> definitions) { }

        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RemoteToolHostCatalog([], _routes.GetValueOrDefault(threadId)));

        public ValueTask<RemoteToolConnectResult> ConnectAsync(
            string threadId,
            string hostId,
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            SetRoute(threadId);
            var route = _routes[threadId];
            return ValueTask.FromResult(new RemoteToolConnectResult(
                route,
                new RemoteToolEnvironment("host", "test", "user", "workspace"),
                ["ReadFile"],
                []));
        }

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(
            string threadId,
            CancellationToken cancellationToken = default)
        {
            _routes.Remove(threadId, out var previous);
            return ValueTask.FromResult(new RemoteToolDisconnectResult(previous is not null, previous));
        }

        public bool TryGetRoute(string threadId, out RemoteToolRoute route) =>
            _routes.TryGetValue(threadId, out route!);

        public bool TryForkRoute(string parentThreadId, string childThreadId)
        {
            if (!_routes.TryGetValue(parentThreadId, out var route))
                return false;
            _routes[childThreadId] = route;
            return true;
        }

        public ValueTask<ToolExecutionResult> InvokeAsync(
            RemoteToolRoute route,
            ToolDefinition definition,
            string contractHash,
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            RemoteCalls++;
            return ValueTask.FromResult(ToolExecutionResult.Succeeded("remote"));
        }

        public void SetRoute(string threadId) =>
            _routes[threadId] = new RemoteToolRoute("host-1", "workspace-1", "lease-1", "instance-1");
    }
}
