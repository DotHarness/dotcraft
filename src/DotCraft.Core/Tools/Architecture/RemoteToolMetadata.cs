namespace DotCraft.Tools;

/// <summary>Stable annotation names used by the Remote Tool Host profile.</summary>
public static class RemoteToolMetadata
{
    /// <summary>Annotation set to JSON <see langword="true"/> for RPC-eligible native definitions.</summary>
    public const string RpcEligibleAnnotation = "dotcraft/rpcEligible";

    /// <summary>Returns whether a native definition opts into remote routing; plugin bindings also require an accepted source.</summary>
    public static bool IsRpcEligible(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Id.Kind is ToolSourceKind.CoreNative or ToolSourceKind.PluginNative
               && definition.Annotations.TryGetValue(RpcEligibleAnnotation, out var value)
               && value.ValueKind == System.Text.Json.JsonValueKind.True;
    }

    public static bool IsRpcEligible(ToolRegistration registration) =>
        IsRpcEligible(registration.Definition)
        && (registration.Definition.Id.Kind == ToolSourceKind.CoreNative
            || registration.Binding.Runtime is IRemoteToolSourceBinding
            || registration.Binding.Runtime is RemoteRoutableToolRuntime { Source: not null });

    public static ToolDefinition NativeDefinition(ToolRegistration registration) =>
        registration.Binding.Runtime is RemoteRoutableToolRuntime routed ? routed.NativeDefinition : registration.Definition;

    public static IRemoteToolSourceBinding? SourceBinding(ToolRegistration registration) =>
        registration.Binding.Runtime is RemoteRoutableToolRuntime routed
            ? routed.Source : registration.Binding.Runtime as IRemoteToolSourceBinding;
}
