using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.Tools;
using DotCraft.AppServer;

namespace DotCraft.AppBinding;

/// <summary>Projects active conversation bindings as server-authoritative plugin-native tools.</summary>
public sealed class ManagedSocialToolSource(
    AppBindingService controlPlane,
    IChannelRuntimeRegistry runtimeRegistry,
    ChannelToolRegistrationService registrationService) : IToolSource, IThreadScopedToolSource
{
    private static readonly HashSet<string> ReservedTargets = new(StringComparer.Ordinal)
    {
        "target", "deliverytarget", "channelcontext", "chatid", "groupid",
        "conversationid", "conversationkind", "touserid", "recipient", "destination"
    };

    public string SourceId => "managed-social";
    public int Priority => 70;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context, CancellationToken cancellationToken = default)
    {
        var craftPath = Path.Combine(context.WorkspacePath, ".craft");
        var registrations = new List<ToolRegistration>();
        foreach (var binding in controlPlane.ListThreadBindings(craftPath, context.ThreadId)
                     .Where(binding => binding.State == AppBindingStates.Active && binding.SocialTarget != null))
        {
            var target = binding.SocialTarget!;
            if (!runtimeRegistry.TryGet(target.ChannelName, out var runtime) || runtime == null) continue;
            var descriptors = registrationService.GetRegisteredTools(
                runtime,
                out _,
                out var adapterConnection);
            foreach (var descriptor in descriptors)
            {
                if (descriptor.InputSchema is not JsonObject schema || DeclaresReservedTarget(schema)) continue;
                registrations.Add(Create(craftPath, binding, target, runtime, adapterConnection, descriptor));
            }
        }
        return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(registrations);
    }

    public ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    private ToolRegistration Create(
        string craftPath,
        AppBindingSnapshot binding,
        SocialChannelTarget target,
        IChannelRuntime runtime,
        AppServerConnection? adapterConnection,
        ChannelToolSpec descriptor)
    {
        var sourceId = $"social:{binding.BindingId}";
        var definitionId = new ToolDefinitionId(ToolSourceKind.PluginNative, sourceId, new SourceToolId(descriptor.Name));
        var definition = new ToolDefinition(
            definitionId,
            new ToolName(target.ChannelName.ToLowerInvariant(), descriptor.Name),
            descriptor.Description,
            JsonSerializer.SerializeToElement(descriptor.InputSchema),
            descriptor.OutputSchema == null ? null : JsonSerializer.SerializeToElement(descriptor.OutputSchema),
            policyHints: new ToolPolicyHints(RequiresApproval: true, OpenWorld: true),
            provenance: new ToolProvenance(ToolSourceKind.PluginNative, sourceId, "social-binding"));
        var runtimeBinding = new ToolRuntimeBinding(
            new RuntimeBindingId(adapterConnection == null
                ? $"social:{binding.BindingId}:{descriptor.Name}:{binding.AuthorityRevision}"
                : $"social:{binding.BindingId}:{descriptor.Name}:{binding.AuthorityRevision}:{adapterConnection.ConnectionId:N}"),
            definitionId,
            new SocialRuntime(runtime, adapterConnection, target, descriptor.Name),
            new SocialLease(
                controlPlane,
                runtimeRegistry,
                runtime,
                adapterConnection,
                craftPath,
                binding.BindingId,
                binding.AuthorityRevision,
                target),
            $"social-binding:{binding.BindingId}", binding.AuthorityRevision);
        return new ToolRegistration(definition, runtimeBinding, ToolProjectionShape.StandardPair,
            descriptor.DeferLoading == true ? ToolExposure.Deferred : ToolExposure.Direct,
            ToolInvocationAudience.Model | ToolInvocationAudience.Host,
            descriptor.DeferLoading == true
                ? new DeferredToolDescriptor(definition.Name.Namespace!, $"{descriptor.Name} {descriptor.Description}") : null);
    }

    private static bool DeclaresReservedTarget(JsonObject schema) =>
        schema["properties"] is JsonObject properties && properties.Any(pair => IsReservedTarget(pair.Key));

    private static bool IsReservedTarget(string name) =>
        ReservedTargets.Contains(new string(name.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray()));

    private sealed class SocialLease(
        AppBindingService controlPlane,
        IChannelRuntimeRegistry runtimeRegistry,
        IChannelRuntime runtime,
        AppServerConnection? adapterConnection,
        string craftPath,
        string bindingId,
        long revision,
        SocialChannelTarget target) : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(ToolInvocationContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var live = controlPlane.GetBinding(craftPath, bindingId);
                if (live.State != AppBindingStates.Active || live.AuthorityRevision != revision
                    || live.SocialTarget?.DeliveryTarget != target.DeliveryTarget)
                    return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("Social binding authority changed."));
                if (!runtimeRegistry.TryGet(target.ChannelName, out var current)
                    || !ReferenceEquals(current, runtime)
                    || !runtime.IsReady
                    || adapterConnection != null
                        && (runtime is not IAdapterChannelToolRuntime adapterRuntime
                            || !ReferenceEquals(adapterRuntime.ChannelToolConnection, adapterConnection)))
                {
                    return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("Social channel is unavailable."));
                }
                return ValueTask.FromResult(ToolBindingLeaseResult.Available);
            }
            catch { return ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("Social binding is unavailable.")); }
        }
    }

    private sealed class SocialRuntime(
        IChannelRuntime runtime,
        AppServerConnection? adapterConnection,
        SocialChannelTarget target,
        string toolName) : IToolRuntime
    {
        public async ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            var overrideName = arguments.Select(pair => pair.Key).FirstOrDefault(IsReservedTarget);
            if (overrideName != null)
                return ToolExecutionResult.Failed(new ToolError("AppBindingTargetOverride",
                    $"Argument '{overrideName}' cannot override the bound social target."));
            if (!runtime.IsReady)
                return ToolExecutionResult.Failed(new ToolError(AppBindingErrorCodes.Offline,
                    $"Channel '{target.ChannelName}' is offline."));
            var request = new ChannelToolInvocationRequest
            {
                ThreadId = context.ThreadId, TurnId = context.TurnId ?? string.Empty, CallId = context.CallId,
                Tool = toolName, Arguments = arguments,
                Context = new ChannelToolInvocationContext
                {
                    ChannelName = target.ChannelName, ChannelContext = target.DeliveryTarget,
                    SenderId = target.BoundBy?.PlatformUserId,
                    GroupId = target.ConversationKind.Equals("user", StringComparison.OrdinalIgnoreCase)
                        ? null : target.DeliveryTarget
                }
            };
            var result = adapterConnection != null && runtime is IAdapterChannelToolRuntime adapterRuntime
                ? await adapterRuntime.ExecuteToolAsync(adapterConnection, request, cancellationToken)
                : await runtime.ExecuteToolAsync(request, cancellationToken);
            var text = string.Join("\n", result.ContentItems?.Where(item => item.Type == "text")
                .Select(item => item.Text).Where(value => !string.IsNullOrWhiteSpace(value)) ?? []);
            if (!result.Success)
                return ToolExecutionResult.Failed(new ToolError(result.ErrorCode ?? "ChannelToolFailed",
                    result.ErrorMessage ?? "The channel tool failed."), text);
            return ToolExecutionResult.Succeeded(text,
                result.StructuredResult == null ? null : JsonSerializer.SerializeToElement(result.StructuredResult));
        }
    }
}
