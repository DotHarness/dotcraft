using System.Text.Json.Nodes;

namespace DotCraft.Tools;

/// <summary>
/// Keeps a native binding stable while selecting local or remote execution from runtime-only
/// thread route state at invocation time.
/// </summary>
internal sealed class RemoteRoutableToolRuntime(
    ToolDefinition definition,
    IToolRuntime localRuntime,
    IRemoteToolHostClient remoteClient) : IToolRuntime
{
    private readonly ToolDefinition _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    private readonly IToolRuntime _localRuntime = localRuntime ?? throw new ArgumentNullException(nameof(localRuntime));
    private readonly IRemoteToolHostClient _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
    private readonly string _contractHash = RemoteToolContractHasher.Compute(definition);

    public ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        return _remoteClient.TryGetRoute(context.ThreadId, out var route)
            ? _remoteClient.InvokeAsync(route, _definition, _contractHash, context, arguments, cancellationToken)
            : _localRuntime.InvokeAsync(context, arguments, cancellationToken);
    }
}

/// <summary>Wraps only trusted RPC-eligible registrations without changing definition identity.</summary>
internal static class RemoteToolRegistrationRouter
{
    public static IReadOnlyList<ToolRegistration> Wrap(
        IReadOnlyList<ToolRegistration> registrations,
        IRemoteToolHostClient? remoteClient)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        if (remoteClient is null)
            return registrations;

        remoteClient.UpdateRemoteToolDefinitions(
            registrations
                .Where(registration => RemoteToolMetadata.IsRpcEligible(registration.Definition))
                .Select(registration => registration.Definition)
                .ToArray());

        return registrations.Select(registration =>
        {
            if (!RemoteToolMetadata.IsRpcEligible(registration.Definition)
                || registration.Binding.Runtime is RemoteRoutableToolRuntime)
            {
                return registration;
            }

            var binding = registration.Binding;
            var routedBinding = new ToolRuntimeBinding(
                binding.Id,
                binding.DefinitionId,
                new RemoteRoutableToolRuntime(registration.Definition, binding.Runtime, remoteClient),
                binding.Lease,
                binding.AuthorityReference,
                binding.Revision,
                binding.Availability,
                binding.Timeout);
            return new ToolRegistration(
                registration.Definition,
                routedBinding,
                registration.ProjectionShape,
                registration.Exposure,
                registration.InvocationAudiences,
                registration.Deferred,
                registration.ProviderFlatNameOverride);
        }).ToArray();
    }
}
