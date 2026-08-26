using System.ComponentModel;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Microsoft.Extensions.AI;

namespace DotCraft.Runtime;

internal sealed record DotNetPluginBuildDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? Path,
    string? Phase,
    int? Line,
    int? Column);

internal sealed record DotNetPluginBuildResult(
    string Outcome,
    string? Fingerprint,
    string? State,
    IReadOnlyList<DotNetPluginBuildDiagnostic> Diagnostics);

/// <summary>Publishes the stable, on-demand managed plugin authoring namespace.</summary>
internal sealed class DotNetPluginAuthoringToolSource : IToolSource
{
    internal const string Namespace = "DotNetPlugin";
    internal const string Id = "dotnet-plugin-authoring";

    private const string NamespaceDescription =
        "Inspect the current DotCraft managed plugin API and build workspace .NET plugin projects.";

    private readonly AppConfig _config;
    private readonly DotNetPluginRuntimeManager _runtime;
    private readonly Lazy<DotNetPluginReferenceSet> _references =
        new(DotNetPluginReferenceSet.LoadCurrentHost, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<DotNetPluginApiInspector> _inspector;

    public DotNetPluginAuthoringToolSource(
        AppConfig config,
        DotNetPluginRuntimeManager runtime)
    {
        _config = config;
        _runtime = runtime;
        _inspector = new Lazy<DotNetPluginApiInspector>(
            () => new DotNetPluginApiInspector(_references.Value),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string SourceId => Id;

    public int Priority => 20;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var methods = new DotNetPluginAuthoringToolMethods(
            ResolveAuthoringDataPath(context),
            _runtime,
            _references,
            _inspector);
        var functions = new AIFunction[]
        {
            DotCraft.GeneratedTools.Runtime.GeneratedToolFunctions
                .DotNetPluginAuthoringToolMethods_Inspect(methods),
            DotCraft.GeneratedTools.Runtime.GeneratedToolFunctions
                .DotNetPluginAuthoringToolMethods_BuildAsync(methods)
        };
        var exposure = _config.Tools.DeferredLoading.Strategy == AppConfig.DeferredLoadingStrategy.Off
            ? ToolExposure.Direct
            : ToolExposure.Deferred;
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
            functions
                .OrderBy(static function => function.Name, StringComparer.Ordinal)
                .Select(function => CreateRegistration(function, context.Revision, exposure))
                .ToArray());
    }

    private static ToolRegistration CreateRegistration(
        AIFunction function,
        long revision,
        ToolExposure exposure)
    {
        var sourceToolId = new SourceToolId(function.Name);
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, Id, sourceToolId);
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(Namespace, function.Name),
            function.Description,
            function.JsonSchema,
            function.ReturnJsonSchema,
            policyHints: new ToolPolicyHints(
                ReadOnly: function.Name == nameof(DotNetPluginAuthoringToolMethods.Inspect)),
            provenance: new ToolProvenance(ToolSourceKind.CoreNative, Id, "native"),
            namespaceDescription: NamespaceDescription);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"native:{Id}:{function.Name}:{revision}"),
            definitionId,
            new AIFunctionToolRuntime(function),
            ToolBindingLeases.AlwaysAvailable,
            $"native:{Id}",
            revision);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            exposure,
            ToolInvocationAudience.Model,
            exposure == ToolExposure.Deferred
                ? new DeferredToolDescriptor(Namespace, function.Description, NamespaceDescription)
                : null);
    }

    private static string ResolveAuthoringDataPath(ToolPlanningContext context)
    {
        var dataDirectoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(context.DataPath));
        return new DotCraftPathRoot(context.WorkspacePath).Resolve(dataDirectoryName);
    }
}

internal sealed class DotNetPluginAuthoringToolMethods(
    string dataPath,
    DotNetPluginRuntimeManager runtime,
    Lazy<DotNetPluginReferenceSet> references,
    Lazy<DotNetPluginApiInspector> inspector)
{
    [GeneratedTool(Name = "Inspect")]
    [ToolSchema(DisallowAdditionalProperties = true)]
    [Description("Find public managed plugin API types and members in the current DotCraft Host, including their XML documentation summaries.")]
    public IReadOnlyList<DotNetPluginApiSymbol> Inspect(
        [Description("Fully-qualified name, simple type name, or member name to find.")] string query) =>
        inspector.Value.Inspect(query);

    [GeneratedTool(Name = "Build")]
    [ToolSchema(DisallowAdditionalProperties = true)]
    [Description("Compile, preflight, publish, and activate one project under .craft/plugin-projects. The new contributions become available on the next Turn.")]
    public async Task<DotNetPluginBuildResult> BuildAsync(
        [Description("Canonical id of the workspace .NET plugin project to build.")] string pluginId,
        CancellationToken cancellationToken = default)
    {
        using var preparation = new DotNetPluginCompiler(references.Value).Prepare(dataPath, pluginId);
        if (!preparation.Succeeded)
        {
            return new DotNetPluginBuildResult(
                "failed",
                null,
                null,
                ToDiagnostics(preparation.Diagnostics));
        }

        var result = await runtime.ApplyAuthoringBuildAsync(pluginId, preparation, cancellationToken)
            .ConfigureAwait(false);
        var runtimeInfo = result.Runtime;
        var active = runtimeInfo?.State == PluginDotnetRuntimeState.Active;
        var outcome = result.Outcome == PluginRuntimeMutationOutcome.NoChange && active
            ? "noChange"
            : result.Outcome == PluginRuntimeMutationOutcome.Applied && active
                ? "built"
                : "failed";
        var diagnostics = preparation.Diagnostics
            .Concat(result.Diagnostics)
            .Concat(runtime.Snapshot.Diagnostics.Where(diagnostic =>
                diagnostic.PluginId is not null &&
                PluginIds.EqualsCanonical(diagnostic.PluginId, pluginId)))
            .ToArray();
        return new DotNetPluginBuildResult(
            outcome,
            preparation.Fingerprint,
            runtimeInfo?.State.ToString().ToLowerInvariant(),
            ToDiagnostics(diagnostics, runtimeInfo?.Blockers));
    }

    private static IReadOnlyList<DotNetPluginBuildDiagnostic> ToDiagnostics(
        IEnumerable<PluginDiagnostic> diagnostics,
        IReadOnlyList<PluginRuntimeBlocker>? blockers = null) =>
        diagnostics
            .Select(static diagnostic => new DotNetPluginBuildDiagnostic(
                diagnostic.Code,
                diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Message,
                diagnostic.Path,
                ReadString(diagnostic.Parameters, "phase"),
                ReadInt(diagnostic.Parameters, "line"),
                ReadInt(diagnostic.Parameters, "column")))
            .Concat((blockers ?? []).Select(static blocker => new DotNetPluginBuildDiagnostic(
                blocker.Code,
                "error",
                blocker.Message,
                null,
                "activation",
                null,
                null)))
            .Distinct()
            .OrderBy(static diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();

    private static string? ReadString(
        IReadOnlyDictionary<string, JsonElement> parameters,
        string name) =>
        parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(
        IReadOnlyDictionary<string, JsonElement> parameters,
        string name) =>
        parameters.TryGetValue(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
}
