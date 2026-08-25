using System.Text.Json.Nodes;
using DotCraft.Contributions;
using DotCraft.Tools;

namespace DotCraft.Runtime;

/// <summary>
/// Interposes the Host between a plugin's own <see cref="IToolSource"/> and the effective snapshot:
/// the definition is copied and the runtime binding is replaced by a Host-owned proxy.
/// </summary>
internal static class DotNetPluginToolProjection
{
    /// <summary>Wraps one plugin registration into a Host-owned registration keyed to the contributing generation.</summary>
    public static ToolRegistration Wrap(
        IContributionView contributions,
        PluginCallGateRegistry callGates,
        string pluginId,
        string generationId,
        ToolPlanningContext planning,
        ToolRegistration contributed,
        long revision)
    {
        var toolId = contributed.Definition.Id.SourceToolId.Value;
        var definitionId = new ToolDefinitionId(
            ToolSourceKind.PluginNative,
            pluginId,
            new SourceToolId(toolId));
        var proxy = new DotNetPluginToolProxy(
            contributions,
            callGates,
            pluginId,
            generationId,
            toolId,
            planning);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"{DotNetPluginToolSource.Id}:{pluginId}:{generationId}:{toolId}:{revision}"),
            definitionId,
            proxy,
            proxy,
            $"{DotNetPluginToolSource.Id}:{pluginId}:{generationId}",
            revision,
            contributed.Binding.Availability,
            contributed.Binding.Timeout);
        return new ToolRegistration(
            CopyDefinition(definitionId, pluginId, contributed.Definition),
            binding,
            ToolProjectionShape.StandardPair,
            contributed.Exposure,
            contributed.InvocationAudiences,
            CopyDeferred(contributed.Deferred));
    }

    /// <summary>Copies the model- and client-visible parts of a plugin result; Host-private channels are dropped.</summary>
    public static ToolExecutionResult CopyResult(ToolExecutionResult? result)
    {
        if (result == null)
            return Invalid("Plugin Tool returned no result.");
        if (result.Error is { } error)
        {
            return ToolExecutionResult.Failed(
                new ToolError(error.Code, error.Message, error.Parameters),
                result.Content);
        }

        return result.Success
            ? ToolExecutionResult.Succeeded(result.Content, result.StructuredContent)
            : Invalid("Plugin Tool reported failure without a stable error.");
    }

    public static ToolExecutionResult Unavailable(string pluginId, string generationId, string toolId) =>
        ToolExecutionResult.Failed(
            new ToolError(
                ToolErrorCodes.Unavailable,
                UnavailableMessage(pluginId, generationId, toolId)),
            $"Plugin Tool '{pluginId}/{toolId}' is no longer available.");

    public static string UnavailableMessage(string pluginId, string generationId, string toolId) =>
        $"Plugin Tool '{pluginId}/{toolId}' is no longer available from generation '{generationId}'.";

    /// <summary>Rebuilds the definition from Host-owned parts. Identity, provenance, projection, and policy scope are the Host's, not the plugin's.</summary>
    private static ToolDefinition CopyDefinition(
        ToolDefinitionId id,
        string pluginId,
        ToolDefinition contributed) =>
        new(
            id,
            contributed.Name,
            contributed.Description,
            contributed.InputSchema,
            contributed.OutputSchema,
            contributed.Annotations,
            new ToolPolicyHints(
                contributed.PolicyHints.RequiresApproval,
                contributed.PolicyHints.ReadOnly,
                contributed.PolicyHints.Destructive,
                contributed.PolicyHints.OpenWorld),
            CopyPresentation(contributed.Presentation),
            new ToolProvenance(ToolSourceKind.PluginNative, pluginId, "plugin"),
            contributed.NamespaceDescription,
            ToolPolicyScope.ProfileManaged);

    private static ToolPresentationDescriptor? CopyPresentation(ToolPresentationDescriptor? presentation) =>
        presentation == null
            ? null
            : new ToolPresentationDescriptor(new PresentationId(presentation.Id.Value), presentation.Options);

    private static DeferredToolDescriptor? CopyDeferred(DeferredToolDescriptor? deferred) =>
        deferred == null
            ? null
            : new DeferredToolDescriptor(deferred.Namespace, deferred.SearchText, deferred.NamespaceDescription);

    private static ToolExecutionResult Invalid(string message) =>
        ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.ResultInvalid, message), message);
}

/// <summary>
/// The Host-side stand-in for one plugin Tool. It holds identifiers and Host-owned planning inputs
/// only, and re-resolves the contributing source on every call so a frozen snapshot pins nothing.
/// </summary>
internal sealed class DotNetPluginToolProxy(
    IContributionView contributions,
    PluginCallGateRegistry callGates,
    string pluginId,
    string generationId,
    string toolId,
    ToolPlanningContext planning) : IToolRuntime, IToolBindingLease
{
    public ValueTask<ToolBindingLeaseResult> CheckAsync(
        ToolInvocationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!callGates.IsCallable(pluginId, generationId) || !HasLiveSource())
        {
            return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable(
                DotNetPluginToolProjection.UnavailableMessage(pluginId, generationId, toolId)));
        }

        return ValueTask.FromResult(ToolBindingLeaseResult.Available);
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        using var lease = callGates.TryEnterCall(pluginId, generationId);
        if (lease == null)
            return DotNetPluginToolProjection.Unavailable(pluginId, generationId, toolId);

        ToolRegistration? live;
        try
        {
            live = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(exception);
        }

        if (live == null)
            return DotNetPluginToolProjection.Unavailable(pluginId, generationId, toolId);

        try
        {
            var available = await live.Binding.Lease
                .CheckAsync(context, cancellationToken)
                .ConfigureAwait(false);
            if (available is not { IsAvailable: true })
                return DotNetPluginToolProjection.Unavailable(pluginId, generationId, toolId);

            // Copy-in as well as copy-out: the plugin never receives the Host's own argument object.
            var result = await live.Binding.Runtime
                .InvokeAsync(context, (JsonObject)arguments.DeepClone(), cancellationToken)
                .ConfigureAwait(false);
            return DotNetPluginToolProjection.CopyResult(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failed(exception);
        }
    }

    private static ToolExecutionResult Failed(Exception exception)
    {
        var message = PluginGeneration.CopyExceptionMessage(exception);
        return ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.ExecutionFailed, message), message);
    }

    private bool HasLiveSource()
    {
        foreach (var entry in contributions.ResolveEntries<IToolSource>(planning.ThreadId))
        {
            if (Owns(entry.Origin))
                return true;
        }

        return false;
    }

    /// <summary>Re-plans the contributing generation with the inputs that produced this proxy and takes the first Tool of that id.</summary>
    private async ValueTask<ToolRegistration?> ResolveAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in contributions.ResolveEntries<IToolSource>(planning.ThreadId))
        {
            if (!Owns(entry.Origin))
                continue;

            var contributed = await entry.Contribution
                .GetRegistrationsAsync(planning, cancellationToken)
                .ConfigureAwait(false);
            if (contributed == null)
                continue;

            foreach (var registration in contributed)
            {
                if (registration != null
                    && string.Equals(registration.Definition.Id.SourceToolId.Value, toolId, StringComparison.Ordinal))
                {
                    return registration;
                }
            }
        }

        return null;
    }

    private bool Owns(ContributionOrigin origin) =>
        origin.Kind == ContributionOriginKind.Plugin
        && string.Equals(origin.Name, pluginId, StringComparison.Ordinal)
        && string.Equals(origin.Generation, generationId, StringComparison.Ordinal);
}
