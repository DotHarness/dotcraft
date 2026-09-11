using System.Text.Json;
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
    internal ToolDefinition NativeDefinition => _definition;

    public ToolInvocationContext Prepare(ToolInvocationContext context, JsonObject arguments)
    {
        string? target = null;
        if (arguments.TryGetPropertyValue(RpcToolSchemaPostProcessor.TargetParameterName, out var targetNode)
            && (targetNode is not JsonValue value || !value.TryGetValue<string>(out target)
                || target is not ("local" or "remote")))
        {
            throw new RemoteToolHostException(ToolErrorCodes.InputInvalid, "'target' must be 'local' or 'remote'.");
        }

        if (target == "local")
            return context with { ExecutionLocation = new("local", context.WorkspacePath) };
        if (_remoteClient.TryGetRoute(context.ThreadId, out var route))
        {
            _remoteClient.TryGetConnectionSnapshot(context.ThreadId, out var snapshot);
            return context with { ExecutionLocation = new("remote", snapshot?.Environment.WorkspacePath, route) };
        }
        if (target == "remote")
            throw new RemoteToolHostException(RemoteToolErrorCodes.LeaseLost, "No remote workspace is connected.");
        return context with { ExecutionLocation = new("local", context.WorkspacePath) };
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        ToolInvocationContext context,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        context = context.ExecutionLocation is null ? Prepare(context, arguments) : context;
        var nativeArguments = (JsonObject)arguments.DeepClone();
        nativeArguments.Remove(RpcToolSchemaPostProcessor.TargetParameterName);
        if (context.ExecutionLocation!.Route is { } route)
        {
            if (!_remoteClient.TryGetRoute(context.ThreadId, out var current) || current != route)
                return ToolExecutionResult.Failed(new ToolError(RemoteToolErrorCodes.LeaseLost,
                    "The captured remote workspace is no longer connected."));
            return await _remoteClient.InvokeAsync(route, _definition, _contractHash, context,
                nativeArguments, cancellationToken).ConfigureAwait(false);
        }
        var result = await _localRuntime.InvokeAsync(context, nativeArguments, cancellationToken).ConfigureAwait(false);
        var meta = result.Meta is { ValueKind: JsonValueKind.Object } existing
            ? JsonNode.Parse(existing.GetRawText())!.AsObject() : new JsonObject();
        meta["executionTarget"] = "local";
        return new ToolExecutionResult(result.Success, result.Content, result.StructuredContent,
            JsonSerializer.SerializeToElement(meta), result.RawSourceResult, result.Error,
            result.ProviderResult, result.ContentItems, result.Directive);
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
                .Select(registration => registration.Binding.Runtime is RemoteRoutableToolRuntime routed
                    ? routed.NativeDefinition : registration.Definition)
                .ToArray());

        return registrations.Select(registration =>
        {
            if (!RemoteToolMetadata.IsRpcEligible(registration.Definition)
                || registration.Binding.Runtime is RemoteRoutableToolRuntime)
            {
                return registration;
            }

            var targetDescription = registration.Definition.Id is
                { Kind: ToolSourceKind.CoreNative, SourceId: "core-native", SourceToolId.Value: "WriteStdin" }
                ? "Use the target of the Exec call that created the terminal."
                : null;
            var projectedDefinition = RpcToolSchemaPostProcessor.Process(registration.Definition, targetDescription);
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
                projectedDefinition,
                routedBinding,
                registration.ProjectionShape,
                registration.Exposure,
                registration.InvocationAudiences,
                registration.Deferred,
                registration.ProviderFlatNameOverride);
        }).ToArray();
    }
}
