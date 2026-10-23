using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ToolRestrictionContributionTests : IDisposable
{
    private const string ThreadId = "thread-a";

    private readonly ContributionAgentHost _host = new("ToolRestrictionContribution");

    [Fact]
    public async Task MaskingRemovesTheRegistrationForEveryAudience_ButNotARuntimeManagedOne()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolRestriction>(new MaskRestriction("write"));
        registry.Add<IToolRestriction>(new MaskRestriction("owned"));
        var snapshot = await BuildAsync(
            registry,
            new StubToolSource("stub", [
                Registration("read"),
                Registration("write"),
                Registration("owned", policyScope: ToolPolicyScope.RuntimeManaged)
            ]));
        var masked = new ToolName(null, "write");
        Assert.Contains(new ToolName(null, "owned"), snapshot.Registrations.Keys);

        Assert.DoesNotContain(masked, snapshot.Registrations.Keys);
        Assert.DoesNotContain(masked, snapshot.ProviderFlatNames.Keys);
        Assert.DoesNotContain(snapshot.ModelVisibleDefinitions, definition => definition.Name == masked);
        Assert.DoesNotContain(snapshot.SourceRegistrations, registration => registration.Definition.Name == masked);

        foreach (var audience in new[]
                 {
                     ToolInvocationAudience.Model,
                     ToolInvocationAudience.Host,
                     ToolInvocationAudience.App
                 })
        {
            var result = await new ToolDispatcher().DispatchAsync(
                snapshot,
                masked,
                [],
                new ToolInvocationRequest(ThreadId, "turn-1", "call-1", audience));
            Assert.False(result.Success);
            Assert.Equal(ToolErrorCodes.NotFound, result.Error?.Code);
        }
    }

    [Fact]
    public async Task RewritingDescriptionAndSchema_ReachesTheModelVisibleDefinitionByteIdenticallyAcrossBuilds()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolRestriction>(new RewriteRestriction(
            "read",
            "Read, but only inside the sandbox.",
            Json("""{"type":"object","properties":{"path":{"type":"string"}}}""")));
        registry.Add<IToolRestriction>(new MaskRestriction("write"));
        var factory = CreateFactory(registry);

        var snapshot = await BuildAsync(factory, Source("read", "write"), ThreadId);
        var second = await BuildAsync(factory, Source("read", "write"), ThreadId);

        var definition = Assert.Single(snapshot.ModelVisibleDefinitions);
        Assert.Equal("Read, but only inside the sandbox.", definition.Description);
        Assert.Contains("path", definition.InputSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(PromptSurface(snapshot), PromptSurface(second));
    }

    [Fact]
    public async Task ExposureEditsNarrowOnly()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolRestriction>(new ExposureRestriction("read", ToolExposure.Hidden));
        registry.Add<IToolRestriction>(new ExposureRestriction("hidden", ToolExposure.Direct));

        var snapshot = await BuildAsync(
            registry,
            new StubToolSource("stub", [Registration("read"), Registration("hidden", ToolExposure.Hidden)]));

        Assert.Empty(snapshot.ModelVisibleDefinitions);
        Assert.Equal(2, snapshot.Registrations.Count);
    }

    [Fact]
    public async Task ARestrictionMayNeverEmptyTheToolSurface()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolRestriction>(new MaskAllRestriction());

        var snapshot = await BuildAsync(registry, Source("read", "write"));

        Assert.Equal(2, snapshot.Registrations.Count);
    }

    [Fact]
    public async Task RestrictionsFold_SoALaterOneSeesTheEarlierEdit()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolRestriction>(
            new RewriteRestriction("read", "first", null),
            new ContributionOptions(Order: 10));
        registry.Add<IToolRestriction>(
            new AppendingRestriction("read", "+second"),
            new ContributionOptions(Order: 20));

        var snapshot = await BuildAsync(registry, Source("read"));

        Assert.Equal("first+second", Assert.Single(snapshot.ModelVisibleDefinitions).Description);
    }

    [Fact]
    public async Task LateRegistrationAndRevocation_ReachTheNextBuild()
    {
        var registry = new ContributionRegistry();
        var factory = CreateFactory(registry);
        Assert.Equal(2, (await BuildAsync(factory, Source("read", "write"), ThreadId)).Registrations.Count);

        var handle = registry.Add<IToolRestriction>(new MaskRestriction("write"));
        registry.Add<IToolRestriction>(new MaskRestriction("read"), ContributionOptions.ForThread("thread-b"));
        Assert.Single((await BuildAsync(factory, Source("read", "write"), ThreadId)).Registrations);
        Assert.Equal(
            new ToolName(null, "edit"),
            Assert.Single((await BuildAsync(factory, Source("read", "write", "edit"), "thread-b")).Registrations.Keys));

        handle.Dispose();
        Assert.Equal(2, (await BuildAsync(factory, Source("read", "write"), ThreadId)).Registrations.Count);
    }

    private static string PromptSurface(EffectiveToolSnapshot snapshot) =>
        string.Join(
            "\n",
            snapshot.ModelVisibleDefinitions.Select(definition =>
                $"{definition.Name}{definition.Description}{definition.InputSchema.GetRawText()}"));

    private ValueTask<EffectiveToolSnapshot> BuildAsync(IContributionView contributions, IToolSource source) =>
        BuildAsync(CreateFactory(contributions), source, ThreadId);

    private static ValueTask<EffectiveToolSnapshot> BuildAsync(
        AgentFactory factory,
        IToolSource source,
        string threadId) =>
        factory.BuildToolSnapshotAsync([source], Planning(threadId));

    private static ToolPlanningContext Planning(string threadId) =>
        new(threadId, null, Path.GetTempPath(), Path.GetTempPath(), "agent", null, null, 1);

    private static StubToolSource Source(params string[] names) =>
        new("stub", names.Select(name => Registration(name)).ToArray());

    private static ToolRegistration Registration(
        string name,
        ToolExposure exposure = ToolExposure.Direct,
        ToolPolicyScope policyScope = ToolPolicyScope.ProfileManaged) =>
        ContributionTools.Registration(new ToolName(null, name), exposure: exposure, policyScope: policyScope);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private AgentFactory CreateFactory(IContributionView? contributions) => _host.CreateFactory(contributions);

    public void Dispose() => _host.Dispose();

    private sealed class StubToolSource(string sourceId, IReadOnlyList<ToolRegistration> registrations) : IToolSource
    {
        public string SourceId { get; } = sourceId;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(registrations);
    }

    private sealed class MaskRestriction(string toolName) : IToolRestriction
    {
        public string Name => "mask";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            string.Equals(context.Definition.Name.Name, toolName, StringComparison.Ordinal)
                ? new ToolRestrictionEdit { Mask = true }
                : null;
    }

    private sealed class MaskAllRestriction : IToolRestriction
    {
        public string Name => "mask-all";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            new ToolRestrictionEdit { Mask = true };
    }

    private sealed class RewriteRestriction(string toolName, string? description, JsonElement? schema)
        : IToolRestriction
    {
        public string Name => "rewrite";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            string.Equals(context.Definition.Name.Name, toolName, StringComparison.Ordinal)
                ? new ToolRestrictionEdit { Description = description, InputSchema = schema }
                : null;
    }

    private sealed class AppendingRestriction(string toolName, string suffix) : IToolRestriction
    {
        public string Name => "append";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            string.Equals(context.Definition.Name.Name, toolName, StringComparison.Ordinal)
                ? new ToolRestrictionEdit { Description = context.Definition.Description + suffix }
                : null;
    }

    private sealed class ExposureRestriction(string toolName, ToolExposure exposure) : IToolRestriction
    {
        public string Name => "expose";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            string.Equals(context.Definition.Name.Name, toolName, StringComparison.Ordinal)
                ? new ToolRestrictionEdit { Exposure = exposure }
                : null;
    }
}
