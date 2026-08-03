using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.AppBinding;

/// <summary>Publishes approved schema-stable stubs while a binding is offline or awaiting confirmation.</summary>
public sealed class AppBindingOfflineToolSource(AppBindingService controlPlane)
    : IToolSource, IThreadScopedToolSource
{
    public string SourceId => "app-binding-offline";
    public int Priority => 81;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context,
        CancellationToken cancellationToken = default)
    {
        var craftPath = Path.Combine(context.WorkspacePath, ".craft");
        var registrations = controlPlane.ListThreadBindings(craftPath, context.ThreadId)
            .Where(binding => binding.ApprovedCapabilityRevision > 0
                              && (binding.State is AppBindingStates.Syncing
                                  or AppBindingStates.Offline
                                  or AppBindingStates.NeedsConfirmation))
            .SelectMany(binding => binding.ApprovedTools.Select(tool => Create(craftPath, binding, tool)))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(registrations);
    }

    public ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    private ToolRegistration Create(string craftPath, AppBindingSnapshot binding, AppBindingToolCapability tool)
    {
        var sourceId = $"binding:{binding.BindingId}";
        var definitionId = new ToolDefinitionId(ToolSourceKind.Mcp, sourceId, new SourceToolId(tool.Name));
        var annotations = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["appBinding/state"] = JsonSerializer.SerializeToElement(binding.State),
            ["appBinding/id"] = JsonSerializer.SerializeToElement(binding.BindingId)
        };
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(tool.Namespace, tool.Name),
            $"{tool.Name} is temporarily unavailable because its app binding is offline.",
            JsonSerializer.SerializeToElement(tool.InputSchema),
            annotations: annotations,
            policyHints: new ToolPolicyHints(
                RequiresApproval: tool.Annotations["requiresApproval"]?.GetValue<bool?>() ?? true,
                ReadOnly: tool.Annotations["readOnly"]?.GetValue<bool?>() ?? false,
                Destructive: tool.Annotations["destructive"]?.GetValue<bool?>() ?? true,
                OpenWorld: tool.Annotations["openWorld"]?.GetValue<bool?>() ?? true),
            provenance: new ToolProvenance(ToolSourceKind.Mcp, sourceId, "binding"));
        var bindingRuntime = new ToolRuntimeBinding(
            new RuntimeBindingId($"app-binding-offline:{binding.BindingId}:{tool.Name}:{binding.AuthorityRevision}"),
            definitionId,
            OfflineRuntime.Instance,
            new OfflineLease(controlPlane, craftPath, binding.BindingId, binding.AuthorityRevision, binding.ApprovedCapabilityRevision),
            $"app-binding:{binding.BindingId}",
            binding.AuthorityRevision);
        var audiences = ToolInvocationAudience.Host;
        if (tool.Visibility.Contains("model", StringComparer.Ordinal)) audiences |= ToolInvocationAudience.Model;
        return new ToolRegistration(definition, bindingRuntime, ToolProjectionShape.StandardPair,
            audiences.HasFlag(ToolInvocationAudience.Model) ? ToolExposure.Direct : ToolExposure.Hidden, audiences);
    }

    private sealed class OfflineLease(
        AppBindingService controlPlane,
        string craftPath,
        string bindingId,
        long authorityRevision,
        long capabilityRevision) : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var binding = controlPlane.GetBinding(craftPath, bindingId);
                if (binding.AuthorityRevision != authorityRevision
                    || binding.ApprovedCapabilityRevision != capabilityRevision
                    || binding.State == AppBindingStates.Revoked)
                    return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("App binding authority changed."));
            }
            catch
            {
                return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("App binding authority is unavailable."));
            }
            return ValueTask.FromResult(new ToolBindingLeaseResult(false,
                new ToolError(AppBindingErrorCodes.Offline,
                    "The app binding is offline. Rebind the app before calling this tool.")));
        }
    }

    private sealed class OfflineRuntime : IToolRuntime
    {
        public static OfflineRuntime Instance { get; } = new();
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Failed(new ToolError(AppBindingErrorCodes.Offline,
                "The app binding is offline. Rebind the app before calling this tool.")));
    }
}
