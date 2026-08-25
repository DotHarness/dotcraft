using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

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
        Assert.Equal("alpha__read", snapshot.ProviderFlatNames[alpha.Definition.Name]);
        Assert.Equal("beta__read", snapshot.ProviderFlatNames[beta.Definition.Name]);
        Assert.True(snapshot.TryResolveProviderFlatName("alpha__read", out var resolved));
        Assert.Equal(alpha.Definition.Name, resolved);
        Assert.False(snapshot.TryResolveProviderFlatName("ALPHA__READ", out _));
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
        Assert.Equal(snapshot.ProviderFlatNameIndex, filtered.ProviderFlatNameIndex);
        Assert.Equal(10, filtered.Revision);
    }

    [Fact]
    public async Task Build_OpenAINativeDeferredSearch_AddsDispatchableRegistrationWithProviderResult()
    {
        var deferred = Registration(
            "mcp__catalog_service",
            "search_records",
            "catalog-service:catalog-service",
            ToolSourceKind.Mcp,
            ToolExposure.Deferred,
            namespaceDescription: "Search and review catalog records.");
        var capabilities = Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.OpenAIResponses);

        var snapshot = new EffectiveToolSnapshotBuilder().Build([deferred], 12, capabilities);

        var searchName = new ToolName(null, DeferredToolSearchRuntime.CanonicalName);
        var registration = snapshot.Registrations[searchName];
        Assert.Equal(ToolSourceKind.CoreNative, registration.Definition.Provenance.Kind);
        Assert.Equal("core.deferred-search", registration.Definition.Presentation?.Id.Value);
        Assert.Equal("SearchTools", snapshot.ProviderFlatNames[searchName]);
        Assert.Contains(registration.Definition, snapshot.ModelVisibleDefinitions);
        var searchSchema = registration.Definition.InputSchema;
        Assert.Equal(
            ["query", "max_results", "maxResults"],
            searchSchema.GetProperty("properties").EnumerateObject().Select(static property => property.Name));
        Assert.Equal(
            0,
            searchSchema.GetProperty("properties").GetProperty("max_results").GetProperty("minimum").GetInt32());
        Assert.Equal(
            0,
            searchSchema.GetProperty("properties").GetProperty("maxResults").GetProperty("minimum").GetInt32());
        Assert.Equal("query", Assert.Single(searchSchema.GetProperty("required").EnumerateArray()).GetString());
        Assert.False(searchSchema.GetProperty("additionalProperties").GetBoolean());

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "records" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        var providerResult = Assert.IsType<NativeToolSearchOutput>(result.ProviderResult);
        var namespaceTool = Assert.Single(providerResult.Tools);
        Assert.Equal("mcp__catalog_service", namespaceTool.Name);
        Assert.Equal("Search and review catalog records.", namespaceTool.Description);
        var child = Assert.Single(namespaceTool.Tools!);
        Assert.Equal("search_records", child.Name);
        Assert.DoesNotContain("mcp__catalog_service__", child.Name, StringComparison.Ordinal);
        Assert.True(snapshot.TryResolveProviderNamespacedName(namespaceTool.Name, child.Name, out var resolved));
        Assert.Equal(deferred.Definition.Name, resolved);
        var callResult = await new ToolDispatcher().DispatchAsync(
            snapshot,
            resolved,
            new JsonObject(),
            new ToolInvocationRequest("thread", "turn", "mcp-call", ToolInvocationAudience.Model));
        Assert.True(callResult.Success);
        var runtime = Assert.IsType<DeferredToolSearchRuntime>(registration.Binding.Runtime);
        Assert.Contains(
            "mcp__catalog_service__search_records",
            runtime.ActivationIndex.GetActivatedToolNames());
    }

    [Fact]
    public async Task Build_NamespaceDescriptionConflict_UsesOneGenericContainerAndDiagnostic()
    {
        var repositories = Registration(
            "mcp__catalog_service",
            "search_records",
            "catalog-service:catalog-service",
            ToolSourceKind.Mcp,
            ToolExposure.Deferred,
            namespaceDescription: "Search records.");
        var users = Registration(
            "mcp__catalog_service",
            "search_owners",
            "catalog-service:catalog-service",
            ToolSourceKind.Mcp,
            ToolExposure.Direct,
            namespaceDescription: "Search owners.");
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [repositories, users],
            16,
            Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.OpenAIResponses));

        var diagnostic = Assert.Single(
            snapshot.Diagnostics,
            value => value.Code == ToolSnapshotDiagnosticCodes.ConflictingNamespaceDescription);
        Assert.Equal("mcp__catalog_service", diagnostic.ToolName.Namespace);
        Assert.Equal(
            "Tools in the mcp__catalog_service namespace.",
            snapshot.NamespaceDescriptions["mcp__catalog_service"]);
        var directTool = Assert.Single(AgentFactory.ProjectSnapshotTools(snapshot), tool => tool.Name.Contains("search_owners", StringComparison.Ordinal));
        Assert.Equal(
            "Tools in the mcp__catalog_service namespace.",
            ToolNamespaceMetadataResolver.GetDescription(directTool));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "search" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        var providerResult = Assert.IsType<NativeToolSearchOutput>(result.ProviderResult);
        var namespaceTool = Assert.Single(providerResult.Tools);
        Assert.Equal("mcp__catalog_service", namespaceTool.Name);
        Assert.Equal("Tools in the mcp__catalog_service namespace.", namespaceTool.Description);
        Assert.Equal("search_records", Assert.Single(namespaceTool.Tools!).Name);
    }

    [Fact]
    public async Task Build_DeferredSearch_MatchesNamespaceDescription()
    {
        var deferred = Registration(
            "mcp__catalog",
            "get_record",
            "catalog",
            ToolSourceKind.Mcp,
            ToolExposure.Deferred,
            namespaceDescription: "review catalog records");
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            15,
            Capabilities(DeferredToolLoadingMode.Simulated, "test"));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "catalog records" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Contains("mcp__catalog__get_record", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_AnthropicNativeDeferredSearch_PreservesToolReferenceContent()
    {
        var deferred = Registration("mcp__catalog", "get_record", "catalog", ToolSourceKind.Mcp,
            ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            13,
            Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.Anthropic));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "select:mcp__catalog__get_record" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        var contents = Assert.IsAssignableFrom<IEnumerable<AIContent>>(result.ProviderResult).ToArray();
        var reference = Assert.IsType<DeferredToolReferenceContent>(Assert.Single(contents));
        Assert.Equal("mcp__catalog__get_record", reference.ToolName);
    }

    [Fact]
    public async Task Build_AnthropicNativeDeferredSearch_KeywordReturnsQualifiedToolReference()
    {
        var deferred = Registration(
            "fixture",
            "LookupRecords",
            "fixture",
            ToolSourceKind.RuntimeDynamic,
            ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            14,
            Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.Anthropic));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "LookupRecords" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        var contents = Assert.IsAssignableFrom<IEnumerable<AIContent>>(result.ProviderResult).ToArray();
        var reference = Assert.IsType<DeferredToolReferenceContent>(Assert.Single(contents));
        Assert.Equal("fixture__LookupRecords", reference.ToolName);
    }

    [Fact]
    public async Task Build_AnthropicNativeDeferredSearch_LocalSelectReturnsNoMatchWithoutActivation()
    {
        var deferred = Registration(
            "fixture",
            "LookupRecords",
            "fixture",
            ToolSourceKind.RuntimeDynamic,
            ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            15,
            Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.Anthropic));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            NativeToolSearchTool.ToolName,
            new JsonObject { ["query"] = "select:LookupRecords" },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.DoesNotContain("fixture__LookupRecords", result.Content, StringComparison.Ordinal);
        Assert.Equal("No matching tools found. Try different keywords.", Assert.IsType<string>(result.ProviderResult));
        var search = Assert.IsType<DeferredToolSearchRuntime>(
            snapshot.Registrations[new ToolName(null, DeferredToolSearchRuntime.CanonicalName)].Binding.Runtime);
        Assert.Empty(search.ActivationIndex.GetActivatedToolNames());

        var localCall = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            "LookupRecords",
            new JsonObject(),
            new ToolInvocationRequest("thread", "turn", "local-call", ToolInvocationAudience.Model));
        Assert.False(localCall.Success);
        Assert.Equal(ToolErrorCodes.NotFound, localCall.Error?.Code);
    }

    [Fact]
    public async Task Build_SimulatedDeferredSearch_UsesCanonicalProviderNameAndTextResult()
    {
        var deferred = Registration(null, "later", "core", ToolSourceKind.CoreNative,
            ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            14,
            Capabilities(DeferredToolLoadingMode.Simulated, ModelProviderProtocols.OpenAIChatCompletions));

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            DeferredToolSearchRuntime.CanonicalName,
            new JsonObject { ["query"] = "later", ["maxResults"] = 2 },
            new ToolInvocationRequest("thread", "turn", "call", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Null(result.ProviderResult);
        Assert.Contains("later", result.Content);
    }

    [Fact]
    public void WithModelExposure_RemovingEveryDeferredDefinition_RemovesSearchRegistration()
    {
        var deferred = Registration(null, "later", "core", ToolSourceKind.CoreNative,
            ToolExposure.Deferred);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            [deferred],
            15,
            Capabilities(DeferredToolLoadingMode.Native, ModelProviderProtocols.OpenAIResponses));

        var filtered = snapshot.WithModelExposure(static _ => false);

        Assert.Empty(filtered.DeferredDefinitions);
        Assert.DoesNotContain(
            filtered.Registrations.Values,
            DeferredToolSearchRuntime.IsRegistration);
        Assert.False(filtered.TryResolveProviderFlatName(NativeToolSearchTool.ToolName, out _));
    }

    [Fact]
    public void ToolName_RejectsInvalidControlledIdentities()
    {
        Assert.Throws<ArgumentException>(() => new ToolName(null, "a.b"));
        Assert.Throws<ArgumentException>(() => new ToolName("bad namespace", "read"));
        Assert.Throws<ArgumentException>(() => new ToolName("valid", "read-item"));
    }

    [Theory]
    [InlineData("catalog-service:catalog-service", "search_records")]
    [InlineData("mcp__catalog_service", "search-records")]
    [InlineData("mcp__catalog_service", "")]
    public void TryResolveProviderNamespacedName_InvalidProviderIdentity_FailsClosed(
        string toolNamespace,
        string localName)
    {
        var registration = Registration(
            "mcp__catalog_service",
            "search_records",
            "catalog-service:catalog-service",
            ToolSourceKind.Mcp);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);

        var exception = Record.Exception(() =>
            snapshot.TryResolveProviderNamespacedName(toolNamespace, localName, out _));

        Assert.Null(exception);
        Assert.False(snapshot.TryResolveProviderNamespacedName(toolNamespace, localName, out _));
    }

    [Fact]
    public void ToolRegistration_DeferredNamespaceMustMatchCanonicalDefinition()
    {
        var registration = Registration(
            "mcp__catalog_service",
            "search_records",
            "catalog-service:catalog-service",
            ToolSourceKind.Mcp);

        Assert.Throws<ArgumentException>(() => new ToolRegistration(
            registration.Definition,
            registration.Binding,
            registration.ProjectionShape,
            ToolExposure.Deferred,
            deferred: new DeferredToolDescriptor("catalog-service:catalog-service", "Search records")));
    }

    [Fact]
    public void Build_ProviderFlatAlias_PreservesSafeCompositeIdentity()
    {
        var ordinary = Registration("mcp__catalog", "get_record", "catalog", ToolSourceKind.Mcp);
        var first = new EffectiveToolSnapshotBuilder().Build([ordinary], 1);

        Assert.Equal("mcp__catalog__get_record", first.ProviderFlatNames[ordinary.Definition.Name]);
        Assert.Equal(ordinary.Definition.Name, first.ProviderFlatNameIndex["mcp__catalog__get_record"]);
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
            "C:\\workspace\\.craft",
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
        ToolInvocationAudience audiences = ToolInvocationAudience.Model | ToolInvocationAudience.Host,
        string? namespaceDescription = null)
    {
        var canonicalNamespace = exposure == ToolExposure.Deferred
            ? toolNamespace ?? "default"
            : toolNamespace;
        var sourceToolId = new SourceToolId(name);
        var definitionId = new ToolDefinitionId(kind, sourceId, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(canonicalNamespace, name),
            $"Run {name}",
            Json("""{"type":"object"}"""),
            provenance: new ToolProvenance(kind, sourceId),
            namespaceDescription: namespaceDescription);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{sourceId}-{name}"),
            definitionId,
            new FakeRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            $"authority:{sourceId}",
            revision: 1);
        var deferred = exposure == ToolExposure.Deferred
            ? new DeferredToolDescriptor(
                canonicalNamespace!,
                $"Search {name}",
                namespaceDescription)
            : null;
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            exposure,
            audiences,
            deferred);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ProviderHostedCapabilityPlan Capabilities(
        DeferredToolLoadingMode mode,
        string protocol) =>
        new()
        {
            DeferredToolSearch = new DeferredToolSearchPlan(mode, mode.ToString(), protocol, 5, null)
        };

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
