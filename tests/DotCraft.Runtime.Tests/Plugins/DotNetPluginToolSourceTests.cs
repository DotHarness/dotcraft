using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Contributions;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the Host wrapper the aggregate source puts around plugin-contributed Tool sources.</summary>
public sealed class DotNetPluginToolSourceTests : IDisposable
{
    private readonly PluginGenerationHarness _harness = new();
    private readonly ContributionRegistry _registry = new();
    private readonly PluginCallGateRegistry _callGates = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Source_WrapsEachContributionWithAGenerationKeyedBinding()
    {
        var gate = PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("review", "review", "Reviews a diff."));
        var source = new DotNetPluginToolSource(_registry, _callGates);

        var registrations = await source.GetRegistrationsAsync(PlanningContext());

        Assert.Equal("dotnet-plugins", source.SourceId);
        var registration = Assert.Single(registrations);
        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        Assert.Equal("acme.tools", registration.Definition.Id.SourceId);
        Assert.Equal("review", registration.Definition.Id.SourceToolId.Value);
        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Provenance.Kind);
        Assert.Equal("plugin", registration.Definition.Provenance.Origin);
        Assert.Equal(ToolPolicyScope.ProfileManaged, registration.Definition.PolicyScope);
        Assert.Equal("dotnet-plugins:acme.tools:gen-1", registration.Binding.AuthorityReference);
        Assert.StartsWith("dotnet-plugins:acme.tools:gen-1:review:", registration.Binding.Id.Value, StringComparison.Ordinal);
        Assert.Equal(_registry.GetRevision<IToolSource>(), registration.Binding.Revision);
        Assert.Empty(source.Diagnostics);
        Assert.True(gate.IsOpen);

        var described = Assert.Single(source.DescribeTools("acme.tools", "gen-1"));
        Assert.Equal("review", described.Id);
        Assert.Equal("Reviews a diff.", described.Description);
    }

    [Fact]
    public async Task Source_KeepsThePluginsOwnObjectsOutOfTheRegistrationItHandsBack()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var plugin = new StubToolSource("review", "review", "Reviews a diff.");
        registrar.Add<IToolSource>(plugin);
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var contributed = Assert.Single(await plugin.GetRegistrationsAsync(PlanningContext()));

        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));

        Assert.NotSame(contributed, registration);
        Assert.NotSame(contributed.Definition, registration.Definition);
        Assert.NotSame(contributed.Binding, registration.Binding);
        Assert.IsType<DotNetPluginToolProxy>(registration.Binding.Runtime);
        Assert.Same(registration.Binding.Runtime, registration.Binding.Lease);
        // The proxy carries identifiers and Host planning inputs, never a reference to the plugin.
        var pluginObjects = new object[] { plugin, contributed, contributed.Definition, contributed.Binding };
        foreach (var field in registration.Binding.Runtime.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Assert.DoesNotContain(field.GetValue(registration.Binding.Runtime), pluginObjects);
        }
    }

    [Fact]
    public async Task Source_IgnoresContributionsThatDoNotComeFromAPlugin()
    {
        _registry.Add<IToolSource>(new StubToolSource("builtin", "builtin", "Not a plugin Tool."));
        var source = new DotNetPluginToolSource(_registry, _callGates);

        Assert.Empty(await source.GetRegistrationsAsync(PlanningContext()));
    }

    [Fact]
    public async Task Source_SkipsADuplicateToolIdAndReportsIt()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("review", "review", "First."));
        registrar.Add<IToolSource>(new StubToolSource("review", "review_again", "Second."));
        var source = new DotNetPluginToolSource(_registry, _callGates);

        var registrations = await source.GetRegistrationsAsync(PlanningContext());

        // A duplicate id is a plugin bug, not a Host failure: the first registration survives.
        var registration = Assert.Single(registrations);
        Assert.Equal("review", registration.Definition.Name.Name);
        var diagnostic = Assert.Single(source.Diagnostics);
        Assert.Equal("PluginToolContributionInvalid", diagnostic.Code);
        Assert.Equal("acme.tools", diagnostic.PluginId);
        Assert.Contains("more than once", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_ReportsASourceThatThrowsWhilePlanningAndKeepsTheRest()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("bad", "bad", "Bad.") { ThrowsWhilePlanning = true });
        registrar.Add<IToolSource>(new StubToolSource("good", "good", "Good."));
        var source = new DotNetPluginToolSource(_registry, _callGates);

        var registrations = await source.GetRegistrationsAsync(PlanningContext());

        Assert.Equal("good", Assert.Single(registrations).Definition.Name.Name);
        var diagnostic = Assert.Single(source.Diagnostics);
        Assert.Equal("PluginToolContributionInvalid", diagnostic.Code);
        Assert.Equal("Plugin operation failed.", diagnostic.Message);
    }

    [Fact]
    public async Task Source_MaterializesAPluginOwnedRegistrationListInsideTheCallGate()
    {
        var gate = PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var registration = Assert.Single(await new StubToolSource("review", "review", "Reviews a diff.")
            .GetRegistrationsAsync(PlanningContext()));
        using var enumerationStarted = new ManualResetEventSlim();
        using var releaseEnumeration = new ManualResetEventSlim();
        registrar.Add<IToolSource>(new BlockingRegistrationListSource(
            registration,
            enumerationStarted,
            releaseEnumeration));
        var source = new DotNetPluginToolSource(_registry, _callGates);

        var planning = Task.Run(async () => await source.GetRegistrationsAsync(PlanningContext()));
        Assert.True(enumerationStarted.Wait(TimeSpan.FromSeconds(10)));
        var closing = gate.CloseAsync();

        Assert.False(closing.IsCompleted, "The generation gate must cover plugin-owned enumeration.");
        releaseEnumeration.Set();

        Assert.Single(await planning.WaitAsync(TimeSpan.FromSeconds(10)));
        await closing.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Proxy_ReportsUnavailableOnceTheContributionIsRevoked()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var handle = registrar.Add<IToolSource>(new StubToolSource("review", "review", "Reviews a diff."));
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));
        var context = InvocationContext(registration);

        Assert.True((await registration.Binding.Lease.CheckAsync(context)).IsAvailable);
        handle.Dispose();

        var lease = await registration.Binding.Lease.CheckAsync(context);
        Assert.False(lease.IsAvailable);
        Assert.Equal(ToolErrorCodes.Unavailable, lease.Error?.Code);
        var result = await registration.Binding.Runtime.InvokeAsync(context, new JsonObject());
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Unavailable, result.Error?.Code);
    }

    [Fact]
    public async Task Proxy_ReportsUnavailableOnceTheGenerationStopsAdmittingCalls()
    {
        var gate = PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("review", "review", "Reviews a diff."));
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));
        var context = InvocationContext(registration);

        await gate.CloseAsync();

        Assert.False((await registration.Binding.Lease.CheckAsync(context)).IsAvailable);
        var result = await registration.Binding.Runtime.InvokeAsync(context, new JsonObject());
        Assert.Equal(ToolErrorCodes.Unavailable, result.Error?.Code);
    }

    [Fact]
    public async Task Proxy_ReportsUnavailableWhenThePluginsOwnLeaseIsWithdrawn()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var plugin = new StubToolSource("review", "review", "Reviews a diff.");
        registrar.Add<IToolSource>(plugin);
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));

        plugin.LeaseAvailable = false;

        var result = await registration.Binding.Runtime.InvokeAsync(
            InvocationContext(registration),
            new JsonObject());
        Assert.Equal(ToolErrorCodes.Unavailable, result.Error?.Code);
    }

    [Fact]
    public async Task Proxy_CopiesArgumentsInAndResultsOut()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var plugin = new StubToolSource("review", "review", "Reviews a diff.")
        {
            Result = ToolExecutionResult.Succeeded(
                "reviewed",
                JsonDocument.Parse("{\"findings\":2}").RootElement)
        };
        registrar.Add<IToolSource>(plugin);
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));
        var arguments = new JsonObject { ["path"] = "a.cs" };

        var result = await registration.Binding.Runtime.InvokeAsync(
            InvocationContext(registration),
            arguments);

        Assert.True(result.Success);
        Assert.Equal("reviewed", result.Content);
        Assert.Equal(2, result.StructuredContent?.GetProperty("findings").GetInt32());
        Assert.NotSame(result, plugin.Result);
        Assert.NotSame(arguments, plugin.LastArguments);
        Assert.Equal("a.cs", plugin.LastArguments?["path"]?.GetValue<string>());
    }

    [Fact]
    public async Task Proxy_TurnsAMissingOrThrownPluginResultIntoAStableFailure()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("empty", "empty", "Returns nothing usable.")
        {
            Result = null
        });
        registrar.Add<IToolSource>(new StubToolSource("thrower", "thrower", "Throws.") { Throws = true });
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registrations = await source.GetRegistrationsAsync(PlanningContext());

        var invalid = registrations.Single(r => r.Definition.Id.SourceToolId.Value == "empty");
        var invalidResult = await invalid.Binding.Runtime.InvokeAsync(InvocationContext(invalid), new JsonObject());
        Assert.Equal(ToolErrorCodes.ResultInvalid, invalidResult.Error?.Code);

        var thrower = registrations.Single(r => r.Definition.Id.SourceToolId.Value == "thrower");
        var thrownResult = await thrower.Binding.Runtime.InvokeAsync(InvocationContext(thrower), new JsonObject());
        Assert.Equal(ToolErrorCodes.ExecutionFailed, thrownResult.Error?.Code);
        Assert.Equal("Plugin operation failed.", thrownResult.Error?.Message);
    }

    [Fact]
    public async Task Proxy_ReportsAPluginDeclaredErrorVerbatim()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        registrar.Add<IToolSource>(new StubToolSource("review", "review", "Reviews a diff.")
        {
            Result = ToolExecutionResult.Failed(
                new ToolError(
                    "review_unavailable",
                    "The review index is not built.",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["retryAfterSeconds"] = JsonSerializer.SerializeToElement(30)
                    }),
                "could not review")
        });
        var source = new DotNetPluginToolSource(_registry, _callGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));

        var result = await registration.Binding.Runtime.InvokeAsync(
            InvocationContext(registration),
            new JsonObject());

        Assert.False(result.Success);
        Assert.Equal("review_unavailable", result.Error?.Code);
        Assert.Equal(30, result.Error?.Parameters["retryAfterSeconds"].GetInt32());
    }

    [Fact]
    public async Task Source_ForwardsThreadReleaseAndForkToTheContributingSources()
    {
        PublishGate("acme.tools", "gen-1");
        var registrar = _registry.CreateRegistrar(ContributionOrigin.Plugin("acme.tools", "gen-1"));
        var plugin = new StubToolSource("review", "review", "Reviews a diff.");
        registrar.Add<IToolSource>(plugin);
        var source = new DotNetPluginToolSource(_registry, _callGates);

        await source.ReleaseThreadAsync("thread-1");
        var forked = source.TryForkThreadBinding("thread-1", "thread-2");

        Assert.Equal("thread-1", plugin.ReleasedThreadId);
        Assert.True(forked);
        Assert.Equal(("thread-1", "thread-2"), plugin.ForkedThreads);
    }

    [Fact]
    public async Task Generation_SignalsStoppingBeforeDrainingAnInFlightToolCall()
    {
        var dataRoot = _harness.DataRoot("drain");
        Directory.CreateDirectory(dataRoot);
        WritePlugin(
            _harness.PluginRoot("drain"),
            "drain",
            "Drain.Plugin",
            """
            using System;
            using System.IO;
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace Drain;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    context.Contributions.Add<IToolSource>(new Tool(context.DataRoot, context.Lifetime.Stopping));
                    return ValueTask.CompletedTask;
                }
                private sealed class Tool(string dataRoot, CancellationToken stopping)
                    : TestTool("slow", null, "slow", "Blocks until released.")
                {
                    public override async ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                    {
                        File.WriteAllText(Path.Combine(dataRoot, "entered"), "1");
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
                        }
                        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                        {
                        }
                        File.WriteAllText(Path.Combine(dataRoot, "stopping"), "1");
                        while (!File.Exists(Path.Combine(dataRoot, "release")))
                            await Task.Delay(10, cancellationToken);
                        return ToolExecutionResult.Succeeded("drained");
                    }
                }
            }
            """);
        var attempt = await _harness.ActivateAsync("drain");
        var generation = Assert.IsType<PluginGeneration>(attempt.Generation);
        var source = new DotNetPluginToolSource(_harness.Registry, _harness.CallGates);
        var registration = Assert.Single(await source.GetRegistrationsAsync(PlanningContext()));
        var call = Task.Run(async () => await registration.Binding.Runtime.InvokeAsync(
            InvocationContext(registration),
            new JsonObject()));
        await WaitForFileAsync(Path.Combine(dataRoot, "entered"));

        var cleanup = generation.BeginCleanup();
        await WaitForFileAsync(Path.Combine(dataRoot, "stopping"));

        Assert.False(cleanup.IsCompleted, "Teardown must signal stopping, then wait for the call to return.");
        // Routing is already revoked while the in-flight call is still running.
        Assert.Empty(_harness.Registry.Resolve<IToolSource>());
        Assert.False(_harness.CallGates.IsCallable("drain", generation.GenerationId));

        File.WriteAllText(Path.Combine(dataRoot, "release"), "1");
        var remnant = await cleanup.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True((await call).Success);
        Assert.Equal("drained", (await call).Content);
        Assert.Empty(remnant.CleanupErrors);
    }

    private PluginCallGate PublishGate(string pluginId, string generationId)
    {
        var gate = new PluginCallGate();
        _callGates.Publish(pluginId, generationId, gate);
        return gate;
    }

    private static ToolPlanningContext PlanningContext() =>
        new(
            threadId: "thread-1",
            turnId: null,
            workspacePath: Path.GetTempPath(),
            dataPath: Path.GetTempPath(),
            mode: "default",
            profile: null,
            providerCapabilities: null,
            revision: 7);

    private static ToolInvocationContext InvocationContext(ToolRegistration registration) =>
        new(
            "thread-1",
            "turn-1",
            "call-1",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            registration.Binding.Revision,
            DateTimeOffset.UtcNow);

    /// <summary>Stands in for a plugin's own Tool source: it owns the definition, the binding, and the runtime.</summary>
    private sealed class StubToolSource(
        string toolId,
        string name,
        string description,
        string schema = "{\"type\":\"object\"}") :
        IToolSource,
        IToolRuntime,
        IToolBindingLease,
        IThreadScopedToolSource,
        IThreadForkToolBindingSource
    {
        public string SourceId { get; } = $"plugin:{toolId}:{name}";

        public ToolExecutionResult? Result { get; init; } = ToolExecutionResult.Succeeded("ok");

        public bool Throws { get; init; }

        public bool ThrowsWhilePlanning { get; init; }

        public bool LeaseAvailable { get; set; } = true;

        public JsonObject? LastArguments { get; private set; }

        public string? ReleasedThreadId { get; private set; }

        public (string Parent, string Child)? ForkedThreads { get; private set; }

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            if (ThrowsWhilePlanning)
                throw new InvalidOperationException("planning exploded");

            var id = new ToolDefinitionId(ToolSourceKind.PluginNative, SourceId, new SourceToolId(toolId));
            var definition = new ToolDefinition(
                id,
                new ToolName(null, name),
                description,
                JsonDocument.Parse(schema).RootElement);
            var binding = new ToolRuntimeBinding(
                new RuntimeBindingId($"{SourceId}:{context.Revision}"),
                id,
                this,
                this,
                SourceId,
                context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                [new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair)]);
        }

        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(LeaseAvailable
                ? ToolBindingLeaseResult.Available
                : ToolBindingLeaseResult.Unavailable("The plugin withdrew the binding."));

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            if (Throws)
                throw new InvalidOperationException("tool exploded");
            LastArguments = arguments;
            return ValueTask.FromResult(Result!);
        }

        public ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            ReleasedThreadId = threadId;
            return ValueTask.CompletedTask;
        }

        public bool TryForkThreadBinding(string parentThreadId, string childThreadId)
        {
            ForkedThreads = (parentThreadId, childThreadId);
            return true;
        }
    }

    private sealed class BlockingRegistrationListSource(
        ToolRegistration registration,
        ManualResetEventSlim enumerationStarted,
        ManualResetEventSlim releaseEnumeration) : IToolSource
    {
        public string SourceId => "blocking-list";

        public int Priority => 0;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                new BlockingRegistrationList(registration, enumerationStarted, releaseEnumeration));
    }

    private sealed class BlockingRegistrationList(
        ToolRegistration registration,
        ManualResetEventSlim enumerationStarted,
        ManualResetEventSlim releaseEnumeration) : IReadOnlyList<ToolRegistration>
    {
        public int Count => 1;

        public ToolRegistration this[int index] => index == 0
            ? registration
            : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<ToolRegistration> GetEnumerator()
        {
            enumerationStarted.Set();
            if (!releaseEnumeration.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release plugin registration enumeration.");
            yield return registration;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
