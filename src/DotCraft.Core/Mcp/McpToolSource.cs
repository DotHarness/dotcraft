using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.Mcp;

public sealed class McpToolSource(
    McpClientManager? manager,
    AppConfig config,
    IEnumerable<IMcpToolCatalogContributor>? contributors = null) : IToolSource
{
    public string SourceId => "mcp";
    public int Priority => 80;

    public async ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var catalogs = new List<McpToolCatalog>();
        if (manager is not null)
            catalogs.AddRange(await McpLiveToolCatalogReader.ReadAsync(manager, cancellationToken));
        foreach (var contributor in contributors ?? [])
            catalogs.AddRange(await contributor.GetCatalogsAsync(context, manager, cancellationToken));

        var entries = catalogs.SelectMany(catalog => catalog.Tools.Select(tool => (Catalog: catalog, Tool: tool))).ToArray();
        var identities = McpToolNaming.NormalizeBatch(entries.Select(entry => new McpToolIdentityInput(
            entry.Catalog.RuntimeName, entry.Catalog.DeclaredName, entry.Tool.SourceToolName, entry.Catalog.SourceIdentity)));
        var deferredConfig = config.Tools.DeferredLoading;
        var alwaysLoaded = deferredConfig.AlwaysLoadedTools.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = new List<ToolRegistration>(entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var (catalog, tool) = entries[index];
            var sourceId = catalog.Origin == "binding" ? catalog.SourceIdentity : catalog.RuntimeName;
            var id = new ToolDefinitionId(ToolSourceKind.Mcp, sourceId, new SourceToolId(tool.SourceToolName));
            var annotations = tool.Annotations.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            annotations[McpAppMetadataParser.ToolAnnotationKey] = McpAppMetadataParser.ToDefinitionAnnotation(tool.AppMetadata);
            var definition = new ToolDefinition(
                id, identities[index].ToolName, tool.Description,
                ToolSchemaSanitizer.SanitizeJsonSchema(tool.InputSchema), tool.ReturnSchema,
                annotations: annotations, policyHints: tool.PolicyHints,
                provenance: new ToolProvenance(ToolSourceKind.Mcp, sourceId, catalog.Origin),
                namespaceDescription: tool.NamespaceDescription);
            var isDeferred = tool.AppMetadata.Visibility.HasFlag(McpAppVisibility.Model)
                             && deferredConfig.Strategy != AppConfig.DeferredLoadingStrategy.Off
                             && catalog.Tools.Count >= deferredConfig.DeferThreshold
                             && !alwaysLoaded.Contains(tool.SourceToolName);
            var (exposure, audiences) = ResolvePublication(tool.AppMetadata, isDeferred);
            registrations.Add(new ToolRegistration(definition, catalog.BindRuntime(id, tool.SourceToolName),
                ToolProjectionShape.McpLifecycle, exposure, audiences,
                deferred: isDeferred ? new DeferredToolDescriptor(definition.Name.Namespace!,
                    $"{tool.SourceToolName} {tool.Description}", tool.NamespaceDescription) : null));
        }
        return registrations;
    }

    internal static (ToolExposure Exposure, ToolInvocationAudience Audiences) ResolvePublication(
        McpAppToolMetadata metadata,
        bool deferred)
    {
        var modelVisible = metadata.Visibility.HasFlag(McpAppVisibility.Model);
        var exposure = !modelVisible ? ToolExposure.Hidden : deferred ? ToolExposure.Deferred : ToolExposure.Direct;
        var audiences = ToolInvocationAudience.Host;
        if (modelVisible) audiences |= ToolInvocationAudience.Model;
        if (metadata.Visibility.HasFlag(McpAppVisibility.App)) audiences |= ToolInvocationAudience.App;
        return (exposure, audiences);
    }
}
