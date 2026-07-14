using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class EffectiveToolSnapshotTests
{
    [Fact]
    public void Build_DuplicateCanonicalName_QuarantinesEveryConflictWithSafeProvenance()
    {
        var first = Registration(null, "read", "core", ToolSourceKind.CoreNative);
        var second = Registration(null, "read", "plugin-a", ToolSourceKind.PluginNative);

        var snapshot = new EffectiveToolSnapshotBuilder().Build([first, second], revision: 7);

        Assert.Empty(snapshot.Registrations);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ToolSnapshotDiagnosticCodes.DuplicateCanonicalName, diagnostic.Code);
        Assert.Equal(new ToolName(null, "read"), diagnostic.ToolName);
        Assert.Equal(["core", "plugin-a"], diagnostic.Provenances.Select(value => value.SourceId));
    }

    [Fact]
    public void Build_SameLocalNameInDifferentNamespaces_PreservesBothAndExactReverseMapping()
    {
        var alpha = Registration("alpha", "read", "source-a", ToolSourceKind.PluginNative);
        var beta = Registration("beta", "read", "source-b", ToolSourceKind.Mcp);

        var snapshot = new EffectiveToolSnapshotBuilder().Build([beta, alpha], revision: 3);

        Assert.Equal(2, snapshot.Registrations.Count);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal("alpha__read", snapshot.ProviderCallNames[alpha.Definition.Name]);
        Assert.Equal("beta__read", snapshot.ProviderCallNames[beta.Definition.Name]);
        Assert.True(snapshot.TryResolveProviderCallName("alpha__read", out var resolved));
        Assert.Equal(alpha.Definition.Name, resolved);
        Assert.False(snapshot.TryResolveProviderCallName("ALPHA__READ", out _));
    }

    [Fact]
    public void Build_ProjectsDirectDeferredAndHiddenWithoutDroppingHiddenRuntime()
    {
        var direct = Registration(null, "direct", "core", ToolSourceKind.CoreNative);
        var modelOnly = Registration(null, "model_only", "core", ToolSourceKind.CoreNative,
            ToolExposure.DirectModelOnly);
        var deferred = Registration("search", "later", "mcp", ToolSourceKind.Mcp,
            ToolExposure.Deferred);
        var hidden = Registration(null, "host_only", "core", ToolSourceKind.CoreNative,
            ToolExposure.Hidden,
            ToolInvocationAudience.Host);

        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [hidden, deferred, modelOnly, direct],
            revision: 9);

        Assert.Equal(4, snapshot.Registrations.Count);
        Assert.Equal(
            ["direct", "model_only"],
            snapshot.ModelVisibleDefinitions.Select(value => value.Name.Name));
        var deferredDefinitions = Assert.Single(snapshot.DeferredDefinitions);
        Assert.Equal("search", deferredDefinitions.Key);
        Assert.Equal("later", Assert.Single(deferredDefinitions.Value).Name.Name);
        Assert.Contains(hidden.Definition.Name, snapshot.Registrations.Keys);
    }

    [Fact]
    public void WithModelExposure_FiltersDirectAndDeferredButKeepsDispatchRegistry()
    {
        var direct = Registration(null, "direct", "core", ToolSourceKind.CoreNative);
        var denied = Registration(null, "denied", "core", ToolSourceKind.CoreNative);
        var deferred = Registration("search", "later", "mcp", ToolSourceKind.Mcp, ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([direct, denied, deferred], revision: 10);

        var filtered = snapshot.WithModelExposure(definition =>
            definition.Name != denied.Definition.Name && definition.Name != deferred.Definition.Name);

        Assert.Equal([direct.Definition.Name], filtered.ModelVisibleDefinitions.Select(value => value.Name));
        Assert.Empty(filtered.DeferredDefinitions);
        Assert.Equal(3, filtered.Registrations.Count);
        Assert.Equal(snapshot.ProviderCallNameIndex, filtered.ProviderCallNameIndex);
        Assert.Equal(10, filtered.Revision);
    }

    [Fact]
    public void Build_ProviderSanitizationCollision_GivesBothDeterministicHashedNames()
    {
        var dotted = Registration(null, "a.b", "first", ToolSourceKind.CoreNative);
        var spaced = Registration(null, "a b", "second", ToolSourceKind.CoreNative);

        var forward = new EffectiveToolSnapshotBuilder().Build([dotted, spaced], revision: 1);
        var reverse = new EffectiveToolSnapshotBuilder().Build([spaced, dotted], revision: 1);

        var dottedName = forward.ProviderCallNames[dotted.Definition.Name];
        var spacedName = forward.ProviderCallNames[spaced.Definition.Name];
        Assert.NotEqual(dottedName, spacedName);
        Assert.Matches("^a_b_[0-9a-f]{12}$", dottedName);
        Assert.Matches("^a_b_[0-9a-f]{12}$", spacedName);
        Assert.Equal(dottedName, reverse.ProviderCallNames[dotted.Definition.Name]);
        Assert.Equal(spacedName, reverse.ProviderCallNames[spaced.Definition.Name]);
    }

    [Fact]
    public void Build_McpCanonicalNamespace_UsesCodexWirePrefixAndUtf8SafeHashFixture()
    {
        var ordinary = Registration("mcp__github", "get_issue", "github", ToolSourceKind.Mcp);
        var longTool = Registration(
            "mcp__server.with punctuation",
            new string('x', 80),
            "long",
            ToolSourceKind.Mcp);

        var first = new EffectiveToolSnapshotBuilder().Build([longTool, ordinary], 1);
        var second = new EffectiveToolSnapshotBuilder().Build([ordinary, longTool], 1);

        Assert.Equal("mcp__github__get_issue", first.ProviderCallNames[ordinary.Definition.Name]);
        var projected = first.ProviderCallNames[longTool.Definition.Name];
        Assert.Equal("mcp__server_with_punctuation__xxxxxxxxxxxxxxxxxxxxx_5ca4c9b75c2d", projected);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(projected) <= ProviderToolProjector.MaximumNameBytes);
        Assert.Matches("_[0-9a-f]{12}$", projected);
        Assert.Equal(projected, second.ProviderCallNames[longTool.Definition.Name]);
        Assert.Equal(new ToolName("mcp__server.with punctuation", new string('x', 80)),
            first.ProviderCallNameIndex[projected]);
    }

    [Fact]
    public async Task BuildAsync_CollectsSourcesByPriorityThenOrdinalSourceId()
    {
        var observed = new List<string>();
        var sourceB = new FakeSource("b", 10, observed, Registration(null, "b", "b", ToolSourceKind.CoreNative));
        var sourceA = new FakeSource("a", 10, observed, Registration(null, "a", "a", ToolSourceKind.CoreNative));
        var sourceFirst = new FakeSource("z", 0, observed, Registration(null, "z", "z", ToolSourceKind.CoreNative));
        var context = new ToolPlanningContext(
            "thread",
            "turn",
            "C:\\workspace",
            "default",
            null,
            new HashSet<string>(StringComparer.Ordinal),
            11);

        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
            [sourceB, sourceA, sourceFirst],
            context);

        Assert.Equal(["z", "a", "b"], observed);
        Assert.Equal(11, snapshot.Revision);
        Assert.Equal(3, snapshot.Registrations.Count);
    }

    private static ToolRegistration Registration(
        string? toolNamespace,
        string name,
        string sourceId,
        ToolSourceKind kind,
        ToolExposure exposure = ToolExposure.Direct,
        ToolInvocationAudience audiences = ToolInvocationAudience.Model | ToolInvocationAudience.Host)
    {
        var sourceToolId = new SourceToolId(name);
        var definitionId = new ToolDefinitionId(kind, sourceId, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(toolNamespace, name),
            $"Run {name}",
            Json("""{"type":"object"}"""),
            provenance: new ToolProvenance(kind, sourceId));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{sourceId}-{name}"),
            definitionId,
            new FakeRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            $"authority:{sourceId}",
            revision: 1);
        var deferred = exposure == ToolExposure.Deferred
            ? new DeferredToolDescriptor(toolNamespace ?? "default", $"Search {name}")
            : null;
        return new ToolRegistration(definition, binding, exposure, audiences, deferred);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class FakeSource(
        string sourceId,
        int priority,
        List<string> observed,
        ToolRegistration registration) : IToolSource
    {
        public string SourceId => sourceId;
        public int Priority => priority;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            observed.Add(SourceId);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([registration]);
        }
    }
}
