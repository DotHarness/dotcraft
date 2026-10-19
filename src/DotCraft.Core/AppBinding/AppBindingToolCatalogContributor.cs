using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Tools;

namespace DotCraft.AppBinding;

public sealed class AppBindingToolCatalogContributor(
    AppBindingService controlPlane) : IMcpToolCatalogContributor
{
    public async ValueTask<IReadOnlyList<McpToolCatalog>> GetCatalogsAsync(
        ToolPlanningContext context,
        McpClientManager? manager,
        CancellationToken cancellationToken = default)
    {
        var catalogs = new List<McpToolCatalog>();
        var configs = manager is null ? [] : await manager.ListConfigsAsync(cancellationToken);
        var statuses = manager is null ? [] : await manager.ListStatusesAsync(cancellationToken);
        foreach (var binding in controlPlane.ListThreadBindings(context.DataPath, context.ThreadId)
                     .Where(static binding => binding.ChannelTarget is null && binding.ApprovedCapabilityRevision > 0
                                              && binding.State is AppBindingStates.Active or AppBindingStates.Syncing
                                                  or AppBindingStates.Offline or AppBindingStates.NeedsConfirmation)
                     .OrderBy(static binding => binding.BindingId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var live = statuses.FirstOrDefault(status => binding.State == AppBindingStates.Active
                && status.Origin.IsBinding && status.Origin.BindingId == binding.BindingId && status.StartupState == "ready");
            var inventory = live is null || manager is null ? null : await manager.GetInventoryAsync(live.Name, cancellationToken);
            var config = configs.FirstOrDefault(candidate => candidate.Name == live?.Name);
            var sourceIdentity = $"binding:{binding.BindingId}";
            var tools = binding.ApprovedTools.Select(tool => ToCatalogTool(binding.BindingId, tool)).ToArray();
            catalogs.Add(new McpToolCatalog(sourceIdentity, live?.Name ?? sourceIdentity, binding.AppId, "binding", tools,
                (id, rawName) =>
                {
                    var generation = inventory?.Generation;
                    var lease = new BindingLease(controlPlane, context.DataPath, binding.BindingId,
                        binding.AuthorityRevision, binding.ApprovedCapabilityRevision,
                        generation.HasValue ? manager : null, live?.Name, generation);
                    return new ToolRuntimeBinding(
                        new RuntimeBindingId($"{sourceIdentity}:{rawName}:{binding.AuthorityRevision}:{binding.ApprovedCapabilityRevision}:{generation}"),
                        id,
                        generation.HasValue && manager is not null && live is not null
                            ? new McpToolRuntime(manager, live.Name, rawName, generation.Value)
                            : OfflineRuntime.Instance,
                        lease, sourceIdentity, binding.AuthorityRevision,
                        timeout: config?.ToolTimeoutSec is > 0 ? TimeSpan.FromSeconds(config.ToolTimeoutSec.Value) : null);
                }));
        }
        return catalogs;
    }

    private static McpCatalogTool ToCatalogTool(string bindingId, AppBindingToolCapability tool)
    {
        var visibility = McpAppVisibility.None;
        if (tool.Visibility.Contains("model", StringComparer.Ordinal)) visibility |= McpAppVisibility.Model;
        if (tool.Visibility.Contains("app", StringComparer.Ordinal)) visibility |= McpAppVisibility.App;
        var annotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["appBinding/id"] = JsonSerializer.SerializeToElement(bindingId)
        };
        if (tool.Annotations["mcp/toolAnnotations"] is { } protocolAnnotations)
            annotations["mcp/toolAnnotations"] = JsonSerializer.SerializeToElement(protocolAnnotations);
        return new McpCatalogTool(tool.SourceToolName, tool.Description,
            JsonSerializer.SerializeToElement(tool.InputSchema),
            tool.OutputSchema is null ? null : JsonSerializer.SerializeToElement(tool.OutputSchema),
            new McpAppToolMetadata(tool.Ui is null ? null : new Uri(tool.Ui.ResourceUri, UriKind.Absolute), visibility),
            new ToolPolicyHints(
                RequiresApproval: tool.Annotations["requiresApproval"]?.GetValue<bool>() ?? true,
                ReadOnly: tool.Annotations["readOnly"]?.GetValue<bool>() ?? false,
                Destructive: tool.Annotations["destructive"]?.GetValue<bool>() ?? true,
                OpenWorld: tool.Annotations["openWorld"]?.GetValue<bool>() ?? true),
            annotations, tool.NamespaceDescription);
    }

    private sealed class BindingLease(
        AppBindingService controlPlane,
        string dataPath,
        string bindingId,
        long authorityRevision,
        long capabilityRevision,
        McpClientManager? manager,
        string? serverName,
        long? generation) : IToolBindingLease
    {
        public async ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            var binding = controlPlane.GetBinding(dataPath, bindingId);
            if (binding.ThreadId != context.ThreadId || binding.State == AppBindingStates.Revoked || binding.AuthorityRevision != authorityRevision
                                                        || binding.ApprovedCapabilityRevision != capabilityRevision)
                return ToolBindingLeaseResult.Unavailable("App binding authority changed.");
            if (binding.State != AppBindingStates.Active || manager is null || serverName is null || generation is null)
                return Offline();
            return await manager.GetGenerationAsync(serverName, cancellationToken) == generation
                ? ToolBindingLeaseResult.Available : Offline();
        }
    }

    private static ToolBindingLeaseResult Offline() => new(false,
        OfflineError);

    private static ToolError OfflineError => new(AppBindingErrorCodes.Offline,
        "The app binding is offline. Rebind the app before calling this tool.");

    private sealed class OfflineRuntime : IToolRuntime
    {
        public static OfflineRuntime Instance { get; } = new();

        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Failed(OfflineError));
    }
}
